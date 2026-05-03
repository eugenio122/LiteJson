using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteJson.Models;

namespace LiteJson.Core
{
    public class PassiveBiDiListener
    {
        private const int DebugPort = 9222;
        public event Action<MicroStepPayload> MicroStepDetected;
        private CancellationTokenSource _cts;

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ListenerLoopAsync(_cts.Token));
        }

        public void Stop() { _cts?.Cancel(); }

        private async Task ListenerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var config = LiteJsonConfig.Load();
                    if (config.Target == TargetEngine.WebUniversal)
                    {
                        string wsUrl = await GetActivePageWebSocketUrlAsync();
                        if (!string.IsNullOrEmpty(wsUrl)) await ConnectAndListenAsync(wsUrl, token);
                    }
                }
                catch { }
                await Task.Delay(2000, token);
            }
        }

        private async Task<string> GetActivePageWebSocketUrlAsync()
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await httpClient.GetStringAsync($"http://127.0.0.1:{DebugPort}/json").ConfigureAwait(false);
            using var jsonDoc = JsonDocument.Parse(response);
            foreach (var item in jsonDoc.RootElement.EnumerateArray())
                if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page")
                    if (item.TryGetProperty("webSocketDebuggerUrl", out var wsUrlProp)) return wsUrlProp.GetString() ?? "";
            return string.Empty;
        }

        private async Task ConnectAndListenAsync(string wsUrl, CancellationToken token)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), token).ConfigureAwait(false);

            await SendCommandAsync(ws, 1, "Runtime.enable", "{}", token);
            await SendCommandAsync(ws, 2, "Page.enable", "{}", token);

            string jsCode = @"
                (function() {
                    if (window.__liteQaInjected) return;
                    window.__liteQaInjected = true;

                    function checkUnique(selector, isXpath) {
                        if (!selector) return false;
                        try {
                            if (isXpath) {
                                var res = document.evaluate(selector, document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
                                return res.snapshotLength === 1;
                            }
                            return document.querySelectorAll(selector).length === 1;
                        } catch(e) { return false; }
                    }

                    function getLoc(value, score, isXpath) {
                        if (!value) return null;
                        var unique = checkUnique(value, isXpath);
                        return { Value: value, Confidence: unique ? score : (score * -1) };
                    }
                    
                    function getCustomAttr(node) {
                        var attrs = ['data-testid', 'data-cy', 'data-test'];
                        for(var i=0; i<attrs.length; i++) {
                            var val = node.getAttribute(attrs[i]);
                            if (val) return '[' + attrs[i] + '=""' + val + '""]';
                        }
                        return null;
                    }

                    function getCssSelector(node) {
                        if (node.id) return '#' + node.id;
                        if (node.className && typeof node.className === 'string') {
                            var classes = node.className.trim().split(/\s+/).join('.');
                            if (classes) return node.tagName.toLowerCase() + '.' + classes;
                        }
                        return node.tagName.toLowerCase();
                    }

                    function getXPathAbs(node) {
                        if (!node || node.nodeType !== 1) return '';
                        if (node.tagName.toLowerCase() === 'html') return '/html';
                        if (node === document.body) return '/html/body';
                        var ix = 0;
                        var siblings = node.parentNode ? node.parentNode.childNodes : [];
                        for (var i = 0; i < siblings.length; i++) {
                            var sibling = siblings[i];
                            if (sibling === node) return getXPathAbs(node.parentNode) + '/' + node.tagName.toLowerCase() + '[' + (ix + 1) + ']';
                            if (sibling.nodeType === 1 && sibling.tagName === node.tagName) ix++;
                        }
                        return '';
                    }

                    function getXPathRel(node) {
                        if (node.id) return '//' + node.tagName.toLowerCase() + '[@id=""' + node.id + '""]';
                        return getXPathAbs(node);
                    }

                    function buildPayload(actionName, el, e) {
                        var metaData = null;
                        if (e) {
                            metaData = {
                                CursorX: e.offsetX !== undefined ? Math.round(e.offsetX) : null,
                                CursorY: e.offsetY !== undefined ? Math.round(e.offsetY) : null,
                                CtrlKey: !!e.ctrlKey,
                                ShiftKey: !!e.shiftKey,
                                AltKey: !!e.altKey
                            };
                        }

                        var textContent = (el.innerText || '').trim();
                        var textSelector = textContent ? '//*[text()=""' + textContent + '""]' : null;

                        var bidiNode = {
                            elementData: {
                                selectorSet: {
                                    customAttribute: getLoc(getCustomAttr(el), 100, false),
                                    id: getLoc(el.id ? '#' + el.id : null, 90, false),
                                    ariaLabel: getLoc(el.getAttribute('aria-label') ? '[aria-label=""' + el.getAttribute('aria-label') + '""]' : null, 85, false),
                                    name: getLoc(el.name ? '[name=""' + el.name + '""]' : null, 85, false),
                                    placeholder: getLoc(el.placeholder ? '[placeholder=""' + el.placeholder + '""]' : null, 85, false),
                                    alt: getLoc(el.alt ? '[alt=""' + el.alt + '""]' : null, 85, false),
                                    text: getLoc(textSelector, 75, true),
                                    title: getLoc(el.title ? '[title=""' + el.title + '""]' : null, 70, false),
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

                        var payload = {
                            actionType: actionName,
                            triggerEngine: 'WebDriver_BiDi',
                            timestamp: new Date().toISOString(),
                            actionMeta: metaData,
                            capturedData: {
                                WebDriver_BiDi: bidiNode
                            }
                        };
                        console.debug('[LITE_QA_STEP]', JSON.stringify(payload));
                    }

                    function logEvent(e) {
                        var el = e.target;
                        if (!el || el.nodeType !== 1) return;
                        if (el.tagName.toLowerCase() === 'html' || el.tagName.toLowerCase() === 'body') return;
                        buildPayload(e.type, el, e);
                    }

                    function logKeyEvent(e) {
                        const actionKeys = ['Enter', 'Tab', 'Escape', 'ArrowDown', 'ArrowUp', 'Space'];
                        if (!actionKeys.includes(e.key)) return;
                        var el = e.target;
                        if (!el || el.nodeType !== 1) return;
                        if (el.tagName.toLowerCase() === 'html' || el.tagName.toLowerCase() === 'body') return;
                        buildPayload('keypress_' + e.key.toLowerCase(), el, e);
                    }

                    document.addEventListener('click', logEvent, true);
                    document.addEventListener('change', logEvent, true);
                    document.addEventListener('keydown', logKeyEvent, true);
                })();";

            var preloadParams = new { source = jsCode };
            await SendCommandAsync(ws, 3, "Page.addScriptToEvaluateOnNewDocument", JsonSerializer.Serialize(preloadParams), token);
            await SendCommandAsync(ws, 4, "Runtime.evaluate", JsonSerializer.Serialize(new { expression = jsCode }), token);

            var buffer = new byte[16384];
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                ProcessMessage(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }

        private async Task SendCommandAsync(ClientWebSocket ws, int id, string method, string paramsJson, CancellationToken token)
        {
            string cmd = $"{{\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}";
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(cmd)), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }

        private void ProcessMessage(string message)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (root.TryGetProperty("method", out var methodProp) && methodProp.GetString() == "Runtime.consoleAPICalled")
                {
                    if (root.TryGetProperty("params", out var paramsObj) && paramsObj.TryGetProperty("args", out var argsArray))
                    {
                        if (argsArray[0].TryGetProperty("value", out var valProp) && valProp.GetString() == "[LITE_QA_STEP]")
                        {
                            string payloadJson = argsArray[1].GetProperty("value").GetString();
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var microStep = JsonSerializer.Deserialize<MicroStepPayload>(payloadJson, options);

                            if (microStep != null)
                            {
                                // Calcula os semáforos no lado C#
                                EvaluateQualityFlags(microStep.CapturedData.WebDriver_BiDi);
                                MicroStepDetected?.Invoke(microStep);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Avaliador de Qualidade (Matemática do Semáforo)
        private void EvaluateQualityFlags(EngineNode<BiDiElementData> node)
        {
            if (node == null || node.ElementData == null || node.ElementData.SelectorSet == null) return;
            var set = node.ElementData.SelectorSet;
            var locators = new[] { set.CustomAttribute, set.Id, set.AriaLabel, set.Name, set.Placeholder, set.Alt, set.Text, set.Title, set.Css, set.XpathRelative, set.XpathAbsolute };

            bool hasAmbiguous = false;
            int maxPositiveScore = 0;

            foreach (var loc in locators)
            {
                if (loc != null)
                {
                    if (loc.Confidence < 0) hasAmbiguous = true;
                    if (loc.Confidence > maxPositiveScore) maxPositiveScore = loc.Confidence;
                }
            }

            if (maxPositiveScore == 100) node.QualityFlags.Add("LOCATOR_P0_GOLDEN");
            else if (maxPositiveScore >= 80) node.QualityFlags.Add("LOCATOR_P1_SEMANTIC");
            else if (maxPositiveScore >= 70) node.QualityFlags.Add("LOCATOR_P2_VISUAL");
            else if (maxPositiveScore <= 60 && maxPositiveScore > 0) node.QualityFlags.Add("WARNING_BRITTLE_LOCATOR");

            if (hasAmbiguous) node.QualityFlags.Add("WARNING_AMBIGUOUS_LOCATOR");
        }
    }
}