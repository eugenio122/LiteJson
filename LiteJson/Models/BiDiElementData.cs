using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 4. NÓS DE DADOS ESPECÍFICOS (ELEMENT DATA)
    // ==========================================
    public class BiDiElementData
    {
        public SelectorSet SelectorSet { get; set; } = new SelectorSet();

        // Propriedades descritivas (Não são seletores, logo continuam como string pura)
        public string Url { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string FrameworkId { get; set; } = "Web_HTML";
    }
}
