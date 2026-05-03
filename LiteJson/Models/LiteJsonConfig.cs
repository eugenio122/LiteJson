using System;
using System.IO;
using System.Text.Json;

namespace LiteJson.Models
{
    public enum TargetEngine { WebUniversal, SapEnterprise }

    public class LiteJsonConfig
    {
        public bool IsEnabled { get; set; } = true;
        public TargetEngine Target { get; set; } = TargetEngine.WebUniversal;
        public string CustomOutputPath { get; set; } = string.Empty;

        /// <summary>
        /// Define quantos snapshots podem aguardar na fila para hidratação UIA.
        /// </summary>
        public int MaxHydrationQueueSize { get; set; } = 5;

        private static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LiteJsonConfig.json");

        public static LiteJsonConfig Load()
        {
            if (File.Exists(ConfigPath))
            {
                try { return JsonSerializer.Deserialize<LiteJsonConfig>(File.ReadAllText(ConfigPath)) ?? new LiteJsonConfig(); }
                catch { return new LiteJsonConfig(); }
            }
            return new LiteJsonConfig();
        }

        public void Save()
        {
            try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }
    }
}