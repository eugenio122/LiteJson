using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 6. SEMÂNTICA E SELETORES (ESTABILIDADE)
    // ==========================================

    public class SemanticNode
    {
        /// <summary>
        /// Score: 100 (Bulletproof)
        /// </summary>
        public LocatorData AutomationId { get; set; }

        /// <summary>
        /// Score: 85 (Semântica pura lida pelos utilizadores)
        /// </summary>
        public LocatorData AccessibleName { get; set; }

        /// <summary>
        /// Score: 80 (Geralmente usado em conjunto com o AccessibleName)
        /// </summary>
        public LocatorData Role { get; set; }

        /// <summary>
        /// Score: 70 (Dicas de contexto)
        /// </summary>
        public LocatorData HelpText { get; set; }
    }
}
