using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 3. O EIXO OBSERVACIONAL (A FOTOGRAFIA)
    // ==========================================

    /// <summary>
    /// Representa a fotografia funcional da página/janela no momento do snapshot.
    /// É a base para as comparações lógicas (asserts) do LiteAutomation.
    /// </summary>
    public class ObservedContext
    {
        /// <summary>
        /// Chave estrangeira que conecta esta fotografia ao seu <see cref="ExtractionPayload"/>.
        /// Sempre igual ao <c>StepId</c> do payload que a contém.
        /// Injetado pelo orquestrador no momento do print.
        /// </summary>
        public string AssociatedStepId { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;
        public string PageTitle { get; set; } = string.Empty;

        // Contexto de Resolução do Monitor (Ajuda a IA a calcular limites visuais)
        public int ViewportWidth { get; set; }
        public int ViewportHeight { get; set; }

        public List<VisibleElement> VisibleElements { get; set; } = new List<VisibleElement>();
    }

}