using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LiteJson.Cleaner.Models
{
    // ==========================================
    // MODELOS DO JSON CLEAN (PARA CONSUMO POR IA)
    // ==========================================

    /// <summary>
    /// Versão enxuta de um passo, destinada a consumo por IA (agente ou ferramenta).
    /// Mantém apenas a identidade do passo e a trilha de interações com a gaveta BiDi.
    /// Remove ObservedContext, TargetElementData e as gavetas UIA/AX_Tree, que pesam
    /// o JSON e não são necessárias para o consumo por IA.
    /// </summary>
    public class CleanStep
    {
        [JsonPropertyName("stepId")]
        public string StepId { get; set; } = string.Empty;

        [JsonPropertyName("stepIndex")]
        public int? StepIndex { get; set; }

        [JsonPropertyName("triggerType")]
        public string TriggerType { get; set; } = string.Empty;

        [JsonPropertyName("interactionTrail")]
        public List<CleanBreadcrumb> InteractionTrail { get; set; } = new List<CleanBreadcrumb>();
    }
}