using System;
using System.Collections.Generic;
using AudioSwitch.Audio;

namespace AudioSwitch.Host
{
    /// <summary>In-memory app sessions used by the developer preview / screenshot / UI test modes.</summary>
    internal sealed class DemoAudioSessionService : IAudioSessionService
    {
        private readonly Dictionary<string, AudioSessionInfo> _sessions;

        public DemoAudioSessionService()
        {
            _sessions = new Dictionary<string, AudioSessionInfo>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "app-browser", new AudioSessionInfo
                    {
                        Key = "app-browser", DisplayName = "浏览器", ProcessId = 1200,
                        ExecutablePath = GetDemoPath("cmd.exe"), Volume = 0.65f
                    }
                },
                {
                    "app-music", new AudioSessionInfo
                    {
                        Key = "app-music", DisplayName = "音乐播放器", ProcessId = 2400,
                        ExecutablePath = GetDemoPath("notepad.exe"), Volume = 0.42f
                    }
                },
                {
                    "app-game", new AudioSessionInfo
                    {
                        Key = "app-game", DisplayName = "游戏", ProcessId = 3600,
                        ExecutablePath = GetDemoPath("mspaint.exe"), Volume = 0.80f, Muted = true
                    }
                },
                {
                    "system", new AudioSessionInfo
                    {
                        Key = "system", DisplayName = "系统声音", IsSystemSounds = true,
                        Volume = 0.30f
                    }
                }
            };
        }

        private static string GetDemoPath(string fileName)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string path = System.IO.Path.Combine(windows, "System32", fileName);
            return System.IO.File.Exists(path) ? path : null;
        }

        public IList<AudioSessionInfo> GetSessions()
        {
            List<AudioSessionInfo> result = new List<AudioSessionInfo>();
            foreach (AudioSessionInfo session in _sessions.Values)
            {
                result.Add(new AudioSessionInfo
                {
                    Key = session.Key,
                    DisplayName = session.DisplayName,
                    ProcessId = session.ProcessId,
                    ExecutablePath = session.ExecutablePath,
                    IsSystemSounds = session.IsSystemSounds,
                    Volume = session.Volume,
                    Muted = session.Muted
                });
            }

            result.Sort(delegate(AudioSessionInfo left, AudioSessionInfo right)
            {
                if (left.IsSystemSounds != right.IsSystemSounds)
                {
                    return left.IsSystemSounds ? -1 : 1;
                }

                return string.Compare(left.DisplayName, right.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        public bool TrySetVolume(string sessionKey, float volume)
        {
            AudioSessionInfo session;
            if (string.IsNullOrEmpty(sessionKey) || !_sessions.TryGetValue(sessionKey, out session))
            {
                return false;
            }

            session.Volume = Math.Max(0f, Math.Min(1f, volume));
            return true;
        }

        public bool TryGetPeakValue(string sessionKey, out float peak)
        {
            switch (sessionKey)
            {
                case "app-browser": peak = 0.62f; return true;
                case "app-music": peak = 0.38f; return true;
                case "app-game": peak = 0.74f; return true;
                case "system": peak = 0.22f; return true;
                default: peak = 0f; return false;
            }
        }

        public bool TrySetMute(string sessionKey, bool muted)
        {
            AudioSessionInfo session;
            if (string.IsNullOrEmpty(sessionKey) || !_sessions.TryGetValue(sessionKey, out session))
            {
                return false;
            }

            session.Muted = muted;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
