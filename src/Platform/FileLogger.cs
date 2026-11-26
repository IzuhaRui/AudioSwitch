using System;
using System.IO;
using System.Text;

namespace AudioSwitch.Platform
{
    /// <summary>Appends log lines to %LOCALAPPDATA%\AudioSwitch\AudioSwitch.log.</summary>
    public sealed class FileLogger : ILogger
    {
        private const string LogFileName = "AudioSwitch.log";
        private static readonly object SyncRoot = new object();
        private readonly string _logFilePath;

        public FileLogger()
        {
            string overridePath = Environment.GetEnvironmentVariable("AUDIOSWITCH_LOG_FILE");
            if (!string.IsNullOrEmpty(overridePath))
            {
                _logFilePath = overridePath;
            }
            else
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioSwitch");
                _logFilePath = Path.Combine(directory, LogFileName);
            }
        }

        public string LogFilePath
        {
            get { return _logFilePath; }
        }

        public void Info(string message)
        {
            Write("信息", message);
        }

        public void Error(string message, Exception exception = null)
        {
            string text = message ?? string.Empty;
            if (exception != null)
            {
                text += Environment.NewLine + exception;
            }

            Write("错误", text);
        }

        private void Write(string level, string message)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}{3}",
                DateTime.Now, level, message, Environment.NewLine);

            lock (SyncRoot)
            {
                if (TryWrite(_logFilePath, line))
                {
                    return;
                }

                // Fall back to the temp directory when the profile directory is not writable
                // (for example when the process runs inside a restricted sandbox).
                try
                {
                    string fallback = Path.Combine(Path.GetTempPath(), LogFileName);
                    TryWrite(fallback, line);
                }
                catch
                {
                    // Logging must never throw.
                }
            }
        }

        private static bool TryWrite(string path, string line)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(path, line, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
