using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 6. SEMÂNTICA E SELETORES (ESTABILIDADE)
    // ==========================================

    /// <summary>
    /// O "Cofre" de Seletores Estruturais (WebDriver BiDi / DOM).
    /// </summary>
    public class SelectorSet
    {
        // --- PRIORIDADE P0 (Padrão Ouro) ---

        /// <summary>Score: 100 (Blindado para testes: data-testid)</summary>
        public LocatorData CustomAttribute { get; set; }

        /// <summary>Score: 90 (Forte, mas sujeito a geração dinâmica no React/Angular)</summary>
        public LocatorData Id { get; set; }


        // --- PRIORIDADE P1 (Formulários e Acessibilidade) ---

        /// <summary>Score: 85 (Regra Ouro de Acessibilidade Web)</summary>
        public LocatorData AriaLabel { get; set; }

        /// <summary>Score: 85 (Robusto para formulários)</summary>
        public LocatorData Name { get; set; }

        /// <summary>Score: 85 (Intenção visual clara em formulários)</summary>
        public LocatorData Placeholder { get; set; }

        /// <summary>Score: 85 (Vital para imagens e ícones representativos)</summary>
        public LocatorData Alt { get; set; }


        // --- PRIORIDADE P2 (Estilos e Textos Visíveis) ---

        /// <summary>Score: 75 (Muito bom, mas quebra se o site mudar de idioma)</summary>
        public LocatorData Text { get; set; }

        /// <summary>Score: 70 (Tooltip nativo, excelente fallback para ícones vazios)</summary>
        public LocatorData Title { get; set; }

        /// <summary>Score: 60 (Quebra com re-designs ou frameworks de CSS utilitário)</summary>
        public LocatorData Css { get; set; }


        // --- PRIORIDADE P3 e P4 (Fallbacks Estruturais) ---

        /// <summary>Score: 40 (Depende da estrutura óssea do DOM, medianamente instável)</summary>
        public LocatorData XpathRelative { get; set; }

        /// <summary>Score: 10 (Quebra com qualquer div adicionada na página. Último recurso)</summary>
        public LocatorData XpathAbsolute { get; set; }
    }
}
