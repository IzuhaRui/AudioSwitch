using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AudioSwitch.Audio.Interop;
using AudioSwitch.Platform;

namespace AudioSwitch.Audio
{
    /// <summary>
    /// Core Audio implementation of <see cref="IAudioVolumeService"/>.
    /// The per-endpoint IAudioEndpointVolume interfaces are cached because the flyout
    /// polls them for external changes (media keys, other applications, ...).
    /// </summary>
    public sealed class AudioVolumeService : IAudioVolumeService
    {
        private readonly ILogger _logger;
        private readonly IMMDeviceEnumerator _enumerator;
        private readonly Dictionary<string, IAudioEndpointVolume> _volumes;
        private readonly Dictionary<string, IAudioMeterInformation> _meters;
        private bool _disposed;

        public AudioVolumeService(ILogger logger)
        {
            _logger = logger;
            _volumes = new Dictionary<string, IAudioEndpointVolume>(StringComparer.OrdinalIgnoreCase);
            _meters = new Dictionary<string, IAudioMeterInformation>(StringComparer.OrdinalIgnoreCase);
            _enumerator = CoreAudioNative.CreateComObject<IMMDeviceEnumerator>(AudioIids.CLSID_MMDeviceEnumerator);
        }

        public event EventHandler<AudioVolumeChangedEventArgs> VolumeChanged;

        public bool TryGetState(string deviceId, out AudioVolumeState state)
        {
            state = null;

            if (_disposed || string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

            IAudioEndpointVolume volume = GetVolumeInterface(deviceId);
            if (volume == null)
            {
                return false;
            }

            float scalar;
            if (volume.GetMasterVolumeLevelScalar(out scalar) < 0)
            {
                Forget(deviceId);
                return false;
            }

            bool muted;
            if (volume.GetMute(out muted) < 0)
            {
                Forget(deviceId);
                return false;
            }

            if (float.IsNaN(scalar) || float.IsInfinity(scalar))
            {
                scalar = 0f;
            }

            state = new AudioVolumeState
            {
                Volume = Math.Max(0f, Math.Min(1f, scalar)),
                Muted = muted
            };
            return true;
        }

        public bool TrySetVolume(string deviceId, float volume)
        {
            if (_disposed || string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

            IAudioEndpointVolume endpointVolume = GetVolumeInterface(deviceId);
            if (endpointVolume == null)
            {
                return false;
            }

            float clamped = Math.Max(0f, Math.Min(1f, volume));
            Guid eventContext = Guid.Empty;
            int hr = endpointVolume.SetMasterVolumeLevelScalar(clamped, ref eventContext);
            if (hr < 0)
            {
                _logger.Error("设置设备音量失败: 0x" + hr.ToString("X8") + " " + deviceId);
                Forget(deviceId);
                return false;
            }

            AudioVolumeState state;
            if (TryGetState(deviceId, out state))
            {
                RaiseVolumeChanged(deviceId, state, false);
            }

            return true;
        }

        public bool TrySetMute(string deviceId, bool muted)
        {
            if (_disposed || string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

            IAudioEndpointVolume endpointVolume = GetVolumeInterface(deviceId);
            if (endpointVolume == null)
            {
                return false;
            }

            Guid eventContext = Guid.Empty;
            int hr = endpointVolume.SetMute(muted, ref eventContext);
            if (hr < 0)
            {
                _logger.Error("设置设备静音失败: 0x" + hr.ToString("X8") + " " + deviceId);
                Forget(deviceId);
                return false;
            }

            AudioVolumeState state;
            if (TryGetState(deviceId, out state))
            {
                RaiseVolumeChanged(deviceId, state, true);
            }

            return true;
        }

        private void RaiseVolumeChanged(string deviceId, AudioVolumeState state, bool isMuteChange)
        {
            EventHandler<AudioVolumeChangedEventArgs> handler = VolumeChanged;
            if (handler != null)
            {
                handler(this, new AudioVolumeChangedEventArgs(deviceId, state, isMuteChange));
            }
        }

        public bool TryGetPeakValue(string deviceId, out float peak)
        {
            peak = 0f;

            if (_disposed || string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

            IAudioMeterInformation meter = GetMeterInterface(deviceId);
            if (meter == null)
            {
                return false;
            }

            float value;
            int hr = meter.GetPeakValue(out value);
            if (hr < 0)
            {
                ForgetMeter(deviceId);
                return false;
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = 0f;
            }

            peak = Math.Max(0f, Math.Min(1f, value));
            return true;
        }

        private IAudioMeterInformation GetMeterInterface(string deviceId)
        {
            IAudioMeterInformation cached;
            if (_meters.TryGetValue(deviceId, out cached))
            {
                return cached;
            }

            IMMDevice device = null;
            try
            {
                int hr = _enumerator.GetDevice(deviceId, out device);
                if (hr < 0 || device == null)
                {
                    return null;
                }

                Guid iid = AudioIids.IID_IAudioMeterInformation;
                IntPtr pointer;
                hr = device.Activate(ref iid, AudioConstants.CLSCTX_ALL, IntPtr.Zero, out pointer);
                if (hr < 0 || pointer == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    IAudioMeterInformation meter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(pointer);
                    _meters[deviceId] = meter;
                    return meter;
                }
                finally
                {
                    Marshal.Release(pointer);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("获取设备电平表接口失败: " + deviceId, ex);
                return null;
            }
            finally
            {
                CoreAudioNative.ReleaseComObject(device);
            }
        }

        private void ForgetMeter(string deviceId)
        {
            IAudioMeterInformation meter;
            if (_meters.TryGetValue(deviceId, out meter))
            {
                _meters.Remove(deviceId);
                CoreAudioNative.ReleaseComObject(meter);
            }
        }

        private IAudioEndpointVolume GetVolumeInterface(string deviceId)
        {
            IAudioEndpointVolume cached;
            if (_volumes.TryGetValue(deviceId, out cached))
            {
                return cached;
            }

            IMMDevice device = null;
            try
            {
                int hr = _enumerator.GetDevice(deviceId, out device);
                if (hr < 0 || device == null)
                {
                    return null;
                }

                Guid iid = AudioIids.IID_IAudioEndpointVolume;
                IntPtr pointer;
                hr = device.Activate(ref iid, AudioConstants.CLSCTX_ALL, IntPtr.Zero, out pointer);
                if (hr < 0 || pointer == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    IAudioEndpointVolume volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pointer);
                    _volumes[deviceId] = volume;
                    return volume;
                }
                finally
                {
                    Marshal.Release(pointer);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("获取设备音量接口失败: " + deviceId, ex);
                return null;
            }
            finally
            {
                CoreAudioNative.ReleaseComObject(device);
            }
        }

        private void Forget(string deviceId)
        {
            IAudioEndpointVolume volume;
            if (_volumes.TryGetValue(deviceId, out volume))
            {
                _volumes.Remove(deviceId);
                CoreAudioNative.ReleaseComObject(volume);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (IAudioEndpointVolume volume in _volumes.Values)
            {
                CoreAudioNative.ReleaseComObject(volume);
            }

            _volumes.Clear();

            foreach (IAudioMeterInformation meter in _meters.Values)
            {
                CoreAudioNative.ReleaseComObject(meter);
            }

            _meters.Clear();
            CoreAudioNative.ReleaseComObject(_enumerator);
        }
    }
}
