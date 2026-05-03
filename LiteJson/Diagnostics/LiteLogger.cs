using System;
using System.IO;
using System.Text;

namespace LiteJson.Diagnostics
{
    /// <summary>
    /// Sistema de Log Local ultra-leve e à prova de falhas de permissão.
    /// Grava na pasta %LocalAppData% para contornar bloqueios de TI corporativa.
    /// </summary>
    public static class LiteLogger
    {
        private static readonly object _lock = new object();
        private static string _logDirectory;
        private static string _logFilePath;

        static LiteLogger()
        {
            try
            {
                // Usa a pasta raiz do projeto/executável para manter os logs portáteis
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _logDirectory = Path.Combine(baseDir, "logs", "litejson");

                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                // Cria um arquivo de log por dia
                _logFilePath = Path.Combine(_logDirectory, $"LiteJson_{DateTime.Now:yyyyMMdd}.log");
            }
            catch
            {
                // Se até a criação da pasta falhar (muito raro), silencia para não quebrar a DLL
            }
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Debug(string message)
        {
            // Ideal para rastrear os "erros silenciosos" (ex: "Iniciando extração UIA...", "Nenhum nó BiDi retornado")
            WriteLog("DEBUG", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(message);
            if (ex != null)
            {
                sb.AppendLine($"Exception: {ex.GetType().Name}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"StackTrace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    sb.AppendLine($"Inner Exception: {ex.InnerException.Message}");
                }
            }

            WriteLog("ERROR", sb.ToString());
        }

        private static void WriteLog(string level, string message)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;

            lock (_lock)
            {
                try
                {
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";

                    // FileShare.ReadWrite permite que o log seja lido ao vivo por outros programas (Notepad++)
                    using (FileStream fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.Write(logEntry);
                    }
                }
                catch
                {
                    // Falha catastrófica ao escrever o log. Ignoramos para não travar a aplicação.
                }
            }
        }
    }
}