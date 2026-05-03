using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 1. PAYLOADS PRINCIPAIS
    // ==========================================

    /// <summary>
    /// Guarda detalhes de como a ação foi executada fisicamente pelo humano.
    /// Crucial para lidar com telas interativas e mapeamentos avançados.
    /// </summary>
    public class ActionMeta
    {
        // Coordenadas relativas ao elemento (vital para interações em tags <canvas> ou mapas)
        public int? CursorX { get; set; }
        public int? CursorY { get; set; }

        // Modificadores de teclado pressionados durante o clique ou digitação
        public bool CtrlKey { get; set; }
        public bool ShiftKey { get; set; }
        public bool AltKey { get; set; }
    }
}
