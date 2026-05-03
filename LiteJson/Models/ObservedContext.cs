using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 5. NOVOS MODELOS V2 (O EIXO OBSERVACIONAL)
    // ==========================================

    /// <summary>
    /// Representa a fotografia funcional da página/janela no momento do snapshot.
    /// É a base para as comparações lógicas (asserts) do LiteAutomation.
    /// </summary>
    public class ObservedContext
    {
        /// <summary>A URL da página no momento da observação.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>O Título da aba do navegador (document.title). Vital para rastrear SPAs.</summary>
        public string PageTitle { get; set; } = string.Empty;

        /// <summary>Lista de todos os elementos interativos relevantes encontrados na tela.</summary>
        public List<VisibleElement> VisibleElements { get; set; } = new List<VisibleElement>();
    }

}
