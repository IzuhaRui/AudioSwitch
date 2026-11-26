using System;

namespace AudioSwitch.Audio
{
    /// <summary>Snapshot of one application (or the system sounds session) on the default output device.</summary>
    public sealed class AudioSessionInfo
    {
        public string Key { get; set; }

        public string DisplayName { get; set; }

        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; }

        public bool IsSystemSounds { get; set; }

        public float Volume { get; set; }

        public bool Muted { get; set; }

        public int Percent
        {
            get { return (int)Math.Round(Math.Max(0.0, Math.Min(1.0, Volume)) * 100.0); }
        }
    }
}
