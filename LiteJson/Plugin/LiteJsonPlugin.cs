using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
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
    public class LiteJsonPlugin : ILitePlugin
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

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        public string Name => "LiteJson Semantic Engine";
        public string Version => "2.0.0";

        public void Initialize(ILiteHostContext hostContext, IEventBus eventBus, string currentLanguage)
        {
            _hostContext = hostContext; _eventBus = eventBus;
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

            StartHydrationWorker();
        }

        private void StartHydrationWorker()
        {
            _workerCts = new CancellationTokenSource();
            _hydrationWorkerThread = new Thread(() => HydrationWorkerLoop(_workerCts.Token));
            _hydrationWorkerThread.SetApartmentState(ApartmentState.STA);
            _hydrationWorkerThread.IsBackground = true;
            _hydrationWorkerThread.Start();
        }

        private void HydrationWorkerLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_hydrationQueue.TryDequeue(out var item))
                {
                    try
                    {
                        _hostContext.SetSessionMetadata("LiteJson_IsProcessing", true);
                        var cfg = LiteJsonConfig.Load();

                        if (_hydrationQueue.Count > cfg.MaxHydrationQueueSize)
                        {
                            LiteLogger.Debug($"[Worker] Purga de fila: ignorando UIA do passo {item.Step.StepId}");
                            item.Step.IsHydrated = false;
                        }
                        else
                        {
                            _jsonManager.HydrateContext(item.Step.ObservedContext, item.Hwnd);
                            item.Step.IsHydrated = true;
                        }

                        lock (_lockObj) { SaveJsonToDisk(GetCurrentScenarioDirectory()); }
                    }
                    catch (Exception ex) { LiteLogger.Error("[Worker] Falha crítica.", ex); }
                    finally
                    {
                        if (_hydrationQueue.IsEmpty) _hostContext.SetSessionMetadata("LiteJson_IsProcessing", false);
                    }
                }
                else
                {
                    Thread.Sleep(500);
                }
            }
        }

        public void PreparePendingStep(string stepId = null)
        {
            if (!LiteJsonConfig.Load().IsEnabled || !_isRecording) return;

            lock (_lockObj)
            {
                UpdateScenarioName();

                // CORREÇÃO: Resgatamos o elemento focado e calculamos o centro X, Y matemático
                IUIAutomationElement focused = null;
                int centerX = 0, centerY = 0;
                IntPtr hwnd = IntPtr.Zero;

                try
                {
                    focused = _automation.GetFocusedElement();
                    if (focused != null)
                    {
                        var rect = focused.CurrentBoundingRectangle;
                        centerX = rect.left + (rect.right - rect.left) / 2;
                        centerY = rect.top + (rect.bottom - rect.top) / 2;
                        hwnd = focused.CurrentNativeWindowHandle != 0 ? (IntPtr)focused.CurrentNativeWindowHandle : GetForegroundWindow();
                    }
                    else
                    {
                        hwnd = GetForegroundWindow();
                    }
                }
                catch { hwnd = GetForegroundWindow(); }

                // Agora passamos o elemento real e as coordenadas reais (não mais null, 0, 0)
                var (center, context) = _jsonManager.ExtractMainStepData(focused, centerX, centerY);

                var payload = new ExtractionPayload
                {
                    StepId = stepId ?? Guid.NewGuid().ToString(),
                    StepName = "Aguardando Print...",
                    TriggerType = "Intentional_Print",
                    PendingConfirmation = true,
                    CapturedData = center,
                    ObservedContext = context,
                    IsHydrated = false
                };

                _scenarioSteps.Add(payload);
                _hydrationQueue.Enqueue((payload, hwnd));
                RecalculateIndices();
                SaveJsonToDisk(GetCurrentScenarioDirectory());
            }
        }

        private void OnImageCaptured(ImageCapturedEvent e)
        {
            var config = LiteJsonConfig.Load();
            if (!config.IsEnabled || !_isRecording || e.CapturedImage == null) return;

            Bitmap clonedImage;
            lock (e.CapturedImage) { clonedImage = new Bitmap(e.CapturedImage); }

            // Preparamos os dados principais antes do Task.Run para garantir que o COM funcione na thread correta
            var pendingStep = _scenarioSteps.LastOrDefault(s => s.PendingConfirmation);
            ExtractionPayload fallbackPayload = null;
            IntPtr fallbackHwnd = IntPtr.Zero;

            if (pendingStep == null)
            {
                IUIAutomationElement focused = null;
                int centerX = 0, centerY = 0;
                try
                {
                    focused = _automation.GetFocusedElement();
                    if (focused != null)
                    {
                        var rect = focused.CurrentBoundingRectangle;
                        centerX = rect.left + (rect.right - rect.left) / 2;
                        centerY = rect.top + (rect.bottom - rect.top) / 2;
                        fallbackHwnd = focused.CurrentNativeWindowHandle != 0 ? (IntPtr)focused.CurrentNativeWindowHandle : GetForegroundWindow();
                    }
                    else
                    {
                        fallbackHwnd = GetForegroundWindow();
                    }
                }
                catch { fallbackHwnd = GetForegroundWindow(); }

                var extraction = _jsonManager.ExtractMainStepData(focused, centerX, centerY);

                fallbackPayload = new ExtractionPayload
                {
                    StepId = !string.IsNullOrEmpty(e.StepId) ? e.StepId : Guid.NewGuid().ToString(),
                    StepName = "Nova Ação",
                    TriggerType = "Intentional_Print",
                    ContextImage = "", // Será atualizado na Task
                    PendingConfirmation = false,
                    CapturedData = extraction.Center,
                    ObservedContext = extraction.Context,
                    IsHydrated = false
                };
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500);
                    lock (_lockObj)
                    {
                        UpdateScenarioName();
                        string stepId = !string.IsNullOrEmpty(e.StepId) ? e.StepId : Guid.NewGuid().ToString();
                        string currentDir = GetCurrentScenarioDirectory();
                        string imageName = $"Step_{stepId.Substring(0, 8)}_Context.jpg";

                        clonedImage.Save(Path.Combine(currentDir, imageName), ImageFormat.Jpeg);
                        clonedImage.Dispose();

                        if (pendingStep != null)
                        {
                            pendingStep.PendingConfirmation = false;
                            pendingStep.ContextImage = imageName;
                            pendingStep.StepId = stepId;
                            pendingStep.StepName = "Nova Ação";
                        }
                        else if (fallbackPayload != null)
                        {
                            fallbackPayload.StepId = stepId;
                            fallbackPayload.ContextImage = imageName;
                            _scenarioSteps.Add(fallbackPayload);
                            _hydrationQueue.Enqueue((fallbackPayload, fallbackHwnd));
                        }

                        RecalculateIndices();
                        SaveJsonToDisk(currentDir);
                    }
                }
                catch (Exception ex) { LiteLogger.Error("Erro ao processar imagem capturada.", ex); }
            });
        }

        private void OnImageCaptureCanceled(ImageCaptureCanceledEvent e)
        {
            lock (_lockObj)
            {
                var pending = _scenarioSteps.Where(s => s.PendingConfirmation).ToList();
                foreach (var p in pending) _scenarioSteps.Remove(p);
                RecalculateIndices();
                SaveJsonToDisk(GetCurrentScenarioDirectory());
            }
        }

        private void UpdateScenarioName()
        {
            string name = _hostContext.GetSessionMetadata("CurrentTestCaseName") as string ?? "Cenario_Global";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (_lastScenarioName != name) { _scenarioSteps.Clear(); _lastScenarioName = name; }
        }

        private string GetCurrentScenarioDirectory()
        {
            var cfg = LiteJsonConfig.Load();
            string root = !string.IsNullOrWhiteSpace(cfg.CustomOutputPath) ? cfg.CustomOutputPath : _baseOutputDirectory;
            string folder = Path.Combine(root, _lastScenarioName ?? "Cenario_Global");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return folder;
        }

        private void SaveJsonToDisk(string dir)
        {
            try
            {
                string lflow = _hostContext.GetSessionMetadata("CurrentLFlowPath") as string;
                string name = !string.IsNullOrEmpty(lflow) ? Path.GetFileNameWithoutExtension(lflow) + ".json" : "Scenario_Data.json";
                string jsonPath = Path.Combine(dir, name);
                string lockPath = jsonPath + ".lock";

                if (!_hydrationQueue.IsEmpty) File.WriteAllText(lockPath, "HIDRATANDO");

                var opt = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                string tmp = jsonPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_scenarioSteps, opt));
                if (File.Exists(jsonPath)) File.Delete(jsonPath);
                File.Move(tmp, jsonPath);

                if (_hydrationQueue.IsEmpty && File.Exists(lockPath)) File.Delete(lockPath);
            }
            catch { }
        }

        private void OnSessionRestarted(SessionRestartedEvent e) { lock (_lockObj) { _scenarioSteps.Clear(); _lastScenarioName = null; } }
        private void OnStepDeleted(StepDeletedEvent e) { lock (_lockObj) { var s = _scenarioSteps.FirstOrDefault(x => x.StepId == e.StepId); if (s != null) { s.IsActive = false; RecalculateIndices(); SaveJsonToDisk(GetCurrentScenarioDirectory()); } } }
        private void OnStepRestored(StepRestoredEvent e) { lock (_lockObj) { var s = _scenarioSteps.FirstOrDefault(x => x.StepId == e.StepId); if (s != null) { s.IsActive = true; RecalculateIndices(); SaveJsonToDisk(GetCurrentScenarioDirectory()); } } }
        private void OnStepsReordered(StepsReorderedEvent e) { lock (_lockObj) { var newList = e.NewOrderIds.Select(id => _scenarioSteps.FirstOrDefault(x => x.StepId == id)).Where(x => x != null).ToList(); _scenarioSteps = newList; RecalculateIndices(); SaveJsonToDisk(GetCurrentScenarioDirectory()); } }
        private void OnStepMetadataChanged(StepMetadataChangedEvent e) { lock (_lockObj) { var s = _scenarioSteps.FirstOrDefault(x => x.StepId == e.StepId); if (s != null) { s.IsEvidenceOnly = e.IsEvidenceOnly; if (!string.IsNullOrEmpty(e.NewName)) s.StepName = e.NewName; RecalculateIndices(); SaveJsonToDisk(GetCurrentScenarioDirectory()); } } }
        private void RecalculateIndices() { int c = 1; foreach (var s in _scenarioSteps) { if (!s.IsActive || s.IsEvidenceOnly || s.PendingConfirmation) s.StepIndex = null; else s.StepIndex = c++; } }

        public UserControl GetSettingsUI() => _settingsUI = new JsonSettingsUI(_hostContext.GetSessionMetadata("IsDarkMode") as bool? ?? false);

        public void Shutdown()
        {
            _workerCts?.Cancel();
            _jsonManager = null;
        }
    }
}