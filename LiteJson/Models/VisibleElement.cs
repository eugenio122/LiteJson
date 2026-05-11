using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace LiteJson.Models
{
    // ==========================================
    // 3. O EIXO OBSERVACIONAL (A FOTOGRAFIA)
    // ==========================================

    /// <summary>
    /// Representa um único elemento extraído durante a varredura do Snapshot.
    /// Reutiliza a nossa gaveta simétrica "CapturedData" para garantir o mesmo padrão dos MicroSteps.
    /// </summary>
    public class VisibleElement
    {
        /// <summary>
        /// ID único de rastreabilidade deste nó no snapshot (Ex: "web-node-42").
        /// Vital para relatórios de BDD, logs de execução e renderização visual.
        /// </summary>
        public string NodeId { get; set; } = string.Empty;

        /// <summary>
        /// Classificação semântica do elemento. 
        /// Ex: "input", "button", "select", "checkbox", "text" (para mensagens de erro/sucesso).
        /// </summary>
        public string ElementType { get; set; } = string.Empty;

        /// <summary>
        /// Indica se o elemento está disponível para interação.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>O cursor piscando estava aqui? Forte indício de digitação (Preencher).</summary>
        public bool IsFocused { get; set; } = false;

        /// <summary>Estado vital para Checkboxes, Radios e Toggles (null se não aplicável).</summary>
        public bool? IsChecked { get; set; }

        // --- Geometria Completa (O Mapa de Calor) ---
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }


        /// <summary>
        /// A gaveta simétrica idêntica à usada nos MicroSteps (com UIA, AX_Tree e BiDi).
        /// </summary>
        public CapturedData CapturedData { get; set; } = new CapturedData();

        /// <summary>
        /// O "Ninho": Mapeia a hierarquia real do DOM. Permite o Event Bubbling Reverso no LiteAutomation.
        /// </summary>
        public List<VisibleElement> Children { get; set; } = new List<VisibleElement>();

        /// <summary>
        /// Para marcar o dono da intenção
        /// </summary>
        public bool IsSemanticAnchor { get; set; }

        /// <summary>
        /// para navegação interna sem quebrar a serialização JSON (evita loops infinitos)
        /// </summary>
        [JsonIgnore] public VisibleElement Parent { get; set; }
    }
}
