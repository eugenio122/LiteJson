using System.Collections.Generic;

namespace LiteJson.Models
{
    // ==========================================
    // 1. PAYLOADS PRINCIPAIS
    // ==========================================
    public class ExtractionPayload
    {
        public string StepId { get; set; } = Guid.NewGuid().ToString();
        public int? StepIndex { get; set; }
        public string StepName { get; set; } = string.Empty;
        public bool IsEvidenceOnly { get; set; }
        public bool IsActive { get; set; } = true;
        public bool PendingConfirmation { get; set; }

        /// <summary>
        /// Flag vital para o LiteAutomation. 
        /// Indica se o UIA/AX_Tree já terminou de ser processado em background.
        /// </summary>
        public bool IsHydrated { get; set; } = false;

        public string TriggerType { get; set; } = string.Empty;
        public string ContextImage { get; set; } = string.Empty;

        public CapturedData CapturedData { get; set; } = new CapturedData();
        public ObservedContext ObservedContext { get; set; } = new ObservedContext();

        public List<MicroStepPayload> MicroSteps { get; set; } = new List<MicroStepPayload>();
    }
}