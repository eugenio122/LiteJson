using System;
using System.Text.RegularExpressions;
using Interop.UIAutomationClient;
using LiteJson.Diagnostics;
using LiteJson.Models;

namespace LiteJson.Adapters
{
    public class UIAAdapter
    {
        public EngineNode<UiaElementData> ExtractUiaNode(IUIAutomationElement element)
        {
            var uiaNode = new EngineNode<UiaElementData> { ElementData = new UiaElementData() };

            if (element == null)
            {
                LiteLogger.Debug("[UIAAdapter.ExtractUiaNode] IUIAutomationElement fornecido é nulo.");
                uiaNode.QualityFlags.Add("UIA_ELEMENT_NOT_FOUND");
                return uiaNode;
            }

            try
            {
                string autoId = element.CurrentAutomationId ?? string.Empty;
                string rawName = element.CurrentName ?? string.Empty;
                string role = element.CurrentLocalizedControlType ?? "unknown";
                string rawHelp = element.CurrentHelpText ?? string.Empty;

                // LGPD Compliance: Passando o Name e o HelpText para formar o "Full Context" 
                // e blindar qualquer dado sensível!
                string fullContext = rawName + " " + rawHelp;
                string name = SanitizePII(rawName, autoId, fullContext);
                string help = SanitizePII(rawHelp, autoId, fullContext);

                var rect = element.CurrentBoundingRectangle;
                string bounds = $"{rect.left},{rect.top},{rect.right - rect.left},{rect.bottom - rect.top}";

                var semUIA = uiaNode.ElementData.Semantic;

                if (!string.IsNullOrEmpty(autoId)) semUIA.AutomationId = new LocatorData(autoId, 100);

                if (!string.IsNullOrEmpty(name))
                {
                    semUIA.AccessibleName = new LocatorData(name, 85);
                }

                semUIA.Role = new LocatorData(role, 80);

                if (!string.IsNullOrEmpty(help))
                {
                    semUIA.HelpText = new LocatorData(help, 70);
                }

                uiaNode.ElementData.UiaClassName = element.CurrentClassName ?? string.Empty;
                uiaNode.ElementData.BoundingRectangle = bounds;

                if (!string.IsNullOrEmpty(autoId))
                {
                    uiaNode.QualityFlags.Add("A11Y_AUTOMATION_ID_PRESENT");
                }

                if (!string.IsNullOrEmpty(name))
                {
                    uiaNode.QualityFlags.Add("A11Y_NAME_PRESENT");
                }

                if (string.IsNullOrEmpty(autoId) && string.IsNullOrEmpty(name))
                {
                    uiaNode.QualityFlags.Add("A11Y_GAP_WARNING");
                }
            }
            catch (Exception ex)
            {
                LiteLogger.Debug($"[UIAAdapter.ExtractUiaNode] Exceção COM ao ler propriedades do elemento: {ex.Message}");
                uiaNode.QualityFlags.Add("UIA_COM_EXCEPTION");
            }

            return uiaNode;
        }

        public ObservedContext ExtractObservedContext(IUIAutomationElement rootElement)
        {
            var context = new ObservedContext();

            if (rootElement == null)
            {
                LiteLogger.Debug("[UIAAdapter.ExtractObservedContext] rootElement fornecido é nulo. Varredura abortada.");
                return context;
            }

            try
            {
                var automation = new CUIAutomation();

                context.PageTitle = SanitizePII(rootElement.CurrentName ?? string.Empty, "", "");

                LiteLogger.Debug($"[UIAAdapter.ExtractObservedContext] Iniciando varredura nativa UIA. Título raiz: '{context.PageTitle}'.");

                int[] controlTypes = new[]
                {
                    50000, // UIA_ButtonControlTypeId
                    50004, // UIA_EditControlTypeId (Input)
                    50002, // UIA_CheckBoxControlTypeId
                    50013, // UIA_RadioButtonControlTypeId
                    50003, // UIA_ComboBoxControlTypeId (Select)
                    50020, // UIA_TextControlTypeId
                    50005  // UIA_HyperlinkControlTypeId (Link)
                };

                IUIAutomationCondition finalCondition = automation.CreatePropertyCondition(30003, controlTypes[0]);
                for (int i = 1; i < controlTypes.Length; i++)
                {
                    var nextCondition = automation.CreatePropertyCondition(30003, controlTypes[i]);
                    finalCondition = automation.CreateOrCondition(finalCondition, nextCondition);
                }

                var elementArray = rootElement.FindAll(TreeScope.TreeScope_Descendants, finalCondition);

                if (elementArray == null)
                {
                    LiteLogger.Debug("[UIAAdapter.ExtractObservedContext] FindAll não encontrou elementos ou retornou null.");
                    return context;
                }

                int count = elementArray.Length;
                int maxElements = Math.Min(count, 150);

                LiteLogger.Debug($"[UIAAdapter.ExtractObservedContext] {count} elementos UIA detectados. Processando até o limite de {maxElements}...");

                for (int i = 0; i < maxElements; i++)
                {
                    var el = elementArray.GetElement(i);
                    if (el == null) continue;

                    try
                    {
                        if (el.CurrentIsOffscreen != 0) continue;

                        int controlType = el.CurrentControlType;
                        string name = el.CurrentName ?? string.Empty;

                        if (controlType == 50020 && string.IsNullOrWhiteSpace(name)) continue;

                        var visibleElement = new VisibleElement
                        {
                            ElementType = MapControlType(controlType),
                            IsEnabled = el.CurrentIsEnabled != 0
                        };

                        var uiaNode = ExtractUiaNode(el);
                        uiaNode.QualityFlags.Add("SUCCESS_UIA_CONTEXT");

                        visibleElement.CapturedData.UIA = uiaNode;

                        visibleElement.CapturedData.WebDriver_BiDi = null;
                        visibleElement.CapturedData.AX_Tree = new EngineNode<AxTreeElementData> { ElementData = new AxTreeElementData() };

                        context.VisibleElements.Add(visibleElement);
                    }
                    catch (Exception elEx)
                    {
                        LiteLogger.Debug($"[UIAAdapter.ExtractObservedContext] Falha ao processar UIA Index {i}. Objeto COM inacessível. {elEx.Message}");
                    }
                }

                LiteLogger.Debug($"[UIAAdapter.ExtractObservedContext] Varredura UIA finalizada. {context.VisibleElements.Count} elementos extraídos para o Fat Payload.");
            }
            catch (Exception rootEx)
            {
                LiteLogger.Error("[UIAAdapter.ExtractObservedContext] Erro catastrófico ao iterar árvore UIA do Windows.", rootEx);
            }

            return context;
        }

        private string MapControlType(int controlType)
        {
            switch (controlType)
            {
                case 50000: return "button";
                case 50004: return "input";
                case 50002: return "checkbox";
                case 50013: return "radio";
                case 50003: return "select";
                case 50020: return "text";
                case 50005: return "link";
                default: return "unknown";
            }
        }

        private string SanitizePII(string text, string autoId, string nameContext)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            string lowerId = (autoId ?? "").ToLower();
            string lowerCtx = (nameContext ?? "").ToLower();

            if (lowerId.Contains("senha") || lowerId.Contains("password") ||
                lowerCtx.Contains("senha") || lowerCtx.Contains("password") ||
                lowerCtx.Contains("pwd"))
            {
                return "[SENHA OCULTA]";
            }

            // Expandido para pegar 'e-mail' também, cobrindo o placeholder do seu site
            string[] keys = { "cpf", "cnpj", "documento", "email", "e-mail", "telefone", "celular", "cartao", "creditcard", "cep", "endereco", "address" };
            foreach (var key in keys)
            {
                if (lowerId.Contains(key) || lowerCtx.Contains(key))
                {
                    return "[DADO SENSÍVEL OCULTO]";
                }
            }

            if (lowerId.Contains("nome") || lowerId.Contains("name") ||
                lowerCtx.Contains("nome") || lowerCtx.Contains("name"))
            {
                if (text.Trim().Contains(" ")) return "[NOME COMPLETO OCULTO]";
            }

            string masked = text;
            masked = Regex.Replace(masked, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", "[EMAIL OCULTO]");
            masked = Regex.Replace(masked, @"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", "[CPF OCULTO]");
            masked = Regex.Replace(masked, @"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b", "[CNPJ OCULTO]");
            masked = Regex.Replace(masked, @"\b(?:\d[ -]*?){13,16}\b", "[CARTAO OCULTO]");

            masked = Regex.Replace(masked, @"(?:\+?55\s?)?(?:\(?\d{2}\)?\s?)?\d{4,5}[-\s]?\d{4}", m =>
            {
                string digits = Regex.Replace(m.Value, @"\D", "");
                if (digits.Length >= 8 && digits.Length <= 13) return "[TELEFONE OCULTO]";
                return m.Value;
            });

            return masked;
        }
    }
}