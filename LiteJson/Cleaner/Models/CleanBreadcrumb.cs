using System.Collections.Generic;
using System.Text.Json.Serialization;
using LiteJson.Models;

namespace LiteJson.Cleaner.Models
{
    /// <summary>
    /// Versão enxuta de um InteractionBreadcrumb.
    /// Mantém todos os campos físicos da interação e a gaveta WebDriver_BiDi
    /// (com seus seletores e scores, incluindo os negativos que sinalizam
    /// possível duplicidade). Remove apenas as gavetas UIA e AX_Tree.
    /// </summary>
    public class CleanBreadcrumb
    {
        [JsonPropertyName("associatedStepId")]
        public string AssociatedStepId { get; set; } = string.Empty;

        [JsonPropertyName("interactionType")]
        public string InteractionType { get; set; } = "click";

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; }

        [JsonPropertyName("tagName")]
        public string TagName { get; set; }

        [JsonPropertyName("elementId")]
        public string ElementId { get; set; }

        [JsonPropertyName("classes")]
        public string Classes { get; set; }

        [JsonPropertyName("inputType")]
        public string InputType { get; set; }

        [JsonPropertyName("visibleText")]
        public string VisibleText { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }

        [JsonPropertyName("boundingBox")]
        public string BoundingBox { get; set; }

        [JsonPropertyName("scrollX")]
        public int ScrollX { get; set; }

        [JsonPropertyName("scrollY")]
        public int ScrollY { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// Cadeia de hovers necessária para o elemento ficar acessível.
        /// Mantida no clean pois é relevante para a IA reconstruir a navegação.
        /// Cada entrada também tem apenas a gaveta BiDi (UIA/AX_Tree removidos).
        /// </summary>
        [JsonPropertyName("hoverChain")]
        public List<CleanTarget> HoverChain { get; set; } = new List<CleanTarget>();

        [JsonPropertyName("WebDriver_BiDi")]
        public EngineNode<BiDiElementData> WebDriver_BiDi { get; set; }
    }
}