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
    // Classes internas para deserialização rápida do JSON que volta do navegador
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
        public string ElementType { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool IsFocused { get; set; } = false;
        public bool? IsChecked { get; set; }
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public BiDiElementData BidiData { get; set; } = new BiDiElementData();

        // NOVO: A árvore espelhada no C# antes da conversão final
        public List<BiDiContextElement> Children { get; set; } = new List<BiDiContextElement>();
    }

    public class WebDriverBiDiAdapter
    {
        private const int DebugPort = 9222;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        // 🧠 O Cérebro JS: Helpers para extrair Seletores e Textos Voláteis
        private const string JsLocatorHelpers = @"
            function checkUnique(selector, isXpath) { try { if (isXpath) { return document.evaluate(selector, document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null).snapshotLength === 1; } return document.querySelectorAll(selector).length === 1; } catch(e) { return false; } }
            function getLoc(value, score, isXpath) { if (!value) return null; var unique = checkUnique(value, isXpath); return { Value: value, Confidence: unique ? score : (score * -1) }; }
            function getCustomAttr(node) { var attrs = ['data-testid', 'data-cy', 'data-test']; for(var i=0; i<attrs.length; i++) { var val = node.getAttribute(attrs[i]); if (val) return '[' + attrs[i] + '=""' + val + '""]'; } return null; }
            function getXPathAbs(node) { if (!node || node.nodeType !== 1) return ''; if (node.tagName.toLowerCase() === 'html') return '/html'; if (node === document.body) return '/html/body'; var ix = 0; var siblings = node.parentNode ? node.parentNode.childNodes : []; for (var i = 0; i < siblings.length; i++) { var sibling = siblings[i]; if (sibling === node) return getXPathAbs(node.parentNode) + '/' + node.tagName.toLowerCase() + '[' + (ix + 1) + ']'; if (sibling.nodeType === 1 && sibling.tagName === node.tagName) ix++; } return ''; }
            function getElementText(el) {
                if (el.tagName.toLowerCase() === 'input' || el.tagName.toLowerCase() === 'textarea') { return el.value || el.placeholder || ''; }
                if (el.tagName.toLowerCase() === 'select') { return el.options[el.selectedIndex] ? el.options[el.selectedIndex].text : ''; }
                var text = el.innerText || el.textContent || '';
                return text.trim().substring(0, 100); 
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
                    value: getElementText(el),
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

            // 🎯 O "Sniper Recursivo": Event Bubbling Reverso e Varredura Profunda
            string jsCode = @"(function() {
                " + JsLocatorHelpers + @"
                
                var TARGET_SELECTORS = 'input, button, select, textarea, a, [role=""button""], [role=""checkbox""], [role=""radio""], [role=""switch""], [role=""dialog""], [role=""alert""], [role=""menu""], [role=""menuitem""], [role=""listbox""], [role=""option""], [role=""tooltip""]';
                var MAX_ELEMENTS = 250;
                var elementCount = 0;

                // Identifica se um filho não interativo carrega o visual (ex: um SVG ou Contador Numérico dentro do botão)
                function hasVisualMeaning(el) {
                    var tag = el.tagName.toLowerCase();
                    if (tag === 'svg' || tag === 'img' || tag === 'i') return true;
                    // Se a div/span só tem texto visível e relevante, ele tem significado
                    for(var i=0; i<el.childNodes.length; i++){
                        var child = el.childNodes[i];
                        if (child.nodeType === 3 && child.nodeValue.trim().length > 0) return true;
                    }
                    return false;
                }

                function extractNodeData(el, rect) {
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
                    var finalValue = (dynState + getElementText(el)).trim();

                    return {
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
                                id: getLoc(el.id ? '#' + el.id : null, 90, false),
                                ariaLabel: getLoc(el.getAttribute('aria-label') ? '[aria-label=""' + el.getAttribute('aria-label') + '""]' : null, 85, false),
                                name: getLoc(el.name ? '[name=""' + el.name + '""]' : null, 85, false),
                                placeholder: getLoc(el.placeholder ? '[placeholder=""' + el.placeholder + '""]' : null, 85, false),
                                alt: getLoc(el.alt ? '[alt=""' + el.alt + '""]' : null, 85, false),
                                title: getLoc(el.title ? '[title=""' + el.title + '""]' : null, 70, false),
                                xpathAbsolute: getLoc(getXPathAbs(el), 10, true)
                            },
                            url: window.location.href,
                            value: finalValue,
                            frameworkId: 'Web_HTML'
                        }
                    };
                }

                // O DOM Walker Recursivo
                function walk(node, depth, isInsideTarget) {
                    if (elementCount >= MAX_ELEMENTS) return null;
                    if (depth > 8) return null; // Trava contra Stack Overflow ou divs muito profundas
                    if (!node || node.nodeType !== 1) return null; 

                    var style = window.getComputedStyle(node);
                    if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') return null;

                    var rect = node.getBoundingClientRect();
                    if (rect.width === 0 || rect.height === 0) return null;
                    if (rect.bottom <= 0 || rect.top >= window.innerHeight || rect.right <= 0 || rect.left >= window.innerWidth) return null;

                    var isCurrentTarget = false;
                    try { isCurrentTarget = node.matches(TARGET_SELECTORS); } catch(e) {}
                    
                    // Deve capturar se for um alvo válido, OU se já estiver dentro de um alvo e o nó tiver carga visual (span/svg)
                    var shouldCapture = isCurrentTarget || (isInsideTarget && hasVisualMeaning(node));
                    
                    var childrenData = [];
                    var passInsideTarget = isInsideTarget || isCurrentTarget;
                    
                    var childrenNodes = node.shadowRoot ? node.shadowRoot.children : node.children;
                    for(var i=0; i<childrenNodes.length; i++) {
                        var childRes = walk(childrenNodes[i], depth + 1, passInsideTarget);
                        if (childRes) {
                            if (Array.isArray(childRes)) {
                                childrenData = childrenData.concat(childRes); // Bubbling reverso das divs não semânticas
                            } else {
                                childrenData.push(childRes);
                            }
                        }
                    }

                    if (shouldCapture) {
                        elementCount++;
                        var elData = extractNodeData(node, rect);
                        if (childrenData.length > 0) elData.Children = childrenData;
                        return elData;
                    } else {
                        // Se for uma div inútil mas conter botões/alvos válidos dentro, propaga para cima (Bubbling Reverso)
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

            var result = Task.Run(() => ExecuteBiDiScriptAsync(jsCode)).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(result))
            {
                try
                {
                    var response = JsonSerializer.Deserialize<BiDiContextResponse>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (response != null)
                    {
                        context.Url = response.Url;
                        context.PageTitle = response.PageTitle;
                        context.ViewportWidth = response.ViewportWidth;
                        context.ViewportHeight = response.ViewportHeight;

                        // Mapeia recursivamente a árvore JS para a Árvore C#
                        if (response.Elements != null)
                        {
                            foreach (var el in response.Elements)
                            {
                                context.VisibleElements.Add(MapBiDiToVisibleElement(el));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LiteLogger.Error("Falha ao desserializar JSON hierárquico do BiDi", ex);
                }
            }
            return context;
        }

        // Função recursiva para transformar o objeto do BiDi na nossa Model principal e assinalar as Flags
        private VisibleElement MapBiDiToVisibleElement(BiDiContextElement el)
        {
            var visibleElement = new VisibleElement
            {
                ElementType = el.ElementType,
                IsEnabled = el.IsEnabled,
                IsFocused = el.IsFocused,
                IsChecked = el.IsChecked,
                CenterX = el.CenterX,
                CenterY = el.CenterY,
                Width = el.Width,
                Height = el.Height
            };

            var bidiNode = new EngineNode<BiDiElementData> { ElementData = el.BidiData };
            bidiNode.QualityFlags.Add("SUCCESS_BIDI_CONTEXT");
            EvaluateQualityFlags(bidiNode);

            visibleElement.CapturedData.WebDriver_BiDi = bidiNode;

            // Inicializa as gavetas vazias para o UIA hidratar posteriormente
            visibleElement.CapturedData.UIA = new EngineNode<UiaElementData> { ElementData = new UiaElementData() };
            visibleElement.CapturedData.AX_Tree = new EngineNode<AxTreeElementData> { ElementData = new AxTreeElementData() };

            if (el.Children != null && el.Children.Count > 0)
            {
                foreach (var child in el.Children)
                {
                    visibleElement.Children.Add(MapBiDiToVisibleElement(child));
                }
            }

            return visibleElement;
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

                var buffer = new byte[8192];
                using var ms = new MemoryStream();

                while (true)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) break;
                }

                var msgDoc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
                return msgDoc.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetRawText();
            }
            catch (Exception ex)
            {
                LiteLogger.Error("Erro crítico no canal de comunicação WebSocket.", ex);
                return null;
            }
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
    }
}