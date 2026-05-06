using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace LiteJson.Models
{
    // ==========================================
    // 4. A GAVETA SIMÉTRICA (CAPTURED DATA)
    // ==========================================

    /// <summary>
    /// O Coração da Inovação: A gaveta tripla simétrica que abriga as verdades de todos os motores.
    /// </summary>
    public class CapturedData
    {
        [JsonPropertyName("UIA")]
        public EngineNode<UiaElementData> UIA { get; set; } = new EngineNode<UiaElementData> { ElementData = new UiaElementData() };

        [JsonPropertyName("AX_Tree")]
        public EngineNode<AxTreeElementData> AX_Tree { get; set; } = new EngineNode<AxTreeElementData> { ElementData = new AxTreeElementData() };

        [JsonPropertyName("WebDriver_BiDi")]
        public EngineNode<BiDiElementData> WebDriver_BiDi { get; set; } = new EngineNode<BiDiElementData> { ElementData = new BiDiElementData() };
    }
}
