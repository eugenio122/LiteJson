using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 5. NÓS DE DADOS ESPECÍFICOS (ENGINE DATA)
    // ==========================================

    public class BiDiElementData
    {
        public SelectorSet SelectorSet { get; set; } = new SelectorSet();

        // Propriedades descritivas (Não são seletores, logo continuam como string pura)
        public string Url { get; set; } = string.Empty;

        /// <summary>O valor atual digitado (para inputs) ou o innerText visível (para botões/divs).</summary>
        public string Value { get; set; } = string.Empty;
        public string FrameworkId { get; set; } = "Web_HTML";
    }
}
