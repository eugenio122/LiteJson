using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiteJson.Cleaner.Models;
using LiteJson.Models;
using LiteJson.Diagnostics;

namespace LiteJson.Cleaner
{
    /// <summary>
    /// Serviço offline e desacoplado que transforma o Scenario_Data.json completo
    /// em uma versão "clean" para consumo por IA.
    ///
    /// Mantém: StepId, StepIndex, TriggerType, InteractionTrail (campos físicos +
    /// gaveta WebDriver_BiDi, incluindo seletores com score negativo).
    /// Remove: ObservedContext, TargetElementData, CapturedData central, e as
    /// gavetas UIA e AX_Tree de cada breadcrumb.
    ///
    /// Não toca no pipeline de captura/hidratação/oráculo — é uma operação
    /// puramente de arquivo (ler de um caminho, escrever em outro).
    /// </summary>
    public class JsonCleanerService
    {
        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// Lê o JSON completo do caminho de entrada, transforma na versão clean
        /// e grava no caminho de saída. Retorna a quantidade de steps processados.
        /// </summary>
        public int CleanFile(string inputPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
                throw new FileNotFoundException("Arquivo de entrada não encontrado.", inputPath);

            string rawJson = File.ReadAllText(inputPath);

            var fullSteps = JsonSerializer.Deserialize<List<ExtractionPayload>>(rawJson, ReadOptions)
                            ?? new List<ExtractionPayload>();

            var cleanSteps = fullSteps.Select(MapStep).ToList();

            string cleanJson = JsonSerializer.Serialize(cleanSteps, WriteOptions);
            File.WriteAllText(outputPath, cleanJson);

            LiteLogger.Info($"[JsonCleaner] Limpeza concluída: {cleanSteps.Count} steps. Saída: {outputPath}");
            return cleanSteps.Count;
        }

        private CleanStep MapStep(ExtractionPayload payload)
        {
            return new CleanStep
            {
                StepId = payload.StepId,
                StepIndex = payload.StepIndex,
                TriggerType = payload.TriggerType,
                InteractionTrail = (payload.InteractionTrail ?? new List<InteractionBreadcrumb>())
                    .Select(MapBreadcrumb)
                    .ToList()
            };
        }

        private CleanBreadcrumb MapBreadcrumb(InteractionBreadcrumb crumb)
        {
            return new CleanBreadcrumb
            {
                AssociatedStepId = crumb.AssociatedStepId,
                InteractionType = crumb.InteractionType,
                Timestamp = crumb.Timestamp,
                TagName = crumb.TagName,
                ElementId = crumb.ElementId,
                Classes = crumb.Classes,
                InputType = crumb.InputType,
                VisibleText = crumb.VisibleText,
                Value = crumb.Value,
                BoundingBox = crumb.BoundingBox,
                ScrollX = crumb.ScrollX,
                ScrollY = crumb.ScrollY,
                Url = crumb.Url,
                WebDriver_BiDi = crumb.WebDriver_BiDi,
                HoverChain = (crumb.HoverChain ?? new List<TargetElementData>())
                    .Select(MapTarget)
                    .ToList()
            };
        }

        private CleanTarget MapTarget(TargetElementData target)
        {
            return new CleanTarget
            {
                AssociatedStepId = target.AssociatedStepId,
                WebDriver_BiDi = target.WebDriver_BiDi
            };
        }
    }
}