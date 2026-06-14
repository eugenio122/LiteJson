using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using LiteJson.Adapters;
using LiteJson.Models;
using LiteJson.Diagnostics;

namespace LiteJson.Core
{
    public partial class LiteJsonManager
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
    }
}