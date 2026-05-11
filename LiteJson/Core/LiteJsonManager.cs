using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using LiteJson.Adapters;
using LiteJson.Models;
using LiteJson.Diagnostics;

namespace LiteJson.Core
{
    public class LiteJsonManager
    {
        private readonly UIAAdapter _uiaAdapter;
        private readonly WebDriverBiDiAdapter _bidiAdapter;
        private readonly IUIAutomation _automation;

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        public LiteJsonManager()
        {
            _uiaAdapter = new UIAAdapter();
            _bidiAdapter = new WebDriverBiDiAdapter();
            _automation = new CUIAutomation();
        }

        public (CapturedData Center, ObservedContext Context) ExtractMainStepData(IUIAutomationElement focused, int x, int y)
        {
            var config = LiteJsonConfig.Load();
            var center = new CapturedData();
            var context = new ObservedContext();

            var uiaData = _uiaAdapter.ExtractUiaNode(focused);
            center.UIA = uiaData;

            center.AX_Tree = new EngineNode<AxTreeElementData> { ElementData = new AxTreeElementData() };

            IntPtr hwnd = focused != null && focused.CurrentNativeWindowHandle != 0
                ? (IntPtr)focused.CurrentNativeWindowHandle
                : GetForegroundWindow();

            if (config.Target == TargetEngine.WebUniversal && _bidiAdapter.CanHandle(hwnd))
            {
                center.WebDriver_BiDi = _bidiAdapter.ExtractBiDiNode(hwnd, x, y);
                context = _bidiAdapter.ExtractObservedContext(hwnd);
            }
            else
            {
                context = _uiaAdapter.ExtractObservedContext(focused);
            }

            return (center, context);
        }

        public void EnsureClickTrackerInjected(IntPtr hwnd)
        {
            if (LiteJsonConfig.Load().Target == TargetEngine.WebUniversal)
                _bidiAdapter.EnsureClickTrackerInjected(hwnd);
        }

        public List<InteractionBreadcrumb> RetrieveAndClearInteractionTrail(IntPtr hwnd)
        {
            if (LiteJsonConfig.Load().Target == TargetEngine.WebUniversal)
                return _bidiAdapter.RetrieveAndClearInteractionTrail(hwnd);
            return new List<InteractionBreadcrumb>();
        }

        public void HydrateTrail(List<InteractionBreadcrumb> trail, IntPtr hwnd)
        {
            if (trail == null || trail.Count == 0) return;
            try
            {
                var config = LiteJsonConfig.Load();
                if (config.Target == TargetEngine.WebUniversal)
                {
                    _bidiAdapter.HydrateTrailAxTree(trail);
                }

                IUIAutomation localWorkerAutomation = new CUIAutomation();
                if (hwnd == IntPtr.Zero) hwnd = GetForegroundWindow();

                var windowElement = localWorkerAutomation.ElementFromHandle(hwnd);
                if (windowElement == null) return;

                var condition = localWorkerAutomation.CreatePropertyCondition(30003, 50030); // UIA_DocumentControlTypeId
                var docElement = windowElement.FindFirst(TreeScope.TreeScope_Descendants, condition);

                int offsetX = docElement != null ? docElement.CurrentBoundingRectangle.left : windowElement.CurrentBoundingRectangle.left;
                int offsetY = docElement != null ? docElement.CurrentBoundingRectangle.top : windowElement.CurrentBoundingRectangle.top + 87;

                int hydratedCount = 0;

                foreach (var step in trail)
                {
                    if (step.UIA == null) step.UIA = new EngineNode<UiaElementData> { ElementData = new UiaElementData() };

                    // PROTEÇÃO DE IDEMPOTÊNCIA: Se este clique já foi hidratado no loop anterior, ignora!
                    if (step.UIA.QualityFlags.Contains("SUCCESS_UIA_BREADCRUMB")) continue;

                    var center = step.CenterCoordinates;
                    if (center.X == 0 && center.Y == 0) continue;

                    int screenX = offsetX + center.X;
                    int screenY = offsetY + center.Y;

                    try
                    {
                        var uiaEl = localWorkerAutomation.ElementFromPoint(new tagPOINT { x = screenX, y = screenY });
                        if (uiaEl != null)
                        {
                            var uiaData = _uiaAdapter.ExtractUiaNode(uiaEl);
                            step.UIA.ElementData = uiaData.ElementData;
                            step.UIA.QualityFlags = uiaData.QualityFlags;
                            step.UIA.QualityFlags.Add("SUCCESS_UIA_BREADCRUMB");
                            step.UIA.QualityFlags.Add("HYDRATED_ASYNC");

                            hydratedCount++;
                        }
                    }
                    catch (Exception innerEx)
                    {
                        LiteLogger.Debug($"[HydrateTrail] ElementFromPoint falhou no clique X:{screenX} Y:{screenY}. Erro: {innerEx.Message}");
                    }
                }

                if (hydratedCount > 0)
                    LiteLogger.Info($"[HydrateTrail] Sucesso: {hydratedCount} NOVOS breadcrumbs hidratados retroativamente.");
            }
            catch (Exception ex)
            {
                LiteLogger.Error("[HydrateTrail] Erro crítico na hidratação da trilha.", ex);
            }
        }

        public void HydrateContext(ObservedContext ctx, IntPtr hwnd)
        {
            if (ctx == null || ctx.VisibleElements.Count == 0) return;
            try
            {
                IUIAutomation localWorkerAutomation = new CUIAutomation();

                if (hwnd == IntPtr.Zero) hwnd = GetForegroundWindow();
                var windowElement = localWorkerAutomation.ElementFromHandle(hwnd);
                if (windowElement == null) return;

                var condition = localWorkerAutomation.CreatePropertyCondition(30003, 50030); // UIA_DocumentControlTypeId
                var docElement = windowElement.FindFirst(TreeScope.TreeScope_Descendants, condition);

                int offsetX = docElement != null ? docElement.CurrentBoundingRectangle.left : windowElement.CurrentBoundingRectangle.left;
                int offsetY = docElement != null ? docElement.CurrentBoundingRectangle.top : windowElement.CurrentBoundingRectangle.top + 87;

                int hydratedCount = 0;

                foreach (var el in ctx.VisibleElements)
                {
                    HydrateRecursive(el, offsetX, offsetY, localWorkerAutomation, ref hydratedCount, false);
                }

                if (hydratedCount > 0)
                    LiteLogger.Info($"[HydrateContext] Sucesso: {hydratedCount} elementos totais hidratados com UIA nativo.");
            }
            catch (Exception ex)
            {
                LiteLogger.Error("[HydrateContext] Erro crítico na hidratação.", ex);
            }
        }

        private void HydrateRecursive(VisibleElement el, int offsetX, int offsetY, IUIAutomation localWorkerAutomation, ref int hydratedCount, bool skipUia)
        {
            bool isAlreadyHydrated = el.CapturedData.UIA != null && !string.IsNullOrEmpty(el.CapturedData.UIA.ElementData.BoundingRectangle);
            bool isSolid = false;

            if (!isAlreadyHydrated && !skipUia)
            {
                int screenX = offsetX + el.CenterX;
                int screenY = offsetY + el.CenterY;

                try
                {
                    var uiaEl = localWorkerAutomation.ElementFromPoint(new tagPOINT { x = screenX, y = screenY });
                    if (uiaEl != null)
                    {
                        var uiaData = _uiaAdapter.ExtractUiaNode(uiaEl);

                        el.CapturedData.UIA.ElementData = uiaData.ElementData;
                        el.CapturedData.UIA.QualityFlags = uiaData.QualityFlags;
                        el.CapturedData.UIA.QualityFlags.Add("SUCCESS_UIA_CONTEXT");
                        el.CapturedData.UIA.QualityFlags.Add("HYDRATED_ASYNC");

                        hydratedCount++;
                    }
                }
                catch (Exception innerEx)
                {
                    LiteLogger.Debug($"[HydrateContext] ElementFromPoint falhou em X:{screenX} Y:{screenY}. Erro: {innerEx.Message}");
                }
            }

            bool hasUiaA11y = el.CapturedData.UIA != null && (
                                   el.CapturedData.UIA.QualityFlags.Contains("A11Y_NAME_PRESENT") ||
                                   el.CapturedData.UIA.QualityFlags.Contains("A11Y_AUTOMATION_ID_PRESENT"));

            bool hasChromeA11y = el.CapturedData.AX_Tree != null && (
                                   el.CapturedData.AX_Tree.QualityFlags.Contains("A11Y_NAME_PRESENT"));

            bool hasStrongBiDiLocator = el.CapturedData.WebDriver_BiDi != null && (
                                        el.CapturedData.WebDriver_BiDi.QualityFlags.Contains("LOCATOR_P0_GOLDEN") ||
                                        el.CapturedData.WebDriver_BiDi.QualityFlags.Contains("LOCATOR_P1_SEMANTIC"));

            bool isStrongNativeTag = el.ElementType == "button" || el.ElementType == "a" ||
                                     el.ElementType == "input" || el.ElementType == "select" ||
                                     el.ElementType == "textarea";

            if (hasUiaA11y || hasChromeA11y || isStrongNativeTag || hasStrongBiDiLocator)
            {
                el.IsSemanticAnchor = true;
                isSolid = true;
            }

            if (el.Children != null && el.Children.Count > 0)
            {
                foreach (var child in el.Children)
                {
                    bool shouldSkipChild = (isSolid || skipUia) && IsVisualChild(child.ElementType);
                    HydrateRecursive(child, offsetX, offsetY, localWorkerAutomation, ref hydratedCount, shouldSkipChild);
                }
            }
        }

        private bool IsVisualChild(string elementType)
        {
            var visualTags = new HashSet<string> { "svg", "img", "span", "i", "p", "div", "b", "strong", "path" };
            return visualTags.Contains(elementType);
        }
    }
}