using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LiteJson.Models
{
    /// <summary>
    /// Representa uma interação física do usuário (clique, foco, digitação).
    /// Herda <see cref="CapturedData"/> para carregar a gaveta tripla simétrica
    /// (UIA + AX_Tree + BiDi), garantindo consistência com o restante da arquitetura
    /// e permitindo que o Oráculo de Ambiguidade use uma assinatura única.
    /// </summary>
    public class InteractionBreadcrumb : CapturedData
    {
        // --- IDENTIDADE E RASTREABILIDADE ---

        /// <summary>
        /// Chave estrangeira que conecta este clique ao seu <see cref="ExtractionPayload"/>.
        /// Sempre igual ao <c>StepId</c> do payload que o contém.
        /// Injetado pelo orquestrador no momento do print, quando a trilha é drenada.
        /// </summary>
        public string AssociatedStepId { get; set; } = string.Empty;

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

        /// <summary>
        /// Cadeia de elementos (menus/dropdowns) que precisavam estar abertos por hover
        /// para este elemento ficar acessível no momento do click. Lista ordenada do
        /// mais externo para o mais interno — o LiteAutomation deve reproduzir o hover
        /// em cada um, nessa ordem, ANTES de executar a interação principal.
        ///
        /// Cada entrada é um TargetElementData completo (com as três gavetas da Trindade
        /// e todos os seletores), porém com XPaths anulados e score zerado, pois
        /// elementos hover têm posição estrutural instável.
        ///
        /// Preenchido a partir do cache de TargetElementData dos prints anteriores,
        /// consumido APENAS em breadcrumbs de click (focus/input não consomem, para
        /// evitar ruído). O cache é limpo após cada click.
        /// </summary>
        public List<TargetElementData> HoverChain { get; set; } = new List<TargetElementData>();

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