using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteJson.Models
{
    // ==========================================
    // 4. A GAVETA SIMÉTRICA (CAPTURED DATA)
    // ==========================================

    /// <summary>
    /// Representa o elemento central do snapshot: o alvo direto da ação do usuário.
    /// Herda a gaveta tripla simétrica (UIA + AX_Tree + BiDi) de <see cref="CapturedData"/>
    /// e adiciona a chave estrangeira <see cref="AssociatedStepId"/> para rastreabilidade
    /// com o <see cref="ExtractionPayload"/> pai.
    /// </summary>
    public class TargetElementData : CapturedData
    {
        /// <summary>
        /// Chave estrangeira que conecta este alvo ao seu <see cref="ExtractionPayload"/>.
        /// Sempre igual ao <c>StepId</c> do payload que o contém.
        /// Injetado pelo orquestrador no momento do print.
        /// </summary>
        [JsonPropertyName("associatedStepId")]
        public string AssociatedStepId { get; set; } = string.Empty;

        /// <summary>
        /// Cria uma cópia profunda deste TargetElementData destinada ao HoverChain.
        ///
        /// Elementos do HoverChain (menus, dropdowns) aparecem e somem conforme a
        /// interação do usuário, então seus XPaths são estruturalmente instáveis —
        /// o índice de um nó pode mudar quando o menu abre/fecha, ou um elemento
        /// pode ocupar a posição de outro. Por isso os XPaths são anulados e o
        /// score zerado (0 = instável, não confundir com -N que seria duplicata).
        ///
        /// Todos os demais seletores (data-testid, id, aria-label, text, css, etc.)
        /// são preservados, pois sobrevivem entre os estados aberto/fechado.
        /// </summary>
        public TargetElementData CloneForHoverChain()
        {
            // Deep clone via serialização: garante cópia independente de toda a
            // árvore (gavetas, SelectorSet, LocatorData) sem risco de referência
            // compartilhada que contaminaria o original.
            var json = JsonSerializer.Serialize(this);
            var clone = JsonSerializer.Deserialize<TargetElementData>(json) ?? new TargetElementData();

            // Anula os XPaths do BiDi — instáveis para elementos hover.
            var ss = clone.WebDriver_BiDi?.ElementData?.SelectorSet;
            if (ss != null)
            {
                if (ss.XpathAbsolute != null)
                {
                    ss.XpathAbsolute.Value = null;
                    ss.XpathAbsolute.Confidence = 0;
                }
                if (ss.XpathRelative != null)
                {
                    ss.XpathRelative.Value = null;
                    ss.XpathRelative.Confidence = 0;
                }
            }

            return clone;
        }
    }
}