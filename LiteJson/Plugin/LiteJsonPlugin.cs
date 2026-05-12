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
        private Thread _trackerWorkerThread;

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        public string Name => "LiteJson Semantic Engine";
        public string Version => "2.0.0";

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

        // =========================================================================
        // HELPER: Anti-Flood da Digitação no C#
        // Garante que o Live Drain não suje a trilha com pedaços de digitação!
        // =========================================================================
        private void AppendInteractionsWithMerge(ExtractionPayload step, List<InteractionBreadcrumb> newInteractions)
        {
            if (newInteractions == null || newInteractions.Count == 0) return;

            foreach (var newInteraction in newInteractions)
            {
                if (newInteraction.InteractionType == "input")
                {
                    var lastItem = step.InteractionTrail.LastOrDefault();
                    if (lastItem != null && lastItem.InteractionType == "input" &&
                        lastItem.ElementId == newInteraction.ElementId &&
                        lastItem.TagName == newInteraction.TagName)
                    {
                        // Se o usuário ainda está digitando no mesmo campo, APENAS ATUALIZA o último registro!
                        lastItem.Timestamp = newInteraction.Timestamp;
                        lastItem.Value = newInteraction.Value;

                        if (lastItem.WebDriver_BiDi != null && lastItem.WebDriver_BiDi.ElementData != null &&
                            newInteraction.WebDriver_BiDi != null && newInteraction.WebDriver_BiDi.ElementData != null)
                        {
                            lastItem.WebDriver_BiDi.ElementData.Value = newInteraction.WebDriver_BiDi.ElementData.Value;
                        }

                        continue; // Pula a adição de uma nova linha
                    }
                }

                step.InteractionTrail.Add(newInteraction);
            }
        }
        // =========================================================================

        private void TrackerWorkerLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var config = LiteJsonConfig.Load();
                    if (config.IsEnabled && _isRecording && config.Target == TargetEngine.WebUniversal)
                    {
                        _jsonManager.EnsureClickTrackerInjected(IntPtr.Zero);

                        var liveTrail = _jsonManager.RetrieveAndClearInteractionTrail(IntPtr.Zero);
                        if (liveTrail != null && liveTrail.Count > 0)
                        {
                            lock (_lockObj)
                            {
                                var currentStep = _scenarioSteps.LastOrDefault(s => s.IsActive);
                                if (currentStep != null)
                                {
                                    // Chama o novo Helper que protege contra o Flood!
                                    AppendInteractionsWithMerge(currentStep, liveTrail);

                                    _hydrationQueue.Enqueue((currentStep, GetForegroundWindow()));
                                    SaveJsonToDisk(GetCurrentScenarioDirectory());
                                }
                            }
                        }
                    }
                }
                catch { }

                Thread.Sleep(1000);
            }
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
                            _jsonManager.HydrateTrail(item.Step.InteractionTrail, item.Hwnd);
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
                    else { hwnd = GetForegroundWindow(); }
                }
                catch { hwnd = GetForegroundWindow(); }

                var finalFlush = _jsonManager.RetrieveAndClearInteractionTrail(hwnd);
                var lastActiveStep = _scenarioSteps.LastOrDefault(s => s.IsActive);
                if (lastActiveStep != null && finalFlush != null && finalFlush.Count > 0)
                {
                    AppendInteractionsWithMerge(lastActiveStep, finalFlush);
                    _hydrationQueue.Enqueue((lastActiveStep, hwnd));
                }

                var (center, context) = _jsonManager.ExtractMainStepData(focused, centerX, centerY);

                var payload = new ExtractionPayload
                {
                    StepId = stepId ?? Guid.NewGuid().ToString(),
                    StepName = "Nova Ação",
                    TriggerType = "Intentional_Print",
                    PendingConfirmation = true,
                    CapturedData = center,
                    ObservedContext = context,
                    InteractionTrail = new List<InteractionBreadcrumb>(),
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
            if (!config.IsEnabled || !_isRecording) return;

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
                    else { fallbackHwnd = GetForegroundWindow(); }
                }
                catch { fallbackHwnd = GetForegroundWindow(); }

                var finalFlush = _jsonManager.RetrieveAndClearInteractionTrail(fallbackHwnd);
                var lastActiveStep = _scenarioSteps.LastOrDefault(s => s.IsActive);
                if (lastActiveStep != null && finalFlush != null && finalFlush.Count > 0)
                {
                    AppendInteractionsWithMerge(lastActiveStep, finalFlush);
                    _hydrationQueue.Enqueue((lastActiveStep, fallbackHwnd));
                }

                var extraction = _jsonManager.ExtractMainStepData(focused, centerX, centerY);

                fallbackPayload = new ExtractionPayload
                {
                    StepId = !string.IsNullOrEmpty(e.StepId) ? e.StepId : Guid.NewGuid().ToString(),
                    StepName = "Nova Ação",
                    TriggerType = "Intentional_Print",
                    PendingConfirmation = false,
                    CapturedData = extraction.Center,
                    ObservedContext = extraction.Context,
                    InteractionTrail = new List<InteractionBreadcrumb>(),
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

                        if (pendingStep != null)
                        {
                            pendingStep.PendingConfirmation = false;
                            pendingStep.StepId = stepId;
                            pendingStep.StepName = "Nova Ação";
                        }
                        else if (fallbackPayload != null)
                        {
                            fallbackPayload.StepId = stepId;
                            _scenarioSteps.Add(fallbackPayload);
                            _hydrationQueue.Enqueue((fallbackPayload, fallbackHwnd));
                        }

                        RecalculateIndices();
                        SaveJsonToDisk(currentDir);
                    }
                }
                catch (Exception ex) { LiteLogger.Error("Erro ao processar gatilho de captura.", ex); }
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
            string name = _hostContext.GetSessionMetadata("CurrentFileName") as string ?? "Cenario_Global";
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

                var opt = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    ReferenceHandler = ReferenceHandler.IgnoreCycles,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string tmp = jsonPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_scenarioSteps, opt));
                if (File.Exists(jsonPath)) File.Delete(jsonPath);
                File.Move(tmp, jsonPath);

                if (_hydrationQueue.IsEmpty && File.Exists(lockPath)) File.Delete(lockPath);
            }
            catch (Exception ex)
            {
                LiteLogger.Error("Falha crítica ao gravar Scenario_Data.json", ex);
            }
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