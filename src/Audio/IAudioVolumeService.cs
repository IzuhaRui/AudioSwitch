using System;

namespace AudioSwitch.Audio
{
    /// <summary>Volume and mute state of one audio endpoint.</summary>
    public sealed class AudioVolumeState
    {
        public float Volume { get; set; }

        public bool Muted { get; set; }

        public int Percent
        {
            get { return (int)Math.Round(Math.Max(0.0, Math.Min(1.0, Volume)) * 100.0); }
        }
    }

    /// <summary>Describes a volume or mute change performed by this application.</summary>
    public sealed class AudioVolumeChangedEventArgs : EventArgs
    {
        public AudioVolumeChangedEventArgs(string deviceId, AudioVolumeState state, bool isMuteChange)
        {
            DeviceId = deviceId;
            State = state;
            IsMuteChange = isMuteChange;
        }

        public string DeviceId { get; private set; }

        public AudioVolumeState State { get; private set; }

        public bool IsMuteChange { get; private set; }
    }

    /// <summary>Reads and changes the volume / mute state of individual endpoints.</summary>
    public interface IAudioVolumeService : IDisposable
    {
        /// <summary>Raised after this application changes a device volume or mute state.</summary>
        event EventHandler<AudioVolumeChangedEventArgs> VolumeChanged;

        bool TryGetState(string deviceId, out AudioVolumeState state);

        bool TrySetVolume(string deviceId, float volume);

        bool TrySetMute(string deviceId, bool muted);

        /// <summary>
        /// Gets the current peak audio level (0..1) of the endpoint. Used for the live
        /// level meter that shows whether the device is currently playing/capturing sound.
        /// </summary>
        bool TryGetPeakValue(string deviceId, out float peak);
    }
}
