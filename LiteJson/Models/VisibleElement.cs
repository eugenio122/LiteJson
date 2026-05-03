using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 5. NOVOS MODELOS V2 (O EIXO OBSERVACIONAL)
    // ==========================================

    /// <summary>
    /// Representa um único elemento extraído durante a varredura do Snapshot.
    /// Reutiliza a nossa gaveta simétrica "CapturedData" para garantir o mesmo padrão dos MicroSteps.
    /// </summary>
    public class VisibleElement
    {
        /// <summary>
        /// Classificação semântica do elemento. 
        /// Ex: "input", "button", "select", "checkbox", "text" (para mensagens de erro/sucesso).
        /// </summary>
        public string ElementType { get; set; } = string.Empty;

        /// <summary>
        /// Indica se o elemento está disponível para interação.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        // Coordenadas relativas ao Viewport do navegador para Hidratação Assíncrona
        public int CenterX { get; set; }
        public int CenterY { get; set; }


        /// <summary>
        /// A gaveta simétrica idêntica à usada nos MicroSteps (com UIA, AX_Tree e BiDi).
        /// </summary>
        public CapturedData CapturedData { get; set; } = new CapturedData();
    }
}
