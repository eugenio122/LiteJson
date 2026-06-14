using System.Text.Json.Serialization;
using LiteJson.Models;

namespace LiteJson.Cleaner.Models
{
    /// <summary>
    /// Versão enxuta de uma entrada do HoverChain (originalmente TargetElementData).
    /// Mantém o AssociatedStepId e a gaveta WebDriver_BiDi; remove UIA e AX_Tree.
    /// </summary>
    public class CleanTarget
    {
        [JsonPropertyName("associatedStepId")]
        public string AssociatedStepId { get; set; } = string.Empty;

        [JsonPropertyName("WebDriver_BiDi")]
        public EngineNode<BiDiElementData> WebDriver_BiDi { get; set; }
    }
}