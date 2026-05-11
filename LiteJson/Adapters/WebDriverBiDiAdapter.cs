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
        public int ViewportWidth { get; set; }
        public int ViewportHeight { get; set; }
        public List<BiDiContextElement> Elements { get; set; } = new List<BiDiContextElement>();
    }

    public class BiDiContextElement
    {
        public int LiteId { get; set; }
        public string ElementType { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool IsFocused { get; set; } = false;
        public bool? IsChecked { get; set; }
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public BiDiElementData BidiData { get; set; } = new BiDiElementData();
        public List<BiDiContextElement> Children { get; set; } = new List<BiDiContextElement>();
    }

    public class WebDriverBiDiAdapter
    {
        private const int DebugPort = 9222;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        private const string JsLocatorHelpers = @"
            function cleanStr(s) { return s ? String(s).replace(/[\r\n\t\f\v ]+/g, ' ').trim() : ''; }
            function checkUnique(selector, isXpath) { try { if (isXpath) { return document.evaluate(selector, document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null).snapshotLength === 1; } return document.querySelectorAll(selector).length === 1; } catch(e) { return false; } }
            function getLoc(value, score, isXpath) { if (!value) return null; var unique = checkUnique(value, isXpath); return { Value: value, Confidence: unique ? score : (score * -1) }; }
            function getCustomAttr(node) { var attrs = ['data-testid', 'data-cy', 'data-test']; for(var i=0; i<attrs.length; i++) { var val = node.getAttribute(attrs[i]); if (val) return ""["" + attrs[i] + ""='"" + cleanStr(val) + ""']""; } return null; }
            function getXPathAbs(node) { if (!node || node.nodeType !== 1) return ''; if (node.tagName.toLowerCase() === 'html') return '/html'; if (node === document.body) return '/html/body'; var ix = 0; var siblings = node.parentNode ? node.parentNode.childNodes : []; for (var i = 0; i < siblings.length; i++) { var sibling = siblings[i]; if (sibling === node) return getXPathAbs(node.parentNode) + '/' + node.tagName.toLowerCase() + '[' + (ix + 1) + ']'; if (sibling.nodeType === 1 && sibling.tagName === node.tagName) ix++; } return ''; }
            function getXPathRel(node) { if (node.id) return ""//"" + node.tagName.toLowerCase() + ""[@id='"" + node.id + ""']""; return getXPathAbs(node); }
            function getCssSelector(node) { if (node.id) return '#' + node.id; if (node.className && typeof node.className === 'string') { var classes = node.className.trim().split(/\s+/).join('.'); if (classes) return node.tagName.toLowerCase() + '.' + classes; } return node.tagName.toLowerCase(); }
            function getElementText(el) {
                if (el.tagName.toLowerCase() === 'input' || el.tagName.toLowerCase() === 'textarea') { return cleanStr(el.value || el.placeholder || ''); }
                if (el.tagName.toLowerCase() === 'select') { return cleanStr(el.options[el.selectedIndex] ? el.options[el.selectedIndex].text : ''); }
                var text = el.innerText || el.textContent || '';
                return cleanStr(text).substring(0, 100); 
            }
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

        private async Task<string> GetActiveWebSocketUrlAsync()
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                string response = await httpClient.GetStringAsync($"http://127.0.0.1:{DebugPort}/json").ConfigureAwait(false);
                using var jsonDoc = JsonDocument.Parse(response);

                string fallbackUrl = "";
                foreach (var item in jsonDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page")
                    {
                        string wsUrl = item.TryGetProperty("webSocketDebuggerUrl", out var wsProp) ? wsProp.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(wsUrl)) continue;
                        if (string.IsNullOrEmpty(fallbackUrl)) fallbackUrl = wsUrl;

                        string visibility = await EvaluateOnSingleTabAsync(wsUrl, "document.visibilityState");
                        if (visibility != null && visibility.Contains("visible"))
                        {
                            return wsUrl;
                        }
                    }
                }
                return fallbackUrl;
            }
            catch { return string.Empty; }
        }

        private async Task<string> EvaluateOnSingleTabAsync(string wsUrl, string jsCode)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token).ConfigureAwait(false);

                var jsCmd = new { id = 1, method = "Runtime.evaluate", @params = new { expression = jsCode, returnByValue = true } };
                // Passando o ExpectedId = 1 para filtrar eventos
                string rawResult = await SendWsCommandAsync(ws, 1, jsCmd, cts.Token);

                if (string.IsNullOrEmpty(rawResult)) return null;

                var parsedJs = JsonDocument.Parse(rawResult);
                var resultNode = parsedJs.RootElement.GetProperty("result").GetProperty("result");
                if (resultNode.TryGetProperty("value", out var valueNode))
                {
                    return valueNode.ValueKind == JsonValueKind.String ? valueNode.GetString() : valueNode.GetRawText();
                }
                return null;
            }
            catch { return null; }
        }

        private async Task<List<string>> EvaluateOnAllPagesAsync(string jsCode)
        {
            var results = new List<string>();
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                string response = await httpClient.GetStringAsync($"http://127.0.0.1:{DebugPort}/json").ConfigureAwait(false);
                using var jsonDoc = JsonDocument.Parse(response);

                var tasks = new List<Task<string>>();
                foreach (var item in jsonDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page")
                    {
                        string wsUrl = item.TryGetProperty("webSocketDebuggerUrl", out var wsProp) ? wsProp.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(wsUrl))
                        {
                            tasks.Add(EvaluateOnSingleTabAsync(wsUrl, jsCode));
                        }
                    }
                }

                var tabResults = await Task.WhenAll(tasks);
                foreach (var res in tabResults)
                {
                    if (!string.IsNullOrEmpty(res)) results.Add(res);
                }
            }
            catch { }
            return results;
        }

        // =========================================================================================
        // O CÃO DE GUARDA DO WEBSOCKET: Filtra eventos aleatórios e só retorna a resposta certa
        // =========================================================================================
        private async Task<string> SendWsCommandAsync(ClientWebSocket ws, int expectedId, object command, CancellationToken token)
        {
            string cmdJson = JsonSerializer.Serialize(command);
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(cmdJson)), WebSocketMessageType.Text, true, token).ConfigureAwait(false);

            var buffer = new byte[16384];
            while (!token.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                while (true)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) break;
                }

                string response = Encoding.UTF8.GetString(ms.ToArray());

                try
                {
                    // Lê o JSON da mensagem que acabou de chegar
                    using var doc = JsonDocument.Parse(response);
                    // Se a mensagem possuir o "id" e ele for igual ao expectedId, é a nossa resposta!
                    if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.GetInt32() == expectedId)
                    {
                        return response;
                    }
                }
                catch
                {
                    // Se não for um JSON válido ou for um evento (ex: Target.attached), ignora e continua ouvindo
                }
            }
            return string.Empty;
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
                        id: getLoc(el.id ? '#' + cleanStr(el.id) : null, 90, false),
                        xpathAbsolute: getLoc(getXPathAbs(el), 10, true)
                    },
                    url: window.location.href,
                    value: getElementText(el),
                    frameworkId: 'Web_HTML'
                };
            })(" + x + ", " + y + ");";

            var result = Task.Run(async () => {
                string wsUrl = await GetActiveWebSocketUrlAsync();
                if (string.IsNullOrEmpty(wsUrl)) return null;
                return await EvaluateOnSingleTabAsync(wsUrl, jsCode);
            }).GetAwaiter().GetResult();

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

            string jsCode = @"(function() {
                " + JsLocatorHelpers + @"
                
                var TARGET_SELECTORS = 'input, button, select, textarea, a, [role=""button""], [role=""checkbox""], [role=""radio""], [role=""switch""], [role=""dialog""], [role=""alert""], [role=""menu""], [role=""menuitem""], [role=""listbox""], [role=""option""], [role=""tooltip""]';
                var MAX_ELEMENTS = 300;
                var elementCount = 0;

                function hasVisualMeaning(el) {
                    var tag = el.tagName.toLowerCase();
                    if (tag === 'svg' || tag === 'img' || tag === 'i') return true;
                    for(var i=0; i<el.childNodes.length; i++){
                        var child = el.childNodes[i];
                        if (child.nodeType === 3 && child.nodeValue.trim().length > 0) return true;
                    }
                    return false;
                }

                function extractNodeData(el, rect, id) {
                    var isFocused = (document.activeElement === el);
                    var isChecked = null;
                    if (el.type === 'checkbox' || el.type === 'radio') {
                        isChecked = el.checked;
                    } else if (el.hasAttribute('aria-checked')) {
                        isChecked = el.getAttribute('aria-checked') === 'true';
                    } else if (el.hasAttribute('aria-selected')) {
                        isChecked = el.getAttribute('aria-selected') === 'true';
                    }

                    var isDisabled = el.disabled || el.getAttribute('aria-disabled') === 'true';

                    var dynState = '';
                    if (el.hasAttribute('aria-expanded')) dynState += '[Expanded:' + el.getAttribute('aria-expanded') + '] ';
                    if (el.hasAttribute('aria-invalid')) dynState += '[Invalid:' + el.getAttribute('aria-invalid') + '] ';
                    var finalValue = cleanStr(dynState + getElementText(el));

                    return {
                        LiteId: id,
                        ElementType: el.tagName.toLowerCase(),
                        IsEnabled: !isDisabled,
                        IsFocused: isFocused,
                        IsChecked: isChecked,
                        CenterX: Math.round(rect.left + rect.width / 2),
                        CenterY: Math.round(rect.top + rect.height / 2),
                        Width: Math.round(rect.width),
                        Height: Math.round(rect.height),
                        Children: [],
                        BidiData: {
                            selectorSet: {
                                customAttribute: getLoc(getCustomAttr(el), 100, false),
                                id: getLoc(el.id ? '#' + cleanStr(el.id) : null, 90, false),
                                ariaLabel: getLoc(el.hasAttribute('aria-label') ? ""[aria-label='"" + cleanStr(el.getAttribute('aria-label')) + ""']"" : null, 85, false),
                                name: getLoc(el.hasAttribute('name') ? ""[name='"" + cleanStr(el.getAttribute('name')) + ""']"" : null, 85, false),
                                placeholder: getLoc(el.hasAttribute('placeholder') ? ""[placeholder='"" + cleanStr(el.getAttribute('placeholder')) + ""']"" : null, 85, false),
                                alt: getLoc(el.hasAttribute('alt') ? ""[alt='"" + cleanStr(el.getAttribute('alt')) + ""']"" : null, 85, false),
                                title: getLoc(el.hasAttribute('title') ? ""[title='"" + cleanStr(el.getAttribute('title')) + ""']"" : null, 70, false),
                                xpathAbsolute: getLoc(getXPathAbs(el), 10, true)
                            },
                            url: window.location.href,
                            value: finalValue,
                            frameworkId: 'Web_HTML'
                        }
                    };
                }

                function walk(node, depth, isInsideTarget) {
                    if (elementCount >= MAX_ELEMENTS) return null;
                    if (depth > 50) return null; 
                    if (!node || node.nodeType !== 1) return null; 

                    var tag = node.tagName.toLowerCase();
                    var isNativeInput = (tag === 'input' || tag === 'select' || tag === 'textarea');

                    var style = window.getComputedStyle(node);
                    
                    if (style.display === 'none') return null;
                    if (!isNativeInput && (style.visibility === 'hidden' || style.opacity === '0')) return null;

                    var rect = node.getBoundingClientRect();
                    
                    if (rect.width === 0 || rect.height === 0) {
                        if (!isNativeInput && style.overflow === 'hidden') return null; 
                    } else {
                        if (rect.bottom <= 0 || rect.top >= window.innerHeight || rect.right <= 0 || rect.left >= window.innerWidth) {
                             if (!isNativeInput) return null;
                        }
                    }

                    var isCurrentTarget = false;
                    try { isCurrentTarget = node.matches(TARGET_SELECTORS); } catch(e) {}
                    
                    var shouldCapture = isCurrentTarget || (isInsideTarget && hasVisualMeaning(node)) || isNativeInput;
                    
                    var childrenData = [];
                    var passInsideTarget = isInsideTarget || isCurrentTarget;
                    
                    var childrenNodes = node.shadowRoot ? node.shadowRoot.children : node.children;
                    for(var i=0; i<childrenNodes.length; i++) {
                        var childRes = walk(childrenNodes[i], depth + 1, passInsideTarget);
                        if (childRes) {
                            if (Array.isArray(childRes)) {
                                childrenData = childrenData.concat(childRes); 
                            } else {
                                childrenData.push(childRes);
                            }
                        }
                    }

                    if (shouldCapture) {
                        elementCount++;
                        node.setAttribute('lite-ax-id', elementCount);
                        var elData = extractNodeData(node, rect, elementCount);
                        if (childrenData.length > 0) elData.Children = childrenData;
                        return elData;
                    } else {
                        if (childrenData.length > 0) return childrenData;
                        return null;
                    }
                }

                var rootResults = walk(document.body, 0, false);
                var finalArray = [];
                if (rootResults) {
                    finalArray = Array.isArray(rootResults) ? rootResults : [rootResults];
                }

                return { 
                    Url: window.location.href, 
                    PageTitle: document.title || '', 
                    ViewportWidth: window.innerWidth,
                    ViewportHeight: window.innerHeight,
                    Elements: finalArray 
                };
            })();";

            var cdpResult = Task.Run(() => ExecuteFullAccessibilityPipelineAsync(jsCode)).GetAwaiter().GetResult();

            if (cdpResult != null && cdpResult.Response != null)
            {
                context.Url = cdpResult.Response.Url;
                context.PageTitle = cdpResult.Response.PageTitle;
                context.ViewportWidth = cdpResult.Response.ViewportWidth;
                context.ViewportHeight = cdpResult.Response.ViewportHeight;

                if (cdpResult.Response.Elements != null)
                {
                    foreach (var el in cdpResult.Response.Elements)
                    {
                        context.VisibleElements.Add(MapBiDiToVisibleElement(el, cdpResult.AxTreeDict, null));
                    }
                }
            }
            return context;
        }

        private VisibleElement MapBiDiToVisibleElement(BiDiContextElement el, Dictionary<int, SemanticNode> axTreeDict, VisibleElement parent = null)
        {
            var visibleElement = new VisibleElement
            {
                NodeId = $"web-node-{el.LiteId}",
                ElementType = el.ElementType,
                IsEnabled = el.IsEnabled,
                IsFocused = el.IsFocused,
                IsChecked = el.IsChecked,
                CenterX = el.CenterX,
                CenterY = el.CenterY,
                Width = el.Width,
                Height = el.Height,
                Parent = parent
            };

            var bidiNode = new EngineNode<BiDiElementData> { ElementData = el.BidiData };
            bidiNode.QualityFlags.Add("SUCCESS_BIDI_CONTEXT");
            EvaluateQualityFlags(bidiNode);

            visibleElement.CapturedData.WebDriver_BiDi = bidiNode;
            visibleElement.CapturedData.UIA = new EngineNode<UiaElementData> { ElementData = new UiaElementData() };

            visibleElement.CapturedData.AX_Tree = new EngineNode<AxTreeElementData> { ElementData = new AxTreeElementData() };

            if (axTreeDict.TryGetValue(el.LiteId, out var semanticData))
            {
                visibleElement.CapturedData.AX_Tree.ElementData.Semantic = semanticData;
                visibleElement.CapturedData.AX_Tree.QualityFlags.Add("SUCCESS_NATIVE_CHROME_AX_TREE");

                if (semanticData.AccessibleName?.Confidence > 0)
                    visibleElement.CapturedData.AX_Tree.QualityFlags.Add("A11Y_NAME_PRESENT");
            }

            if (el.Children != null && el.Children.Count > 0)
            {
                foreach (var child in el.Children)
                {
                    visibleElement.Children.Add(MapBiDiToVisibleElement(child, axTreeDict, visibleElement));
                }
            }

            return visibleElement;
        }

        private class PipelineResult
        {
            public BiDiContextResponse Response { get; set; }
            public Dictionary<int, SemanticNode> AxTreeDict { get; set; } = new Dictionary<int, SemanticNode>();
        }

        private async Task<PipelineResult> ExecuteFullAccessibilityPipelineAsync(string jsCode)
        {
            try
            {
                string wsUrl = await GetActiveWebSocketUrlAsync();
                if (string.IsNullOrEmpty(wsUrl)) return null;

                // Aumentado para 10s para garantir que não cancele árvores de sites pesados
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token).ConfigureAwait(false);

                var jsCmd = new { id = 1, method = "Runtime.evaluate", @params = new { expression = jsCode, returnByValue = true } };
                var jsRawResult = await SendWsCommandAsync(ws, 1, jsCmd, cts.Token);

                if (string.IsNullOrEmpty(jsRawResult)) return null;

                var pipelineResult = new PipelineResult();
                var parsedJs = JsonDocument.Parse(jsRawResult);
                string jsonString = parsedJs.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetRawText();
                pipelineResult.Response = JsonSerializer.Deserialize<BiDiContextResponse>(jsonString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var domCmd = new { id = 2, method = "DOM.getDocument", @params = new { depth = -1, pierce = true } };
                var domRawResult = await SendWsCommandAsync(ws, 2, domCmd, cts.Token);
                var liteIdToBackendId = ParseDomTreeForIds(JsonDocument.Parse(domRawResult).RootElement);

                if (liteIdToBackendId.Count > 0)
                {
                    var axCmd = new { id = 3, method = "Accessibility.getFullAXTree" };
                    var axRawResult = await SendWsCommandAsync(ws, 3, axCmd, cts.Token);
                    pipelineResult.AxTreeDict = ParseAxTree(JsonDocument.Parse(axRawResult).RootElement, liteIdToBackendId);
                }

                var cleanupCmd = new { id = 4, method = "Runtime.evaluate", @params = new { expression = "document.querySelectorAll('[lite-ax-id]').forEach(e => e.removeAttribute('lite-ax-id'));" } };
                await SendWsCommandAsync(ws, 4, cleanupCmd, cts.Token);

                return pipelineResult;
            }
            catch (Exception ex)
            {
                LiteLogger.Error("Erro crítico no Pipeline de Acessibilidade CDP.", ex);
                return null;
            }
        }

        private Dictionary<int, int> ParseDomTreeForIds(JsonElement rootDoc)
        {
            var map = new Dictionary<int, int>();
            try
            {
                var rootNode = rootDoc.GetProperty("result").GetProperty("root");
                SearchDomRecursive(rootNode, map);
            }
            catch { }
            return map;
        }

        private void SearchDomRecursive(JsonElement node, Dictionary<int, int> map)
        {
            if (node.TryGetProperty("attributes", out var attrs))
            {
                for (int i = 0; i < attrs.GetArrayLength() - 1; i += 2)
                {
                    if (attrs[i].GetString() == "lite-ax-id")
                    {
                        if (int.TryParse(attrs[i + 1].GetString(), out int liteId))
                        {
                            if (node.TryGetProperty("backendNodeId", out var backendId))
                            {
                                map[liteId] = backendId.GetInt32();
                            }
                        }
                        break;
                    }
                }
            }

            if (node.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    SearchDomRecursive(child, map);
                }
            }
        }

        private Dictionary<int, SemanticNode> ParseAxTree(JsonElement axDoc, Dictionary<int, int> liteIdToBackendId)
        {
            var axTreeDict = new Dictionary<int, SemanticNode>();

            var backendToLiteId = new Dictionary<int, int>();
            foreach (var kvp in liteIdToBackendId) backendToLiteId[kvp.Value] = kvp.Key;

            try
            {
                var nodes = axDoc.GetProperty("result").GetProperty("nodes");
                foreach (var axNode in nodes.EnumerateArray())
                {
                    if (axNode.TryGetProperty("backendDOMNodeId", out var backendIdProp))
                    {
                        int backendId = backendIdProp.GetInt32();
                        if (backendToLiteId.TryGetValue(backendId, out int liteId))
                        {
                            var semantic = new SemanticNode();

                            if (axNode.TryGetProperty("role", out var roleObj) && roleObj.TryGetProperty("value", out var roleVal))
                            {
                                semantic.Role = new LocatorData(roleVal.GetString(), 90);
                            }

                            if (axNode.TryGetProperty("name", out var nameObj) && nameObj.TryGetProperty("value", out var nameVal))
                            {
                                string nameStr = nameVal.GetString();
                                if (!string.IsNullOrWhiteSpace(nameStr))
                                {
                                    semantic.AccessibleName = new LocatorData(nameStr, 95);
                                }
                            }

                            if (axNode.TryGetProperty("description", out var descObj) && descObj.TryGetProperty("value", out var descVal))
                            {
                                string descStr = descVal.GetString();
                                if (!string.IsNullOrWhiteSpace(descStr))
                                {
                                    semantic.HelpText = new LocatorData(descStr, 80);
                                }
                            }

                            axTreeDict[liteId] = semantic;
                        }
                    }
                }
            }
            catch { }
            return axTreeDict;
        }

        public void EvaluateQualityFlags(EngineNode<BiDiElementData> node)
        {
            if (node?.ElementData?.SelectorSet == null) return;
            var set = node.ElementData.SelectorSet;

            var p0 = new[] { set.CustomAttribute, set.Id };
            var p1 = new[] { set.AriaLabel, set.Name, set.Placeholder, set.Alt };
            var p2 = new[] { set.Text, set.Title, set.Css };

            bool amb = false;
            int max = 0;

            foreach (var l in p0) if (l != null) { if (l.Confidence < 0) amb = true; if (Math.Abs(l.Confidence) > max) max = Math.Abs(l.Confidence); }
            foreach (var l in p1) if (l != null) { if (l.Confidence < 0) amb = true; if (Math.Abs(l.Confidence) > max) max = Math.Abs(l.Confidence); }
            foreach (var l in p2) if (l != null) { if (l.Confidence < 0) amb = true; if (Math.Abs(l.Confidence) > max) max = Math.Abs(l.Confidence); }

            if (max == 100) node.QualityFlags.Add("LOCATOR_P0_GOLDEN");
            else if (max >= 80 && max <= 99) node.QualityFlags.Add("LOCATOR_P1_SEMANTIC");
            else if (max >= 70 && max <= 79) node.QualityFlags.Add("LOCATOR_P2_VISUAL");
            else if (max <= 60 && max > 0) node.QualityFlags.Add("WARNING_BRITTLE_LOCATOR");

            if (amb) node.QualityFlags.Add("WARNING_AMBIGUOUS_LOCATOR");
        }

        public void HydrateTrailAxTree(List<InteractionBreadcrumb> trail)
        {
            if (trail == null || trail.Count == 0) return;

            try
            {
                string wsUrl = Task.Run(() => GetActiveWebSocketUrlAsync()).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(wsUrl)) return;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var ws = new ClientWebSocket();
                Task.Run(() => ws.ConnectAsync(new Uri(wsUrl), cts.Token)).GetAwaiter().GetResult();

                int msgId = 8000;

                var domCmd = new { id = msgId, method = "DOM.getDocument", @params = new { depth = 0 } };
                Task.Run(() => SendWsCommandAsync(ws, msgId, domCmd, cts.Token)).GetAwaiter().GetResult();
                msgId++;

                foreach (var step in trail)
                {
                    if (step.AX_Tree == null) step.AX_Tree = new EngineNode<AxTreeElementData> { ElementData = new AxTreeElementData() };
                    if (step.AX_Tree.QualityFlags.Contains("SUCCESS_NATIVE_CHROME_AX_TREE")) continue;

                    var center = step.CenterCoordinates;
                    if (center.X == 0 && center.Y == 0) continue;

                    int locId = msgId++;
                    var locCmd = new { id = locId, method = "DOM.getNodeForLocation", @params = new { x = center.X, y = center.Y } };
                    string locResRaw = Task.Run(() => SendWsCommandAsync(ws, locId, locCmd, cts.Token)).GetAwaiter().GetResult();

                    if (string.IsNullOrEmpty(locResRaw)) continue;

                    var locRes = JsonDocument.Parse(locResRaw);

                    if (locRes.RootElement.TryGetProperty("result", out var resProp) && resProp.TryGetProperty("backendNodeId", out var bNodeProp))
                    {
                        int backendId = bNodeProp.GetInt32();

                        int axId = msgId++;
                        var axCmd = new { id = axId, method = "Accessibility.getPartialAXTree", @params = new { backendNodeId = backendId, fetchRelatives = false } };
                        string axResRaw = Task.Run(() => SendWsCommandAsync(ws, axId, axCmd, cts.Token)).GetAwaiter().GetResult();

                        if (string.IsNullOrEmpty(axResRaw)) continue;

                        var axRes = JsonDocument.Parse(axResRaw);

                        if (axRes.RootElement.TryGetProperty("result", out var axResProp) &&
                            axResProp.TryGetProperty("nodes", out var nodesProp) &&
                            nodesProp.GetArrayLength() > 0)
                        {
                            var axNode = nodesProp[0];
                            var semantic = step.AX_Tree.ElementData.Semantic;

                            if (axNode.TryGetProperty("role", out var roleObj) && roleObj.TryGetProperty("value", out var roleVal))
                                semantic.Role = new LocatorData(roleVal.GetString(), 90);

                            if (axNode.TryGetProperty("name", out var nameObj) && nameObj.TryGetProperty("value", out var nameVal))
                            {
                                string nameStr = nameVal.GetString();
                                if (!string.IsNullOrWhiteSpace(nameStr)) semantic.AccessibleName = new LocatorData(nameStr, 95);
                            }

                            if (axNode.TryGetProperty("description", out var descObj) && descObj.TryGetProperty("value", out var descVal))
                            {
                                string descStr = descVal.GetString();
                                if (!string.IsNullOrWhiteSpace(descStr)) semantic.HelpText = new LocatorData(descStr, 80);
                            }

                            step.AX_Tree.QualityFlags.Add("SUCCESS_NATIVE_CHROME_AX_TREE");
                            if (semantic.AccessibleName != null) step.AX_Tree.QualityFlags.Add("A11Y_NAME_PRESENT");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LiteLogger.Debug($"[HydrateTrailAxTree] Falha ao hidratar AX_Tree via CDP: {ex.Message}");
            }
        }

        private async Task<string> SendSingleEvaluationAsync(string jsCode)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
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
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token).ConfigureAwait(false);

                var jsCmd = new { id = 1, method = "Runtime.evaluate", @params = new { expression = jsCode, returnByValue = true } };

                var jsRawResult = await SendWsCommandAsync(ws, 1, jsCmd, cts.Token);
                if (string.IsNullOrEmpty(jsRawResult)) return null;

                var parsedJs = JsonDocument.Parse(jsRawResult);
                return parsedJs.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetRawText();
            }
            catch { return null; }
        }

        public void EnsureClickTrackerInjected(IntPtr hwnd)
        {
            string jsCode = @"(function() {
                if (window.__liteTrackerInjected_V2) return 'ALREADY_INJECTED';
                window.__liteTrackerInjected_V2 = true;
                window.__liteInteractionTrail = [];

                " + JsLocatorHelpers + @"

                function buildBidiData(el) {
                    var textContent = getElementText(el);
                    var textSelector = textContent ? ""//*[text()='"" + textContent + ""']"" : null;
                    return {
                        elementData: {
                            selectorSet: {
                                customAttribute: getLoc(getCustomAttr(el), 100, false),
                                id: getLoc(el.id ? '#' + cleanStr(el.id) : null, 90, false),
                                ariaLabel: getLoc(el.hasAttribute('aria-label') ? ""[aria-label='"" + cleanStr(el.getAttribute('aria-label')) + ""']"" : null, 85, false),
                                name: getLoc(el.hasAttribute('name') ? ""[name='"" + cleanStr(el.getAttribute('name')) + ""']"" : null, 85, false),
                                placeholder: getLoc(el.hasAttribute('placeholder') ? ""[placeholder='"" + cleanStr(el.getAttribute('placeholder')) + ""']"" : null, 85, false),
                                alt: getLoc(el.hasAttribute('alt') ? ""[alt='"" + cleanStr(el.getAttribute('alt')) + ""']"" : null, 85, false),
                                text: getLoc(textSelector, 75, true),
                                title: getLoc(el.hasAttribute('title') ? ""[title='"" + cleanStr(el.getAttribute('title')) + ""']"" : null, 70, false),
                                css: getLoc(getCssSelector(el), 60, false),
                                xpathRelative: getLoc(getXPathRel(el), 40, true),
                                xpathAbsolute: getLoc(getXPathAbs(el), 10, true)
                            },
                            url: window.location.href,
                            value: el.value || textContent || '',
                            frameworkId: 'Web_HTML_Passive'
                        },
                        qualityFlags: ['SUCCESS_BIDI_INJECTION']
                    };
                }

                function pushInteraction(interaction) {
                    window.__liteInteractionTrail.push(interaction);
                    if (window.__liteInteractionTrail.length > 25) window.__liteInteractionTrail.shift();
                }

                document.addEventListener('click', function(e) {
                    var target = e.target.closest('button, a, input, select') || e.target;
                    var rect = target.getBoundingClientRect ? target.getBoundingClientRect() : {left:0, top:0, width:0, height:0};
                    var text = getElementText(target);

                    pushInteraction({
                        InteractionType: 'click',
                        Timestamp: new Date().toISOString(),
                        TagName: target.tagName ? target.tagName.toLowerCase() : '',
                        ElementId: target.id || '',
                        Classes: target.getAttribute('class') || '',
                        InputType: target.type || '',
                        VisibleText: text,
                        Value: '', 
                        BoundingBox: Math.round(rect.left) + ',' + Math.round(rect.top) + ',' + Math.round(rect.width) + ',' + Math.round(rect.height),
                        ScrollX: Math.round(window.scrollX),
                        ScrollY: Math.round(window.scrollY),
                        UrlPath: window.location.pathname,
                        WebDriver_BiDi: buildBidiData(target)
                    });
                }, true);

                document.addEventListener('keydown', function(e) {
                    if (e.key === 'Tab' || e.key === 'Enter' || e.key === 'Escape') {
                        var target = e.target;
                        var rect = target.getBoundingClientRect ? target.getBoundingClientRect() : {left:0, top:0, width:0, height:0};
                        
                        pushInteraction({
                            InteractionType: 'keypress_' + e.key.toLowerCase(),
                            Timestamp: new Date().toISOString(),
                            TagName: target.tagName ? target.tagName.toLowerCase() : '',
                            ElementId: target.id || '',
                            Classes: target.getAttribute('class') || '',
                            InputType: target.type || '',
                            VisibleText: '', 
                            Value: '',       
                            BoundingBox: Math.round(rect.left) + ',' + Math.round(rect.top) + ',' + Math.round(rect.width) + ',' + Math.round(rect.height),
                            ScrollX: Math.round(window.scrollX),
                            ScrollY: Math.round(window.scrollY),
                            UrlPath: window.location.pathname,
                            WebDriver_BiDi: buildBidiData(target)
                        });
                    }
                }, true);

                document.addEventListener('input', function(e) {
                    var target = e.target;
                    if (!target.tagName) return;
                    var tag = target.tagName.toLowerCase();
                    if (tag !== 'input' && tag !== 'textarea' && tag !== 'select') return;

                    var trail = window.__liteInteractionTrail;
                    if (trail.length > 0) {
                        var last = trail[trail.length - 1];
                        if (last.InteractionType === 'input' && last.ElementId === (target.id || '') && last.TagName === tag) {
                            last.Timestamp = new Date().toISOString();
                            return; 
                        }
                    }

                    var rect = target.getBoundingClientRect ? target.getBoundingClientRect() : {left:0, top:0, width:0, height:0};
                    
                    pushInteraction({
                        InteractionType: 'input',
                        Timestamp: new Date().toISOString(),
                        TagName: tag,
                        ElementId: target.id || '',
                        Classes: target.getAttribute('class') || '',
                        InputType: target.type || '',
                        VisibleText: '', 
                        Value: target.value || '',       
                        BoundingBox: Math.round(rect.left) + ',' + Math.round(rect.top) + ',' + Math.round(rect.width) + ',' + Math.round(rect.height),
                        ScrollX: Math.round(window.scrollX),
                        ScrollY: Math.round(window.scrollY),
                        UrlPath: window.location.pathname,
                        WebDriver_BiDi: buildBidiData(target)
                    });
                }, true);

                document.addEventListener('focus', function(e) {
                    var target = e.target;
                    if (!target || !target.tagName) return;
                    var tag = target.tagName.toLowerCase();

                    if (tag === 'body' || tag === 'html' || tag === 'document') return;

                    var rect = target.getBoundingClientRect ? target.getBoundingClientRect() : {left:0, top:0, width:0, height:0};
                    
                    var trail = window.__liteInteractionTrail;
                    if (trail.length > 0) {
                        var last = trail[trail.length - 1];
                        if (last.InteractionType === 'focus' && last.ElementId === (target.id || '') && last.TagName === tag) {
                            return;
                        }
                    }

                    var text = '';
                    if (tag === 'button' || tag === 'a') text = getElementText(target);

                    pushInteraction({
                        InteractionType: 'focus',
                        Timestamp: new Date().toISOString(),
                        TagName: tag,
                        ElementId: target.id || '',
                        Classes: target.getAttribute('class') || '',
                        InputType: target.type || '',
                        VisibleText: text, 
                        Value: '',         
                        BoundingBox: Math.round(rect.left) + ',' + Math.round(rect.top) + ',' + Math.round(rect.width) + ',' + Math.round(rect.height),
                        ScrollX: Math.round(window.scrollX),
                        ScrollY: Math.round(window.scrollY),
                        UrlPath: window.location.pathname,
                        WebDriver_BiDi: buildBidiData(target)
                    });
                }, true); 

                return 'INJECTED';
            })();";

            Task.Run(() => EvaluateOnAllPagesAsync(jsCode));
        }

        public List<InteractionBreadcrumb> RetrieveAndClearInteractionTrail(IntPtr hwnd)
        {
            string jsCode = @"(function() {
                if (!window.__liteInteractionTrail || window.__liteInteractionTrail.length === 0) return null;
                var clone = JSON.stringify(window.__liteInteractionTrail);
                window.__liteInteractionTrail = [];
                return clone;
            })();";

            var allBreadcrumbs = new List<InteractionBreadcrumb>();
            var results = Task.Run(() => EvaluateOnAllPagesAsync(jsCode)).GetAwaiter().GetResult();

            foreach (var result in results)
            {
                if (!string.IsNullOrEmpty(result) && result != "null")
                {
                    try
                    {
                        var breadcrumbs = JsonSerializer.Deserialize<List<InteractionBreadcrumb>>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (breadcrumbs != null)
                        {
                            foreach (var bc in breadcrumbs)
                            {
                                if (bc.WebDriver_BiDi != null) EvaluateQualityFlags(bc.WebDriver_BiDi);
                                allBreadcrumbs.Add(bc);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LiteLogger.Debug($"Falha ao desserializar a Trilha de Interações de uma aba: {ex.Message}");
                    }
                }
            }

            allBreadcrumbs.Sort((a, b) => string.Compare(a.Timestamp, b.Timestamp));
            return allBreadcrumbs;
        }
    }
}