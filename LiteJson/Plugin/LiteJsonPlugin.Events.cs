using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Interop.UIAutomationClient;
using LiteJson.Models;
using LiteJson.Diagnostics;
using LiteTools.Interfaces;

namespace LiteJson.Plugin
{
    public partial class LiteJsonPlugin
    {
        // =========================================================================
        // EVENTO: PreparePendingStep
        // Chamado pelo LiteShot no momento do atalho de captura (pré-overlay).
        // Captura o estado da tela e cria o payload pendente de confirmação.
        // =========================================================================
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
                        hwnd = focused.CurrentNativeWindowHandle != 0
                            ? (IntPtr)focused.CurrentNativeWindowHandle
                            : GetForegroundWindow();
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

                string newStepId = stepId ?? Guid.NewGuid().ToString();

                // --- INJEÇÃO DE IDENTIDADE ---
                // Promove CapturedData para TargetElementData e carimba o StepId nos três artefatos.
                var targetElementData = new TargetElementData
                {
                    AssociatedStepId = newStepId,
                    UIA = center.UIA,
                    AX_Tree = center.AX_Tree,
                    WebDriver_BiDi = center.WebDriver_BiDi
                };
                context.AssociatedStepId = newStepId;
                if (finalFlush != null)
                    foreach (var crumb in finalFlush)
                        crumb.AssociatedStepId = newStepId;

                var payload = new ExtractionPayload
                {
                    StepId = newStepId,
                    StepName = "Nova Ação",
                    TriggerType = "Intentional_Print",
                    PendingConfirmation = true,
                    TargetElementData = targetElementData,
                    ObservedContext = context,
                    InteractionTrail = new List<InteractionBreadcrumb>(),
                    IsHydrated = false
                };

                _scenarioSteps.Add(payload);
                _hydrationQueue.Enqueue((payload, hwnd));

                // --- HOVER CHAIN: grava o alvo deste print no cache de triggers ---
                // O mouse parado sobre um menu suspenso é um candidato a trigger.
                // Guarda uma cópia limpa (XPaths anulados) para o próximo click consumir.
                lock (_hoverChainCache)
                {
                    _hoverChainCache.Add(targetElementData.CloneForHoverChain());
                }

                RecalculateIndices();
                SaveJsonToDisk(GetCurrentScenarioDirectory());
            }
        }

        // =========================================================================
        // EVENTO: OnImageCaptured
        // Chamado quando o usuário confirma a captura (ex: Ctrl+C no LiteShot).
        // Confirma o pendingStep ou cria um fallbackPayload se não havia pending.
        // =========================================================================
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
                        fallbackHwnd = focused.CurrentNativeWindowHandle != 0
                            ? (IntPtr)focused.CurrentNativeWindowHandle
                            : GetForegroundWindow();
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

                string fallbackStepId = !string.IsNullOrEmpty(e.StepId) ? e.StepId : Guid.NewGuid().ToString();

                // --- INJEÇÃO DE IDENTIDADE ---
                var fallbackTarget = new TargetElementData
                {
                    AssociatedStepId = fallbackStepId,
                    UIA = extraction.Center.UIA,
                    AX_Tree = extraction.Center.AX_Tree,
                    WebDriver_BiDi = extraction.Center.WebDriver_BiDi
                };
                extraction.Context.AssociatedStepId = fallbackStepId;

                fallbackPayload = new ExtractionPayload
                {
                    StepId = fallbackStepId,
                    StepName = "Nova Ação",
                    TriggerType = "Intentional_Print",
                    PendingConfirmation = false,
                    TargetElementData = fallbackTarget,
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

                            // --- INJEÇÃO DE IDENTIDADE no pendingStep ---
                            pendingStep.TargetElementData.AssociatedStepId = stepId;
                            pendingStep.ObservedContext.AssociatedStepId = stepId;
                            foreach (var crumb in pendingStep.InteractionTrail)
                                crumb.AssociatedStepId = stepId;
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

        // =========================================================================
        // EVENTO: OnImageCaptureCanceled
        // Remove payloads pendentes quando o usuário cancela a captura (ESC).
        // Remove também do cache de HoverChain APENAS os triggers dos prints
        // cancelados — triggers de prints anteriores válidos permanecem.
        // =========================================================================
        private void OnImageCaptureCanceled(ImageCaptureCanceledEvent e)
        {
            List<string> canceledStepIds;

            lock (_lockObj)
            {
                var pending = _scenarioSteps.Where(s => s.PendingConfirmation).ToList();
                canceledStepIds = pending.Select(p => p.StepId).ToList();

                foreach (var p in pending) _scenarioSteps.Remove(p);

                RecalculateIndices();
                SaveJsonToDisk(GetCurrentScenarioDirectory());
            }

            // Remove do cache só as entradas cujo trigger pertence aos prints cancelados.
            if (canceledStepIds.Count > 0)
            {
                lock (_hoverChainCache)
                {
                    _hoverChainCache.RemoveAll(trigger => canceledStepIds.Contains(trigger.AssociatedStepId));
                }
            }
        }

        // =========================================================================
        // EVENTOS: Gerenciamento de Passos (LiteFlow -> LiteJson)
        // =========================================================================
        private void OnSessionRestarted(SessionRestartedEvent e)
        {
            lock (_lockObj)
            {
                _scenarioSteps.Clear();
                _lastScenarioName = null;
            }
            lock (_hoverChainCache)
            {
                _hoverChainCache.Clear();
            }
        }

        private void OnStepDeleted(StepDeletedEvent e)
        {
            lock (_lockObj)
            {
                var s = _scenarioSteps.FirstOrDefault(x => x.StepId == e.StepId);
                if (s != null) { s.IsActive = false; RecalculateIndices(); SaveJsonToDisk(GetCurrentScenarioDirectory()); }
            }
        }

        private void OnStepRestored(StepRestoredEvent e)
        {
            lock (_lockObj)
            {
                var s = _scenarioSteps.FirstOrDefault(x => x.StepId == e.StepId);
                if (s != null) { s.IsActive = true; RecalculateIndices(); SaveJsonToDisk(GetCurrentScenarioDirectory()); }
            }
        }

        private void OnStepsReordered(StepsReorderedEvent e)
        {
            lock (_lockObj)
            {
                var newList = e.NewOrderIds
                    .Select(id => _scenarioSteps.FirstOrDefault(x => x.StepId == id))
                    .Where(x => x != null)
                    .ToList();
                _scenarioSteps = newList;
                RecalculateIndices();
                SaveJsonToDisk(GetCurrentScenarioDirectory());
            }
        }

        private void OnStepMetadataChanged(StepMetadataChangedEvent e)
        {
            lock (_lockObj)
            {
                var s = _scenarioSteps.FirstOrDefault(x => x.StepId == e.StepId);
                if (s != null)
                {
                    s.IsEvidenceOnly = e.IsEvidenceOnly;
                    if (!string.IsNullOrEmpty(e.NewName)) s.StepName = e.NewName;
                    RecalculateIndices();
                    SaveJsonToDisk(GetCurrentScenarioDirectory());
                }
            }
        }
    }
}