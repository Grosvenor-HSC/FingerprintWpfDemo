using System;
using System.Diagnostics;
using System.IO;

namespace FingerprintWpfDemo
{
    /// <summary>
    /// Simple client-side logger. Writes to a log file under LocalApplicationData
    /// and also to the Visual Studio debug output window.
    /// </summary>
    public static class ClientLogger
    {
        private static readonly object _lock = new object();

        private static readonly string LogFilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FingerprintWpfDemo",
                "client.log");

        public static void Log(string message, Exception ex = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
                if (ex != null)
                {
                    line += Environment.NewLine + ex;
                }

                lock (_lock)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }

                Debug.WriteLine(line);
            }
            catch
            {
                // Don't ever throw from logging.
            }
        }
    }
}
