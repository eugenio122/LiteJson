using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 4. NÓS DE DADOS ESPECÍFICOS (ELEMENT DATA)
    // ==========================================
    public class AxTreeElementData
    {
        public SemanticNode Semantic { get; set; } = new SemanticNode();
        public string BoundingRectangle { get; set; } = string.Empty;
    }
}
