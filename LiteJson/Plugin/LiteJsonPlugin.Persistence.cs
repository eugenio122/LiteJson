using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiteJson.Models;
using LiteJson.Diagnostics;

namespace LiteJson.Plugin
{
    public partial class LiteJsonPlugin
    {
        // =========================================================================
        // PERSISTÊNCIA: Gerenciamento do nome do cenário ativo
        // =========================================================================
        private void UpdateScenarioName()
        {
            string name = _hostContext.GetSessionMetadata("CurrentFileName") as string ?? "Cenario_Global";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (_lastScenarioName != name) { _scenarioSteps.Clear(); _lastScenarioName = name; }
        }

        // =========================================================================
        // PERSISTÊNCIA: Diretório de saída do cenário atual
        // =========================================================================
        private string GetCurrentScenarioDirectory()
        {
            var cfg = LiteJsonConfig.Load();
            string root = !string.IsNullOrWhiteSpace(cfg.CustomOutputPath)
                ? cfg.CustomOutputPath
                : _baseOutputDirectory;
            string folder = Path.Combine(root, _lastScenarioName ?? "Cenario_Global");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return folder;
        }

        // =========================================================================
        // PERSISTÊNCIA: Gravação atômica do JSON (tmp -> rename)
        // Usa arquivo .lock para sinalizar hidratação em andamento ao LiteAutomation.
        // =========================================================================
        private void SaveJsonToDisk(string dir)
        {
            try
            {
                string lflow = _hostContext.GetSessionMetadata("CurrentLFlowPath") as string;
                string name = !string.IsNullOrEmpty(lflow)
                    ? Path.GetFileNameWithoutExtension(lflow) + ".json"
                    : "Scenario_Data.json";
                string jsonPath = Path.Combine(dir, name);
                string lockPath = jsonPath + ".lock";

                if (!_hydrationQueue.IsEmpty) File.WriteAllText(lockPath, "HIDRATANDO");

                var opt = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    ReferenceHandler = ReferenceHandler.IgnoreCycles,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string tmp = jsonPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_scenarioSteps, opt));
                if (File.Exists(jsonPath)) File.Delete(jsonPath);
                File.Move(tmp, jsonPath);

                if (_hydrationQueue.IsEmpty && File.Exists(lockPath)) File.Delete(lockPath);
            }
            catch (Exception ex)
            {
                LiteLogger.Error("Falha crítica ao gravar Scenario_Data.json", ex);
            }
        }

        // =========================================================================
        // PERSISTÊNCIA: Recalcula os índices visíveis dos passos ativos
        // =========================================================================
        private void RecalculateIndices()
        {
            int c = 1;
            foreach (var s in _scenarioSteps)
            {
                if (!s.IsActive || s.IsEvidenceOnly || s.PendingConfirmation)
                    s.StepIndex = null;
                else
                    s.StepIndex = c++;
            }
        }
    }
}