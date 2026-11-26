using System;

namespace AudioSwitch.Platform
{
    /// <summary>Minimal logging abstraction so every module can report problems consistently.</summary>
    public interface ILogger
    {
        string LogFilePath { get; }

        void Info(string message);

        void Error(string message, Exception exception = null);
    }
}
