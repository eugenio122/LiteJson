using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Interop.UIAutomationClient;
using LiteJson.Core;
using LiteJson.Models;
using LiteJson.Diagnostics;
using LiteTools.Interfaces;

namespace LiteJson.Plugin
{
    public partial class LiteJsonPlugin
    {
        // =========================================================================
        // WORKER: Anti-Flood da Digitação no C#
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

                // --- HOVER CHAIN: o click consome e limpa o cache de triggers ---
                // Qualquer click (dentro ou fora de um menu) o recolhe, então a cadeia
                // acumulada até aqui pertence a este click. Focus/input NÃO consomem,
                // para evitar ruído.
                if (newInteraction.InteractionType == "click")
                {
                    lock (_hoverChainCache)
                    {
                        if (_hoverChainCache.Count > 0)
                        {
                            newInteraction.HoverChain.AddRange(_hoverChainCache);
                            _hoverChainCache.Clear();
                        }
                    }
                }

                step.InteractionTrail.Add(newInteraction);
            }
        }

        // =========================================================================
        // WORKER: Tracker Loop — injeta o click tracker e drena a trilha ao vivo
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

        // =========================================================================
        // WORKER: Hydration Loop — hidrata UIA, AX Tree e executa o Oráculo
        // =========================================================================
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

                            // --- ORÁCULO DE AMBIGUIDADE ---
                            // Extrai o Full DOM uma única vez para este step,
                            // avalia Target + Trail + Context e descarta o Full DOM.
                            _jsonManager.RunOracle(
                                item.Step.TargetElementData,
                                item.Step.InteractionTrail,
                                item.Step.ObservedContext,
                                item.Step.StepId);

                            item.Step.IsHydrated = true;
                        }

                        lock (_lockObj) { SaveJsonToDisk(GetCurrentScenarioDirectory()); }
                    }
                    catch (Exception ex) { LiteLogger.Error("[Worker] Falha crítica.", ex); }
                    finally
                    {
                        if (_hydrationQueue.IsEmpty)
                            _hostContext.SetSessionMetadata("LiteJson_IsProcessing", false);
                    }
                }
                else
                {
                    Thread.Sleep(500);
                }
            }
        }
    }
}