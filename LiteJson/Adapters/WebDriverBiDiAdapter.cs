using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteJson.Diagnostics;
using LiteJson.Models;

namespace LiteJson.Adapters
{
    public class BiDiContextResponse
    {
        public string Url { get; set; } = string.Empty;
        public string PageTitle { get; set; } = string.Empty;
        public List<BiDiContextElement> Elements { get; set; } = new List<BiDiContextElement>();
    }

    public class BiDiContextElement
    {
        public string ElementType { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public BiDiElementData BidiData { get; set; } = new BiDiElementData();
    }

    public class WebDriverBiDiAdapter
    {
        private const int DebugPort = 9222;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        private const string JsLocatorHelpers = @"
            function checkUnique(selector, isXpath) { try { if (isXpath) { return document.evaluate(selector, document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null).snapshotLength === 1; } return document.querySelectorAll(selector).length === 1; } catch(e) { return false; } }
            function getLoc(value, score, isXpath) { if (!value) return null; var unique = checkUnique(value, isXpath); return { Value: value, Confidence: unique ? score : (score * -1) }; }
            function getCustomAttr(node) { var attrs = ['data-testid', 'data-cy', 'data-test']; for(var i=0; i<attrs.length; i++) { var val = node.getAttribute(attrs[i]); if (val) return '[' + attrs[i] + '=""' + val + '""]'; } return null; }
            function getXPathAbs(node) { if (!node || node.nodeType !== 1) return ''; if (node.tagName.toLowerCase() === 'html') return '/html'; if (node === document.body) return '/html/body'; var ix = 0; var siblings = node.parentNode ? node.parentNode.childNodes : []; for (var i = 0; i < siblings.length; i++) { var sibling = siblings[i]; if (sibling === node) return getXPathAbs(node.parentNode) + '/' + node.tagName.toLowerCase() + '[' + (ix + 1) + ']'; if (sibling.nodeType === 1 && sibling.tagName === node.tagName) ix++; } return ''; }
        ";

        public bool CanHandle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == 0) return false;
                using (var process = Process.GetProcessById((int)processId))
                {
                    string pName = process.ProcessName.ToLower();
                    return pName.Contains("chrome") || pName.Contains("msedge") || pName.Contains("brave") || pName.Contains("opera");
                }
            }
            catch { return false; }
        }

        public EngineNode<BiDiElementData> ExtractBiDiNode(IntPtr hwnd, int x, int y)
        {
            var node = new EngineNode<BiDiElementData> { ElementData = new BiDiElementData() };
            string jsCode = @"(function(x, y) {
                var el = document.elementFromPoint(x, y);
                if (!el) return null;
                " + JsLocatorHelpers + @"
                return {
                    selectorSet: {
                        customAttribute: getLoc(getCustomAttr(el), 100, false),
                        id: getLoc(el.id ? '#' + el.id : null, 90, false),
                        xpathAbsolute: getLoc(getXPathAbs(el), 10, true)
                    },
                    url: window.location.href,
                    value: el.value || el.innerText || '',
                    frameworkId: 'Web_HTML'
                };
            })(" + x + ", " + y + ");";

            var result = Task.Run(() => ExecuteBiDiScriptAsync(jsCode)).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(result))
            {
                node.ElementData = JsonSerializer.Deserialize<BiDiElementData>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                node.QualityFlags.Add("SUCCESS_BIDI_INJECTION");
                EvaluateQualityFlags(node);
            }
            return node;
        }

        public ObservedContext ExtractObservedContext(IntPtr hwnd)
        {
            var context = new ObservedContext();

            // CORREÇÃO: O JS agora devolve apenas a coordenada simples relativa ao site (Viewport)
            string jsCode = @"(function() {
                " + JsLocatorHelpers + @"
                var results = [];
                var elements = document.querySelectorAll('input, button, select, textarea, a, [role=""button""]');
                for(var i=0; i<elements.length; i++) {
                    var el = elements[i];
                    var rect = el.getBoundingClientRect();
                    var style = window.getComputedStyle(el);
                    
                    if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') continue;
                    if (rect.width === 0 || rect.height === 0) continue;

                    // Ignora elementos totalmente fora do Viewport visível para não sobrecarregar a fila
                    if (rect.bottom <= 0 || rect.top >= window.innerHeight || rect.right <= 0 || rect.left >= window.innerWidth) continue;

                    results.push({
                        ElementType: el.tagName.toLowerCase(),
                        IsEnabled: !el.disabled,
                        CenterX: Math.round(rect.left + rect.width / 2),
                        CenterY: Math.round(rect.top + rect.height / 2),
                        BidiData: {
                            selectorSet: {
                                customAttribute: getLoc(getCustomAttr(el), 100, false),
                                id: getLoc(el.id ? '#' + el.id : null, 90, false),
                                xpathAbsolute: getLoc(getXPathAbs(el), 10, true)
                            },
                            url: window.location.href,
                            value: el.value || el.innerText || '',
                            frameworkId: 'Web_HTML'
                        }
                    });
                    if (results.length >= 100) break;
                }
                return { Url: window.location.href, PageTitle: document.title || '', Elements: results };
            })();";

            var result = Task.Run(() => ExecuteBiDiScriptAsync(jsCode)).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(result))
            {
                var response = JsonSerializer.Deserialize<BiDiContextResponse>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (response != null)
                {
                    context.Url = response.Url;
                    context.PageTitle = response.PageTitle;
                    foreach (var el in response.Elements)
                    {
                        var visibleElement = new VisibleElement
                        {
                            ElementType = el.ElementType,
                            IsEnabled = el.IsEnabled,
                            CenterX = el.CenterX,
                            CenterY = el.CenterY
                        };

                        var bidiNode = new EngineNode<BiDiElementData> { ElementData = el.BidiData };
                        bidiNode.QualityFlags.Add("SUCCESS_BIDI_CONTEXT");
                        EvaluateQualityFlags(bidiNode);

                        visibleElement.CapturedData.WebDriver_BiDi = bidiNode;

                        // Inicializa as gavetas para o worker de hidratação preencher
                        visibleElement.CapturedData.UIA = new EngineNode<UiaElementData> { ElementData = new UiaElementData() };
                        visibleElement.CapturedData.AX_Tree = new EngineNode<AxTreeElementData> { ElementData = new AxTreeElementData() };

                        context.VisibleElements.Add(visibleElement);
                    }
                }
            }
            return context;
        }

        private async Task<string> ExecuteBiDiScriptAsync(string jsCode)
        {
            try
            {
                using var httpClient = new HttpClient();
                string response = await httpClient.GetStringAsync($"http://127.0.0.1:{DebugPort}/json").ConfigureAwait(false);
                using var jsonDoc = JsonDocument.Parse(response);

                string wsUrl = string.Empty;
                foreach (var item in jsonDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page")
                    {
                        wsUrl = item.TryGetProperty("webSocketDebuggerUrl", out var wsUrlProp) ? wsUrlProp.GetString() ?? "" : "";
                        break;
                    }
                }

                if (string.IsNullOrEmpty(wsUrl)) return null;

                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None).ConfigureAwait(false);
                var command = new { id = 1, method = "Runtime.evaluate", @params = new { expression = jsCode, returnByValue = true } };
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

                var buffer = new byte[16384];
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                var msgDoc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
                return msgDoc.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetRawText();
            }
            catch { return null; }
        }

        public void EvaluateQualityFlags(EngineNode<BiDiElementData> node)
        {
            if (node?.ElementData?.SelectorSet == null) return;
            var set = node.ElementData.SelectorSet;
            var locs = new[] { set.Id, set.CustomAttribute, set.XpathAbsolute };
            bool amb = false; int max = 0;
            foreach (var l in locs) if (l != null) { if (l.Confidence < 0) amb = true; if (l.Confidence > max) max = l.Confidence; }
            if (max >= 90) node.QualityFlags.Add("LOCATOR_P1_SEMANTIC");
            if (amb) node.QualityFlags.Add("WARNING_AMBIGUOUS_LOCATOR");
        }
    }
}