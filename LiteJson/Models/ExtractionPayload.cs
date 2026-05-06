using System.Collections.Generic;

namespace LiteJson.Models
{
    // ==========================================
    // 2. PAYLOAD PRINCIPAL (O ESTADO / SNAPSHOT)
    // ==========================================
    public class ExtractionPayload
    {
        public string StepId { get; set; } = Guid.NewGuid().ToString();
        public int? StepIndex { get; set; }
        public string StepName { get; set; } = string.Empty;

        /// <summary>
        /// Data/Hora exata do Snapshot. Fundamental para o LiteAutomation 
        /// calcular o delta de tempo entre o Estado A e o Estado B.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public bool IsEvidenceOnly { get; set; }
        public bool IsActive { get; set; } = true;
        public bool PendingConfirmation { get; set; }

        /// <summary>
        /// Indica se o UIA/AX_Tree já terminou de ser processado pela Thread STA em background.
        /// </summary>
        public bool IsHydrated { get; set; } = false;

        public string TriggerType { get; set; } = string.Empty;
        public string ContextImage { get; set; } = string.Empty;

        /// <summary>
        /// Dados do elemento central (caso o usuário tenha focado em algo antes de printar).
        /// Pode atuar como um fallback secundário.
        /// </summary>
        public CapturedData CapturedData { get; set; } = new CapturedData();

        /// <summary>
        /// O coração da arquitetura State-Driven: A fotografia completa de todos os elementos da tela.
        /// </summary>
        public ObservedContext ObservedContext { get; set; } = new ObservedContext();
    }
}