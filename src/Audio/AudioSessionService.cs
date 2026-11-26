using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AudioSwitch.Audio.Interop;
using AudioSwitch.Platform;

namespace AudioSwitch.Audio
{
    /// <summary>
    /// Core Audio implementation of per-application volume control (IAudioSessionManager2
    /// + ISimpleAudioVolume). Sessions are grouped by process id so one app row controls
    /// all of that app's sessions on the default output device.
    /// </summary>
    public sealed class AudioSessionService : IAudioSessionService
    {
        private sealed class SessionEntry
        {
            public IAudioSessionControl Control;
            public ISimpleAudioVolume Volume;
            public IAudioMeterInformation Meter;
        }

        private sealed class SessionGroup
        {
            public string Key;
            public string DisplayName;
            public int ProcessId;
            public bool IsSystemSounds;
            public string ExecutablePath;
            public float Volume;
            public bool Muted;
            public readonly List<SessionEntry> Sessions = new List<SessionEntry>();
        }

        private readonly ILogger _logger;
        private readonly IMMDeviceEnumerator _enumerator;
        private readonly Dictionary<string, SessionGroup> _cache =
            new Dictionary<string, SessionGroup>(StringComparer.OrdinalIgnoreCase);
        private bool _cacheValid;
        private bool _disposed;

        public AudioSessionService(ILogger logger)
        {
            _logger = logger;
            try
            {
                _enumerator = CoreAudioNative.CreateComObject<IMMDeviceEnumerator>(
                    AudioIids.CLSID_MMDeviceEnumerator);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法初始化 Windows 音频会话接口。", ex);
            }
        }

        public IList<AudioSessionInfo> GetSessions()
        {
            ThrowIfDisposed();

            List<AudioSessionInfo> result = new List<AudioSessionInfo>();
            ReleaseCache();

            IMMDevice device = null;
            IAudioSessionManager2 manager = null;
            IAudioSessionEnumerator enumerator = null;

            try
            {
                int hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole,
                    out device);
                if (hr < 0 || device == null)
                {
                    return result;
                }

                Guid iid = AudioSessionIids.IID_IAudioSessionManager2;
                IntPtr pointer;
                hr = device.Activate(ref iid, AudioConstants.CLSCTX_ALL, IntPtr.Zero, out pointer);
                if (hr < 0 || pointer == IntPtr.Zero)
                {
                    return result;
                }

                try
                {
                    manager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(pointer);
                }
                finally
                {
                    Marshal.Release(pointer);
                }

                hr = manager.GetSessionEnumerator(out enumerator);
                if (hr < 0 || enumerator == null)
                {
                    return result;
                }

                int count;
                hr = enumerator.GetCount(out count);
                if (hr < 0)
                {
                    return result;
                }

                for (int i = 0; i < count; i++)
                {
                    TryReadSession(enumerator, i);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("枚举应用音频会话失败。", ex);
            }
            finally
            {
                CoreAudioNative.ReleaseComObject(enumerator);
                CoreAudioNative.ReleaseComObject(manager);
                CoreAudioNative.ReleaseComObject(device);
            }

            foreach (SessionGroup group in _cache.Values)
            {
                result.Add(new AudioSessionInfo
                {
                    Key = group.Key,
                    DisplayName = group.DisplayName,
                    ProcessId = group.ProcessId,
                    ExecutablePath = group.ExecutablePath,
                    IsSystemSounds = group.IsSystemSounds,
                    Volume = group.Volume,
                    Muted = group.Muted
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

            _cacheValid = true;
            return result;
        }

        public bool TrySetVolume(string sessionKey, float volume)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(sessionKey))
            {
                return false;
            }

            EnsureCache();
            SessionGroup group;
            if (!_cache.TryGetValue(sessionKey, out group))
            {
                return false;
            }

            float clamped = Math.Max(0f, Math.Min(1f, volume));
            Guid context = Guid.Empty;
            bool applied = false;
            foreach (SessionEntry entry in group.Sessions)
            {
                try
                {
                    int hr = entry.Volume.SetMasterVolume(clamped, ref context);
                    if (hr >= 0)
                    {
                        applied = true;
                    }
                    else
                    {
                        _logger.Error("设置应用音量失败: " + group.DisplayName + "，HRESULT=0x"
                            + hr.ToString("X8"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("设置应用音量失败: " + group.DisplayName, ex);
                }
            }

            return applied;
        }

        public bool TryGetPeakValue(string sessionKey, out float peak)
        {
            peak = 0f;
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(sessionKey))
            {
                return false;
            }

            EnsureCache();
            SessionGroup group;
            if (!_cache.TryGetValue(sessionKey, out group))
            {
                return false;
            }

            float max = 0f;
            bool any = false;
            foreach (SessionEntry entry in group.Sessions)
            {
                if (entry.Meter == null)
                {
                    continue;
                }

                try
                {
                    float value;
                    if (entry.Meter.GetPeakValue(out value) >= 0)
                    {
                        if (!float.IsNaN(value) && !float.IsInfinity(value) && value > max)
                        {
                            max = value;
                        }

                        any = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("读取应用电平失败: " + group.DisplayName, ex);
                }
            }

            peak = Math.Max(0f, Math.Min(1f, max));
            return any;
        }

        public bool TrySetMute(string sessionKey, bool muted)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(sessionKey))
            {
                return false;
            }

            EnsureCache();
            SessionGroup group;
            if (!_cache.TryGetValue(sessionKey, out group))
            {
                return false;
            }

            Guid context = Guid.Empty;
            bool applied = false;
            foreach (SessionEntry entry in group.Sessions)
            {
                try
                {
                    int hr = entry.Volume.SetMute(muted, ref context);
                    if (hr >= 0)
                    {
                        applied = true;
                    }
                    else
                    {
                        _logger.Error("设置应用静音失败: " + group.DisplayName + "，HRESULT=0x"
                            + hr.ToString("X8"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("设置应用静音失败: " + group.DisplayName, ex);
                }
            }

            return applied;
        }

        private void TryReadSession(IAudioSessionEnumerator enumerator, int index)
        {
            IAudioSessionControl control = null;
            bool keepControl = false;
            try
            {
                int hr = enumerator.GetSession(index, out control);
                if (hr < 0 || control == null)
                {
                    return;
                }

                IAudioSessionControl2 control2 = control as IAudioSessionControl2;
                if (control2 == null)
                {
                    return;
                }

                int state;
                if (control2.GetState(out state) < 0 || state == 2)
                {
                    // AudioSessionStateExpired
                    return;
                }

                int processId;
                if (control2.GetProcessId(out processId) < 0)
                {
                    return;
                }

                bool isSystemSounds = control2.IsSystemSoundsSession() == 0;

                ISimpleAudioVolume volume = control as ISimpleAudioVolume;
                if (volume == null)
                {
                    return;
                }

                float level;
                bool muted;
                if (volume.GetMasterVolume(out level) < 0 || volume.GetMute(out muted) < 0)
                {
                    return;
                }

                string key = isSystemSounds ? "system" : "pid:" + processId;
                SessionGroup group;
                if (!_cache.TryGetValue(key, out group))
                {
                    group = new SessionGroup
                    {
                        Key = key,
                        ProcessId = processId,
                        IsSystemSounds = isSystemSounds,
                        DisplayName = isSystemSounds ? "系统声音" : GetProcessName(processId),
                        ExecutablePath = isSystemSounds ? null : GetProcessPath(processId)
                    };
                    _cache[key] = group;
                }

                IAudioMeterInformation meter = control as IAudioMeterInformation;
                group.Sessions.Add(new SessionEntry
                {
                    Control = control,
                    Volume = volume,
                    Meter = meter
                });
                keepControl = true;
                group.Volume = Math.Max(0f, Math.Min(1f, level));
                group.Muted = muted;
            }
            catch (Exception ex)
            {
                _logger.Error("读取应用音频会话失败。", ex);
            }
            finally
            {
                // The cached SessionEntry holds the original control RCW: releasing it here
                // would disconnect the derived ISimpleAudioVolume/IAudioMeterInformation
                // interfaces (InvalidComObjectException) and make volume changes no-ops.
                if (!keepControl)
                {
                    CoreAudioNative.ReleaseComObject(control);
                }
            }
        }

        private static string GetProcessPath(int processId)
        {
            if (processId <= 0)
            {
                return null;
            }

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return process.MainModule.FileName;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetProcessName(int processId)
        {
            if (processId <= 0)
            {
                return "系统声音";
            }

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    string name = process.ProcessName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
            }
            catch
            {
                // The process may have exited between session enumeration and lookup.
            }

            return "应用 (PID " + processId + ")";
        }

        private void EnsureCache()
        {
            if (!_cacheValid)
            {
                GetSessions();
            }
        }

        private void ReleaseCache()
        {
            foreach (SessionGroup group in _cache.Values)
            {
                foreach (SessionEntry entry in group.Sessions)
                {
                    // Release the anchor control RCW only; Volume/Meter are interfaces of
                    // the same RCW and are disconnected at the same time.
                    CoreAudioNative.ReleaseComObject(entry.Control);
                    entry.Control = null;
                    entry.Volume = null;
                    entry.Meter = null;
                }

                group.Sessions.Clear();
            }

            _cache.Clear();
            _cacheValid = false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("AudioSessionService");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseCache();
            CoreAudioNative.ReleaseComObject(_enumerator);
        }
    }
}
