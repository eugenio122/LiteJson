using System;
using System.Collections.Generic;
using System.Text;

namespace LiteJson.Models
{
    // ==========================================
    // 4. A GAVETA SIMÉTRICA (CAPTURED DATA)
    // ==========================================

    public class EngineNode<T>
    {
        public T ElementData { get; set; }
        public List<string> QualityFlags { get; set; } = new List<string>();
    }
}
