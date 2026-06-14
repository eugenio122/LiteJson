using System;
using System.Collections.Generic;
using System.Linq;
using LiteJson.Models;
using LiteJson.Diagnostics;

namespace LiteJson.Core
{
    public partial class LiteJsonManager
    {
        // =========================================================================
        // ORÁCULO DE AMBIGUIDADE (V2.1)
        // Roda em background após a hidratação completa.
        //
        // HIERARQUIA DE AUTORIDADE:
        // O ObservedContext é a FONTE DE VERDADE primária — ele é a fotografia
        // completa da tela e conta as duplicatas corretamente. É avaliado PRIMEIRO.
        //
        // O TargetElementData e cada InteractionBreadcrumb são "observadores" do
        // mesmo elemento físico. Eles NÃO recontam duplicatas (a Trindade olhando
        // o mesmo elemento não é duplicata). Em vez disso:
        //   - Procuram seu XpathAbsoluto no ObservedContext JÁ AVALIADO do mesmo step.
        //   - ACHOU  → replicam todos os scores/flags daquele elemento.
        //   - NÃO ACHOU → calculam por si mesmos (o XPath é único, então o elemento
        //                 não estava na fatia visível; ex.: interação sem print).
        //
        // FONTES DE CONTAGEM (para quem precisa calcular):
        //   1. Seletores FORMATADOS (data-testid, id, css, xpath, text, aria-label,
        //      placeholder, alt, title, name) → Full DOM do navegador (bidiMap).
        //   2. Seletores SEMÂNTICOS localizados (role/accessibleName/helpText do UIA
        //      e AX_Tree) → mapas construídos da própria Trindade (idioma real).
        // =========================================================================

        /// <summary>
        /// Ponto de entrada do Oráculo. Chamado uma vez por step após a hidratação.
        /// </summary>
        public void RunOracle(
            TargetElementData target,
            List<InteractionBreadcrumb> trail,
            ObservedContext context,
            string stepId)
        {
            if (target == null && (trail == null || trail.Count == 0) && (context == null || context.VisibleElements.Count == 0))
                return;

            try
            {
                // --- PASSO 1: EXTRAIR O MAPA DE SELETORES FORMATADOS (Full DOM, efêmero) ---
                var bidiMap = _bidiAdapter.ExtractFullDomFrequencyMap() ?? new Dictionary<string, int>();
                LiteLogger.Debug($"[Oráculo] StepId={stepId}: Full DOM (formatados) mapeado. {bidiMap.Count} seletores BiDi únicos.");

                // --- PASSO 2: COLETAR AS INSTÂNCIAS DO OBSERVED CONTEXT (fonte de verdade) ---
                var contextInstances = new List<CapturedData>();
                if (context != null)
                    foreach (var el in context.VisibleElements)
                        if (el?.CapturedData != null)
                            contextInstances.Add(el.CapturedData);

                // --- PASSO 3: CONSTRUIR OS MAPAS SEMÂNTICOS LOCALIZADOS ---
                // Contados a partir de elementos físicos únicos. Como o ObservedContext
                // é a fatia visível completa, ele é a base correta para essa contagem.
                // Agrupa por XpathAbsoluto para não contar o mesmo elemento 2x.
                var semanticUiaMap = new Dictionary<string, int>(StringComparer.Ordinal);
                var semanticAxMap = new Dictionary<string, int>(StringComparer.Ordinal);

                var seenForSemantic = new HashSet<string>(StringComparer.Ordinal);
                foreach (var inst in contextInstances)
                {
                    string xpath = inst.WebDriver_BiDi?.ElementData?.SelectorSet?.XpathAbsolute?.Value;
                    string dedupKey = string.IsNullOrWhiteSpace(xpath) ? Guid.NewGuid().ToString() : xpath.Trim();
                    if (!seenForSemantic.Add(dedupKey)) continue; // já contado

                    var uiaSem = inst.UIA?.ElementData?.Semantic;
                    if (uiaSem != null)
                    {
                        BumpSemantic(semanticUiaMap, uiaSem.AccessibleName?.Value);
                        BumpSemantic(semanticUiaMap, uiaSem.Role?.Value);
                        BumpSemantic(semanticUiaMap, uiaSem.HelpText?.Value);
                    }

                    var axSem = inst.AX_Tree?.ElementData?.Semantic;
                    if (axSem != null)
                    {
                        BumpSemantic(semanticAxMap, axSem.AccessibleName?.Value);
                        BumpSemantic(semanticAxMap, axSem.Role?.Value);
                        BumpSemantic(semanticAxMap, axSem.HelpText?.Value);
                    }
                }

                LiteLogger.Debug($"[Oráculo] StepId={stepId}: Semânticos contados. UIA={semanticUiaMap.Count}, AX={semanticAxMap.Count} valores distintos.");

                // --- PASSO 4: AVALIAR O OBSERVED CONTEXT (fonte de verdade) ---
                // Cada elemento é avaliado individualmente; um mapa de resultados é
                // construído por XpathAbsoluto para replicação posterior.
                var verdictByXpath = new Dictionary<string, CapturedData>(StringComparer.Ordinal);

                foreach (var inst in contextInstances)
                {
                    EvaluateInstance(inst, bidiMap, semanticAxMap, semanticUiaMap, stepId);

                    string xpath = inst.WebDriver_BiDi?.ElementData?.SelectorSet?.XpathAbsolute?.Value;
                    if (!string.IsNullOrWhiteSpace(xpath))
                        verdictByXpath[xpath.Trim()] = inst; // guarda como referência para replicação
                }

                // --- PASSO 5: TARGET E TRAIL — REPLICAM DO CONTEXT OU CALCULAM ---
                if (target != null)
                    EvaluateObserverInstance(target, verdictByXpath, bidiMap, semanticAxMap, semanticUiaMap, stepId, "Target");

                if (trail != null)
                    foreach (var crumb in trail)
                        EvaluateObserverInstance(crumb, verdictByXpath, bidiMap, semanticAxMap, semanticUiaMap, stepId, "Breadcrumb");

                LiteLogger.Debug($"[Oráculo] StepId={stepId}: Avaliação concluída.");
            }
            catch (Exception ex)
            {
                LiteLogger.Error($"[Oráculo] Erro crítico no RunOracle. StepId={stepId}.", ex);
            }
        }

        /// <summary>
        /// Avalia uma instância "observadora" (Target ou Breadcrumb).
        /// Se o XpathAbsoluto dela existe no ObservedContext já avaliado, REPLICA o
        /// veredito (não reconta). Senão, CALCULA por si mesma.
        /// </summary>
        private void EvaluateObserverInstance(
            CapturedData observer,
            Dictionary<string, CapturedData> verdictByXpath,
            Dictionary<string, int> bidiMap,
            Dictionary<string, int> semanticAxMap,
            Dictionary<string, int> semanticUiaMap,
            string stepId,
            string sourceLabel)
        {
            if (observer == null) return;

            string xpath = observer.WebDriver_BiDi?.ElementData?.SelectorSet?.XpathAbsolute?.Value;

            if (!string.IsNullOrWhiteSpace(xpath) && verdictByXpath.TryGetValue(xpath.Trim(), out var contextMatch))
            {
                // MESMO ELEMENTO já avaliado no ObservedContext → replica tudo.
                ReplicateVerdict(contextMatch, observer);
                LiteLogger.Debug($"[Oráculo] {sourceLabel} StepId={stepId}: XPath encontrado no Context. Veredito replicado.");
            }
            else
            {
                // Elemento não está na fatia visível (ex.: interação sem print) → calcula.
                EvaluateInstance(observer, bidiMap, semanticAxMap, semanticUiaMap, stepId);
                LiteLogger.Debug($"[Oráculo] {sourceLabel} StepId={stepId}: XPath ausente no Context. Calculado isoladamente.");
            }
        }

        /// <summary>
        /// Replica os scores de cada LocatorData e as QualityFlags do Oráculo de uma
        /// instância-fonte (do ObservedContext) para uma instância-alvo (observer) que
        /// representa o mesmo elemento físico.
        /// </summary>
        private void ReplicateVerdict(CapturedData source, CapturedData target)
        {
            // --- BiDi ---
            var srcSs = source.WebDriver_BiDi?.ElementData?.SelectorSet;
            var tgtSs = target.WebDriver_BiDi?.ElementData?.SelectorSet;
            if (srcSs != null && tgtSs != null)
            {
                CopyScore(srcSs.CustomAttribute, tgtSs.CustomAttribute);
                CopyScore(srcSs.Id, tgtSs.Id);
                CopyScore(srcSs.AriaLabel, tgtSs.AriaLabel);
                CopyScore(srcSs.Name, tgtSs.Name);
                CopyScore(srcSs.Placeholder, tgtSs.Placeholder);
                CopyScore(srcSs.Alt, tgtSs.Alt);
                CopyScore(srcSs.Text, tgtSs.Text);
                CopyScore(srcSs.Title, tgtSs.Title);
                CopyScore(srcSs.Css, tgtSs.Css);
                CopyScore(srcSs.XpathRelative, tgtSs.XpathRelative);
                CopyScore(srcSs.XpathAbsolute, tgtSs.XpathAbsolute);
            }

            // --- AX_Tree ---
            var srcAx = source.AX_Tree?.ElementData?.Semantic;
            var tgtAx = target.AX_Tree?.ElementData?.Semantic;
            if (srcAx != null && tgtAx != null)
            {
                CopyScore(srcAx.AccessibleName, tgtAx.AccessibleName);
                CopyScore(srcAx.Role, tgtAx.Role);
                CopyScore(srcAx.HelpText, tgtAx.HelpText);
            }

            // --- UIA ---
            var srcUia = source.UIA?.ElementData?.Semantic;
            var tgtUia = target.UIA?.ElementData?.Semantic;
            if (srcUia != null && tgtUia != null)
            {
                CopyScore(srcUia.AutomationId, tgtUia.AutomationId);
                CopyScore(srcUia.AccessibleName, tgtUia.AccessibleName);
                CopyScore(srcUia.Role, tgtUia.Role);
                CopyScore(srcUia.HelpText, tgtUia.HelpText);
            }

            // --- Replica as flags do Oráculo nas QualityFlags ---
            ReplicateOracleFlags(source.WebDriver_BiDi, target.WebDriver_BiDi);
            ReplicateOracleFlags(source.AX_Tree, target.AX_Tree);
            ReplicateOracleFlags(source.UIA, target.UIA);
        }

        /// <summary>
        /// Copia o Confidence (sinal incluso) do locator-fonte para o locator-alvo,
        /// quando ambos existem e têm o mesmo valor.
        /// </summary>
        private void CopyScore(LocatorData source, LocatorData target)
        {
            if (source == null || target == null) return;
            if (string.IsNullOrWhiteSpace(source.Value) || string.IsNullOrWhiteSpace(target.Value)) return;
            if (source.Value.Trim() != target.Value.Trim()) return;
            target.Confidence = source.Confidence;
        }

        /// <summary>
        /// Copia as flags do Oráculo (ORACLE_VERIFIED, AMBIGUOUS_ELEMENT,
        /// ORACLE_NEEDS_HUMAN_REVIEW) de uma gaveta-fonte para a gaveta-alvo.
        /// </summary>
        private void ReplicateOracleFlags<T>(EngineNode<T> source, EngineNode<T> target)
        {
            if (source?.QualityFlags == null || target?.QualityFlags == null) return;

            string[] oracleFlags = { "ORACLE_VERIFIED", "AMBIGUOUS_ELEMENT", "ORACLE_NEEDS_HUMAN_REVIEW" };
            foreach (var flag in oracleFlags)
            {
                if (source.QualityFlags.Contains(flag) && !target.QualityFlags.Contains(flag))
                    target.QualityFlags.Add(flag);
            }
        }

        /// <summary>
        /// Avalia uma única instância contra os mapas (Full DOM + semânticos).
        /// Cada seletor é avaliado individualmente; flags de elemento são injetadas
        /// conforme a contagem agregada de seletores ambíguos.
        /// </summary>
        private void EvaluateInstance(
            CapturedData data,
            Dictionary<string, int> bidiMap,
            Dictionary<string, int> semanticAxMap,
            Dictionary<string, int> semanticUiaMap,
            string stepId)
        {
            if (data == null) return;

            int ambiguousCount = 0;
            int totalCount = 0;

            // --- BiDi: seletores formatados consultam o Full DOM ---
            var ss = data.WebDriver_BiDi?.ElementData?.SelectorSet;
            if (ss != null)
            {
                EvaluateFormatted(ss.CustomAttribute, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.Id, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.AriaLabel, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.Name, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.Placeholder, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.Alt, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.Text, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.Title, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.Css, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.XpathRelative, bidiMap, ref ambiguousCount, ref totalCount);
                EvaluateFormatted(ss.XpathAbsolute, bidiMap, ref ambiguousCount, ref totalCount);
            }

            // --- AX_Tree: seletores semânticos consultam o mapa localizado ---
            var axSem = data.AX_Tree?.ElementData?.Semantic;
            if (axSem != null)
            {
                EvaluateSemantic(axSem.AccessibleName, semanticAxMap, ref ambiguousCount, ref totalCount);
                EvaluateSemantic(axSem.Role, semanticAxMap, ref ambiguousCount, ref totalCount);
                EvaluateSemantic(axSem.HelpText, semanticAxMap, ref ambiguousCount, ref totalCount);
            }

            // --- UIA: seletores semânticos consultam o mapa localizado ---
            var uiaSem = data.UIA?.ElementData?.Semantic;
            if (uiaSem != null)
            {
                EvaluateSemantic(uiaSem.AutomationId, semanticUiaMap, ref ambiguousCount, ref totalCount);
                EvaluateSemantic(uiaSem.AccessibleName, semanticUiaMap, ref ambiguousCount, ref totalCount);
                EvaluateSemantic(uiaSem.Role, semanticUiaMap, ref ambiguousCount, ref totalCount);
                EvaluateSemantic(uiaSem.HelpText, semanticUiaMap, ref ambiguousCount, ref totalCount);
            }

            if (totalCount == 0) return;

            // --- INJETAR FLAGS NO NÍVEL DO ELEMENTO ---
            if (ambiguousCount == 0)
            {
                InjectFlag(data, "ORACLE_VERIFIED");
            }
            else if (ambiguousCount < totalCount)
            {
                InjectFlag(data, "AMBIGUOUS_ELEMENT");
            }
            else
            {
                InjectFlag(data, "AMBIGUOUS_ELEMENT");
                InjectFlag(data, "ORACLE_NEEDS_HUMAN_REVIEW");
            }
        }

        /// <summary>
        /// Avalia um seletor FORMATADO contra o Full DOM. Ambíguo se aparece 2+ vezes.
        /// Aplica o score (positivo ou negativo) diretamente no LocatorData.
        /// </summary>
        private void EvaluateFormatted(LocatorData locator, Dictionary<string, int> freqMap, ref int ambiguousCount, ref int totalCount)
        {
            if (locator == null || string.IsNullOrWhiteSpace(locator.Value)) return;
            if (freqMap == null) return;

            totalCount++;
            string value = locator.Value.Trim();

            if (!freqMap.TryGetValue(value, out int domCount)) domCount = 0;

            bool isAmbiguous = domCount > 1;
            if (isAmbiguous) ambiguousCount++;

            locator.Confidence = isAmbiguous ? -Math.Abs(locator.Confidence) : Math.Abs(locator.Confidence);
        }

        /// <summary>
        /// Avalia um seletor SEMÂNTICO localizado contra o mapa da Trindade.
        /// Ambíguo se o valor aparece em 2+ elementos físicos distintos.
        /// </summary>
        private void EvaluateSemantic(LocatorData locator, Dictionary<string, int> semanticMap, ref int ambiguousCount, ref int totalCount)
        {
            if (locator == null || string.IsNullOrWhiteSpace(locator.Value)) return;
            if (semanticMap == null) return;

            totalCount++;
            string value = locator.Value.Trim();

            if (!semanticMap.TryGetValue(value, out int sliceCount)) sliceCount = 0;

            bool isAmbiguous = sliceCount > 1;
            if (isAmbiguous) ambiguousCount++;

            locator.Confidence = isAmbiguous ? -Math.Abs(locator.Confidence) : Math.Abs(locator.Confidence);
        }

        /// <summary>
        /// Incrementa a frequência de um valor semântico no mapa, se válido.
        /// </summary>
        private void BumpSemantic(Dictionary<string, int> map, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string v = value.Trim();
            map[v] = map.TryGetValue(v, out int c) ? c + 1 : 1;
        }

        /// <summary>
        /// Injeta uma QualityFlag nos três motores da Trindade que têm dados válidos.
        /// </summary>
        private void InjectFlag(CapturedData data, string flag)
        {
            if (data.WebDriver_BiDi?.QualityFlags != null && !data.WebDriver_BiDi.QualityFlags.Contains(flag))
                data.WebDriver_BiDi.QualityFlags.Add(flag);

            if (data.AX_Tree?.QualityFlags != null && !data.AX_Tree.QualityFlags.Contains(flag))
                data.AX_Tree.QualityFlags.Add(flag);

            if (data.UIA?.QualityFlags != null && !data.UIA.QualityFlags.Contains(flag))
                data.UIA.QualityFlags.Add(flag);
        }
    }
}