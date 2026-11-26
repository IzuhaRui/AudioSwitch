using System;
using System.Collections.Generic;
using AudioSwitch.Audio;

namespace AudioSwitch.Host
{
    /// <summary>In-memory volume state used by the developer preview / screenshot modes.</summary>
    internal sealed class DemoAudioVolumeService : IAudioVolumeService
    {
        private readonly Dictionary<string, AudioVolumeState> _states;

        public DemoAudioVolumeService()
        {
            _states = new Dictionary<string, AudioVolumeState>(StringComparer.OrdinalIgnoreCase)
            {
                { "demo-playback-1", new AudioVolumeState { Volume = 0.65f } },
                { "demo-playback-2", new AudioVolumeState { Volume = 0.42f } },
                { "demo-playback-3", new AudioVolumeState { Volume = 0.30f } },
                { "demo-playback-4", new AudioVolumeState { Volume = 0.55f } },
                { "demo-recording-1", new AudioVolumeState { Volume = 0.80f } },
                { "demo-recording-2", new AudioVolumeState { Volume = 0.50f } },
                { "demo-recording-3", new AudioVolumeState { Volume = 0.20f, Muted = true } }
            };
        }

        public event EventHandler<AudioVolumeChangedEventArgs> VolumeChanged;

        public bool TryGetState(string deviceId, out AudioVolumeState state)
        {
            state = null;
            if (string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

            AudioVolumeState found;
            if (!_states.TryGetValue(deviceId, out found))
            {
                found = new AudioVolumeState { Volume = 0.5f };
                _states[deviceId] = found;
            }

            state = new AudioVolumeState { Volume = found.Volume, Muted = found.Muted };
            return true;
        }

        /// <summary>Test helper: changes stored state without raising VolumeChanged.</summary>
        internal void SimulateExternalState(string deviceId, float volume, bool muted)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return;
            }

            AudioVolumeState state;
            if (!_states.TryGetValue(deviceId, out state))
            {
                state = new AudioVolumeState();
                _states[deviceId] = state;
            }

            state.Volume = Math.Max(0f, Math.Min(1f, volume));
            state.Muted = muted;
        }

        public bool TrySetVolume(string deviceId, float volume)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

            AudioVolumeState state;
            if (!_states.TryGetValue(deviceId, out state))
            {
                state = new AudioVolumeState();
                _states[deviceId] = state;
            }

            state.Volume = Math.Max(0f, Math.Min(1f, volume));
            RaiseVolumeChanged(deviceId, state, false);
            return true;
        }

        public bool TrySetMute(string deviceId, bool muted)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

            AudioVolumeState state;
            if (!_states.TryGetValue(deviceId, out state))
            {
                state = new AudioVolumeState();
                _states[deviceId] = state;
            }

            state.Muted = muted;
            RaiseVolumeChanged(deviceId, state, true);
            return true;
        }

        private void RaiseVolumeChanged(string deviceId, AudioVolumeState state, bool isMuteChange)
        {
            EventHandler<AudioVolumeChangedEventArgs> handler = VolumeChanged;
            if (handler != null)
            {
                handler(this, new AudioVolumeChangedEventArgs(deviceId,
                    new AudioVolumeState { Volume = state.Volume, Muted = state.Muted }, isMuteChange));
            }
        }

        public bool TryGetPeakValue(string deviceId, out float peak)
        {
            // A fixed but varied set of levels so previews/screenshots show the meters.
            switch (deviceId)
            {
                case "demo-playback-1":
                    peak = 0.62f;
                    return true;
                case "demo-playback-2":
                    peak = 0.35f;
                    return true;
                case "demo-playback-3":
                    peak = 0.0f;
                    return true;
                case "demo-playback-4":
                    peak = 0.18f;
                    return true;
                case "demo-recording-1":
                    peak = 0.55f;
                    return true;
                case "demo-recording-2":
                    peak = 0.28f;
                    return true;
                case "demo-recording-3":
                    peak = 0.42f;
                    return true;
                default:
                    peak = 0f;
                    return false;
            }
        }

        public void Dispose()
        {
        }
    }
}
