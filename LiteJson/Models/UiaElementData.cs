using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    /// ==========================================
    // 5. NÓS DE DADOS ESPECÍFICOS (ENGINE DATA)
    // ==========================================

    public class UiaElementData
    {
        public SemanticNode Semantic { get; set; } = new SemanticNode();
        public string UiaClassName { get; set; } = string.Empty;
        public string BoundingRectangle { get; set; } = string.Empty;
    }
}
