using System;
using System.Collections.Generic;

namespace AudioSwitch.Audio
{
    /// <summary>
    /// Enumerates application audio sessions on the default output device and changes
    /// their per-application volume / mute state.
    /// </summary>
    public interface IAudioSessionService : IDisposable
    {
        /// <summary>Snapshots the currently existing app sessions (grouped by process).</summary>
        IList<AudioSessionInfo> GetSessions();

        bool TrySetVolume(string sessionKey, float volume);

        bool TrySetMute(string sessionKey, bool muted);

        /// <summary>Current peak level (0..1) of the app session, for the live meter.</summary>
        bool TryGetPeakValue(string sessionKey, out float peak);
    }
}
