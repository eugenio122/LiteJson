using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Interop.UIAutomationClient;
using LiteJson.Core;
using LiteJson.Models;
using LiteJson.Diagnostics;
using LiteJson.UI;
using LiteTools.Interfaces;

namespace LiteJson.Plugin
{
    public partial class LiteJsonPlugin : ILitePlugin
    {
        private LiteJsonManager _jsonManager;
        private IUIAutomation _automation;
        private ILiteHostContext _hostContext;
        private IEventBus _eventBus;
        private JsonSettingsUI _settingsUI;

        private string _baseOutputDirectory;
        private List<ExtractionPayload> _scenarioSteps;
        private readonly object _lockObj = new object();
        private string _lastScenarioName = null;
        private bool _isRecording = true;

        private readonly ConcurrentQueue<(ExtractionPayload Step, IntPtr Hwnd)> _hydrationQueue = new();
        private CancellationTokenSource _workerCts;
        private Thread _hydrationWorkerThread;
        private Thread _trackerWorkerThread;

        // Cache volátil de triggers de menu (TargetElementData dos prints anteriores).
        // Acumula enquanto o usuário navega por menus suspensos e é consumido +
        // limpo no primeiro click. NÃO é persistido no JSON.
        private readonly List<TargetElementData> _hoverChainCache = new();

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        public string Name => "LiteJson Semantic Engine";
        public string Version => "2.1.0";

        public void Initialize(ILiteHostContext hostContext, IEventBus eventBus, string currentLanguage)
        {
            _hostContext = hostContext;
            _eventBus = eventBus;
            LiteJsonLanguageManager.CurrentLanguage = currentLanguage;

            _jsonManager = new LiteJsonManager();
            _automation = new CUIAutomation();

            _scenarioSteps = new List<ExtractionPayload>();
            _baseOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LiteJson_Output");

            _eventBus.Subscribe<ImageCapturedEvent>(OnImageCaptured);
            _eventBus.Subscribe<ImageCaptureCanceledEvent>(OnImageCaptureCanceled);

            _eventBus.Subscribe<RecordingStateChangedEvent>(e => _isRecording = e.IsRecording);
            _eventBus.Subscribe<ThemeChangedEvent>(e => _settingsUI?.ApplyTheme(e.IsDarkMode));
            _eventBus.Subscribe<SessionRestartedEvent>(OnSessionRestarted);
            _eventBus.Subscribe<StepDeletedEvent>(OnStepDeleted);
            _eventBus.Subscribe<StepRestoredEvent>(OnStepRestored);
            _eventBus.Subscribe<StepsReorderedEvent>(OnStepsReordered);
            _eventBus.Subscribe<StepMetadataChangedEvent>(OnStepMetadataChanged);

            StartWorkers();
        }

        private void StartWorkers()
        {
            _workerCts = new CancellationTokenSource();

            _hydrationWorkerThread = new Thread(() => HydrationWorkerLoop(_workerCts.Token));
            _hydrationWorkerThread.SetApartmentState(ApartmentState.STA);
            _hydrationWorkerThread.IsBackground = true;
            _hydrationWorkerThread.Start();

            _trackerWorkerThread = new Thread(() => TrackerWorkerLoop(_workerCts.Token));
            _trackerWorkerThread.IsBackground = true;
            _trackerWorkerThread.Start();
        }

        public UserControl GetSettingsUI() =>
            _settingsUI = new JsonSettingsUI(_hostContext.GetSessionMetadata("IsDarkMode") as bool? ?? false);

        public void Shutdown()
        {
            _workerCts?.Cancel();
            _jsonManager = null;
        }
    }
}