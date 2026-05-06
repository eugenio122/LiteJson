using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 6. SEMÂNTICA E SELETORES (ESTABILIDADE)
    // ==========================================

    /// <summary>
    /// Embrulha o valor do seletor juntamente com o seu Score de Confiança (0 a 100).
    /// Permite que o LiteAutomation tome decisões baseadas em matemática simples.
    /// </summary>
    public class LocatorData
    {
        public string Value { get; set; }
        public int Confidence { get; set; }

        // Construtor vazio necessário para a serialização JSON
        public LocatorData() { }

        public LocatorData(string value, int confidence)
        {
            Value = value;
            Confidence = confidence;
        }
    }
}
