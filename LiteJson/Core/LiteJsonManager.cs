using System;
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

            var uiaData = _uiaAdapter.ExtractDualTree(focused);
            center.UIA = uiaData.UIA; center.AX_Tree = uiaData.AX_Tree;

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

        public void HydrateContext(ObservedContext ctx, IntPtr hwnd)
        {
            if (ctx == null || ctx.VisibleElements.Count == 0) return;
            try
            {
                // Motor COM instanciado na Thread STA que criámos no Plugin
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
                    bool isAlreadyHydrated = el.CapturedData.UIA != null && !string.IsNullOrEmpty(el.CapturedData.UIA.ElementData.BoundingRectangle);
                    if (isAlreadyHydrated) continue;

                    int screenX = offsetX + el.CenterX;
                    int screenY = offsetY + el.CenterY;

                    try
                    {
                        var uiaEl = localWorkerAutomation.ElementFromPoint(new tagPOINT { x = screenX, y = screenY });
                        if (uiaEl != null)
                        {
                            var data = _uiaAdapter.ExtractDualTree(uiaEl);

                            el.CapturedData.UIA.ElementData = data.UIA.ElementData;
                            el.CapturedData.UIA.QualityFlags = data.UIA.QualityFlags;
                            el.CapturedData.UIA.QualityFlags.Add("SUCCESS_UIA_CONTEXT");
                            el.CapturedData.UIA.QualityFlags.Add("HYDRATED_ASYNC");

                            el.CapturedData.AX_Tree.ElementData = data.AX_Tree.ElementData;
                            el.CapturedData.AX_Tree.QualityFlags = data.AX_Tree.QualityFlags;
                            el.CapturedData.AX_Tree.QualityFlags.Add("SUCCESS_AX_TREE_CONTEXT");
                            el.CapturedData.AX_Tree.QualityFlags.Add("HYDRATED_ASYNC");

                            hydratedCount++;
                        }
                    }
                    catch (Exception innerEx)
                    {
                        LiteLogger.Debug($"[HydrateContext] ElementFromPoint falhou em X:{screenX} Y:{screenY}. Erro: {innerEx.Message}");
                    }
                }

                LiteLogger.Info($"[HydrateContext] Sucesso: {hydratedCount} de {ctx.VisibleElements.Count} elementos hidratados com UIA.");
            }
            catch (Exception ex)
            {
                LiteLogger.Error("[HydrateContext] Erro crítico na hidratação.", ex);
            }
        }

        public (EngineNode<UiaElementData> UIA, EngineNode<AxTreeElementData> AX_Tree) ExtractDualTreeFromElement(IUIAutomationElement element)
            => _uiaAdapter.ExtractDualTree(element);
    }
}