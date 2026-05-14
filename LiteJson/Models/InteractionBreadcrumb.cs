using System;
using System.Text.Json.Serialization;

namespace LiteJson.Models
{
    public class InteractionBreadcrumb
    {
        // --- DADOS DA INTERAÇÃO FÍSICA ---
        public string InteractionType { get; set; } = "click";
        public string Timestamp { get; set; }

        // --- DADOS BÁSICOS DO ELEMENTO ALVO ---
        public string TagName { get; set; }
        public string ElementId { get; set; }
        public string Classes { get; set; }
        public string InputType { get; set; }
        public string VisibleText { get; set; }
        public string Value { get; set; }
        public string BoundingBox { get; set; } // Formato "Left,Top,Width,Height"
        public int ScrollX { get; set; }
        public int ScrollY { get; set; }

        /// <summary>
        /// A URL completa da página no momento da interação (incluindo parâmetros de query).
        /// Fundamental para reconstrução de estados e comparadores do LiteAutomation.
        /// </summary>
        public string Url { get; set; }

        // ====================================================================
        // AS TRÊS GAVETAS DA ARQUITETURA PROFUNDA (A TRINDADE SIMÉTRICA)
        // Travadas com JsonPropertyName para ignorar o CamelCase do Serializador!
        // ====================================================================

        /// <summary>
        /// A Linha de Frente (Navegador): Preenchida instantaneamente pelo JavaScript.
        /// </summary>
        [JsonPropertyName("WebDriver_BiDi")]
        public EngineNode<BiDiElementData> WebDriver_BiDi { get; set; }

        /// <summary>
        /// A Retaguarda (Windows): Preenchida assincronamente pelo C# na fila de hidratação.
        /// </summary>
        [JsonPropertyName("UIA")]
        public EngineNode<UiaElementData> UIA { get; set; }

        /// <summary>
        /// A Retaguarda (Acessibilidade Nativa): Preenchida assincronamente via CDP pelo C#.
        /// </summary>
        [JsonPropertyName("AX_Tree")]
        public EngineNode<AxTreeElementData> AX_Tree { get; set; }

        // ====================================================================

        // Propriedade auxiliar para facilitar o cálculo do centro no worker C#
        [JsonIgnore]
        public (int X, int Y) CenterCoordinates
        {
            get
            {
                if (string.IsNullOrWhiteSpace(BoundingBox)) return (0, 0);
                var parts = BoundingBox.Split(',');
                if (parts.Length == 4 &&
                    int.TryParse(parts[0], out int left) &&
                    int.TryParse(parts[1], out int top) &&
                    int.TryParse(parts[2], out int width) &&
                    int.TryParse(parts[3], out int height))
                {
                    return (left + (width / 2), top + (height / 2));
                }
                return (0, 0);
            }
        }
    }
}