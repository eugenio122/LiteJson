using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 1. PAYLOADS PRINCIPAIS
    // ==========================================
    /// <summary>
    /// Representa uma ação invisível/passiva (Ex: um clique no meio do caminho ou uma digitação)
    /// </summary>
    public class MicroStepPayload
    {
        public string StepId { get; set; }
        public string ActionType { get; set; }
        public string TriggerEngine { get; set; }
        public string Timestamp { get; set; }

        // Metadados específicos do momento da ação física
        public ActionMeta ActionMeta { get; set; } = new ActionMeta();

        public CapturedData CapturedData { get; set; }
    }
}
