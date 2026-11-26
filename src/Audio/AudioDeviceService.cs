using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AudioSwitch.Audio.Interop;
using AudioSwitch.Platform;

namespace AudioSwitch.Audio
{
    /// <summary>
    /// Default Core Audio implementation of <see cref="IAudioDeviceService"/>.
    /// Uses IMMDeviceEnumerator for enumeration and IPolicyConfig for switching the default endpoint.
    /// </summary>
    public sealed class AudioDeviceService : IAudioDeviceService
    {
        private readonly ILogger _logger;
        private readonly IMMDeviceEnumerator _enumerator;
        private readonly IPolicyConfig _policyConfig;
        private readonly AudioNotificationClient _notificationClient;
        private bool _disposed;

        public AudioDeviceService(ILogger logger)
        {
            _logger = logger;

            try
            {
                _enumerator = CoreAudioNative.CreateComObject<IMMDeviceEnumerator>(AudioIids.CLSID_MMDeviceEnumerator);
                _policyConfig = CoreAudioNative.CreateComObject<IPolicyConfig>(AudioIids.CLSID_CPolicyConfigClient);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法初始化 Windows 音频设备接口。请确认 Windows 音频服务正在运行。", ex);
            }

            _notificationClient = new AudioNotificationClient(OnSystemAudioChanged);
            int hr = _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            if (hr < 0)
            {
                _logger.Error("注册音频设备变更通知失败: 0x" + hr.ToString("X8"));
            }
        }

        public event EventHandler DevicesChanged;

        public IList<AudioEndpoint> GetEndpoints()
        {
            ThrowIfDisposed();

            List<AudioEndpoint> endpoints = new List<AudioEndpoint>();

            // One broken endpoint/property store must not wipe out the whole list: each
            // flow and each device is isolated, failures are logged and skipped.
            try
            {
                AddFlow(EDataFlow.eRender, AudioDataFlow.Playback, endpoints);
            }
            catch (Exception ex)
            {
                _logger.Error("枚举播放设备失败，已跳过失败的设备。", ex);
            }

            try
            {
                AddFlow(EDataFlow.eCapture, AudioDataFlow.Recording, endpoints);
            }
            catch (Exception ex)
            {
                _logger.Error("枚举录音设备失败，已跳过失败的设备。", ex);
            }

            SortEndpoints(endpoints);
            return endpoints;
        }

        public IList<string> GetKnownDeviceIds()
        {
            ThrowIfDisposed();

            List<string> ids = new List<string>();
            bool complete = true;

            try
            {
                complete &= AddKnownIds(EDataFlow.eRender, ids);
            }
            catch (Exception ex)
            {
                complete = false;
                _logger.Error("枚举已安装播放设备失败。", ex);
            }

            try
            {
                complete &= AddKnownIds(EDataFlow.eCapture, ids);
            }
            catch (Exception ex)
            {
                complete = false;
                _logger.Error("枚举已安装录音设备失败。", ex);
            }

            // Never let a partial enumeration delete hidden-device preferences: the
            // caller (TrayPresenter) skips pruning when this method fails.
            if (!complete)
            {
                throw new InvalidOperationException(
                    "无法完整枚举已安装音频设备，已跳过隐藏设置清理。");
            }

            return ids;
        }

        public string GetDefaultEndpointId(AudioDataFlow flow)
        {
            ThrowIfDisposed();
            EDataFlow nativeFlow = flow == AudioDataFlow.Playback ? EDataFlow.eRender : EDataFlow.eCapture;
            return GetDefaultId(nativeFlow, ERole.eConsole);
        }

        public void SetDefaultDevice(string deviceId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(deviceId))
            {
                throw new ArgumentException("设备 ID 无效。", "deviceId");
            }

            int consoleResult = _policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
            _policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            _policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);

            if (consoleResult < 0)
            {
                Marshal.ThrowExceptionForHR(consoleResult);
            }
        }

        private void OnSystemAudioChanged()
        {
            EventHandler handler = DevicesChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void AddFlow(EDataFlow nativeFlow, AudioDataFlow flow, List<AudioEndpoint> result)
        {
            // Default-device lookups are decorative (checkmark); their failure must not
            // abort enumeration of the flow.
            string defaultConsole = TryGetDefaultId(nativeFlow, ERole.eConsole);
            string defaultMultimedia = TryGetDefaultId(nativeFlow, ERole.eMultimedia);
            string defaultCommunications = TryGetDefaultId(nativeFlow, ERole.eCommunications);

            IMMDeviceCollection collection = null;
            try
            {
                int hr = _enumerator.EnumAudioEndpoints(nativeFlow, AudioConstants.DEVICE_STATE_ACTIVE, out collection);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                int count;
                hr = collection.GetCount(out count);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        hr = collection.Item(i, out device);
                        if (hr < 0)
                        {
                            continue;
                        }

                        AudioEndpoint endpoint;
                        try
                        {
                            endpoint = ReadEndpoint(device, flow);
                        }
                        catch (Exception ex)
                        {
                            // A single device with a broken property store must be skipped,
                            // not take the whole enumeration down.
                            _logger.Error("读取音频设备属性失败，已跳过该设备。", ex);
                            continue;
                        }

                        if (endpoint == null)
                        {
                            continue;
                        }

                        endpoint.IsDefaultConsole = string.Equals(endpoint.Id, defaultConsole,
                            StringComparison.OrdinalIgnoreCase);
                        endpoint.IsDefaultMultimedia = string.Equals(endpoint.Id, defaultMultimedia,
                            StringComparison.OrdinalIgnoreCase);
                        endpoint.IsDefaultCommunications = string.Equals(endpoint.Id, defaultCommunications,
                            StringComparison.OrdinalIgnoreCase);
                        result.Add(endpoint);
                    }
                    finally
                    {
                        CoreAudioNative.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                CoreAudioNative.ReleaseComObject(collection);
            }
        }

        private AudioEndpoint ReadEndpoint(IMMDevice device, AudioDataFlow flow)
        {
            string id = GetDeviceId(device);
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            IPropertyStore store = null;
            try
            {
                int hr = device.OpenPropertyStore(AudioConstants.STGM_READ, out store);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                string name = CoreAudioNative.ReadString(store, PropertyKeys.DeviceFriendlyName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = CoreAudioNative.ReadString(store, PropertyKeys.DeviceDescription);
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = flow == AudioDataFlow.Playback ? "播放设备" : "录音设备";
                }

                return new AudioEndpoint
                {
                    Id = id,
                    Name = name.Trim(),
                    Flow = flow,
                    FormFactor = (EndpointFormFactor)GetFormFactor(store, flow)
                };
            }
            catch (Exception ex)
            {
                // A broken property store should not make the device disappear from the
                // list entirely (and definitely must not abort the whole enumeration):
                // fall back to a generic display name instead.
                _logger.Error("读取音频设备属性失败，设备 " + id + " 使用占位名称。", ex);
                return new AudioEndpoint
                {
                    Id = id,
                    Name = flow == AudioDataFlow.Playback ? "播放设备" : "录音设备",
                    Flow = flow,
                    FormFactor = flow == AudioDataFlow.Playback
                        ? EndpointFormFactor.Speakers
                        : EndpointFormFactor.Microphone
                };
            }
            finally
            {
                CoreAudioNative.ReleaseComObject(store);
            }
        }

        private static int GetFormFactor(IPropertyStore store, AudioDataFlow flow)
        {
            uint? value = CoreAudioNative.ReadUInt32(store, PropertyKeys.AudioEndpointFormFactor);
            if (value.HasValue)
            {
                int raw = unchecked((int)value.Value);
                if (raw >= 0 && raw <= (int)EndpointFormFactor.UnknownFormFactor)
                {
                    return raw;
                }
            }

            return flow == AudioDataFlow.Recording
                ? (int)EndpointFormFactor.Microphone
                : (int)EndpointFormFactor.Speakers;
        }

        private static string GetDeviceId(IMMDevice device)
        {
            IntPtr buffer;
            int hr = device.GetId(out buffer);
            if (hr < 0 || buffer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        private bool AddKnownIds(EDataFlow nativeFlow, List<string> result)
        {
            bool success = true;
            IMMDeviceCollection collection = null;
            try
            {
                int hr = _enumerator.EnumAudioEndpoints(nativeFlow, AudioConstants.DEVICE_STATEMASK_ALL,
                    out collection);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                int count;
                hr = collection.GetCount(out count);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        hr = collection.Item(i, out device);
                        if (hr < 0)
                        {
                            continue;
                        }

                        string id = GetDeviceId(device);
                        if (!string.IsNullOrEmpty(id) && !result.Contains(id))
                        {
                            result.Add(id);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Mark the whole lookup as incomplete: pruning must be skipped
                        // rather than risk deleting a hidden record for this device.
                        success = false;
                        _logger.Error("读取已安装音频设备 ID 失败，已跳过该设备。", ex);
                    }
                    finally
                    {
                        CoreAudioNative.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                CoreAudioNative.ReleaseComObject(collection);
            }

            return success;
        }

        private string TryGetDefaultId(EDataFlow flow, ERole role)
        {
            try
            {
                return GetDefaultId(flow, role);
            }
            catch (Exception ex)
            {
                _logger.Error("读取默认音频设备失败。", ex);
                return null;
            }
        }

        private string GetDefaultId(EDataFlow flow, ERole role)
        {
            IMMDevice device = null;
            try
            {
                int hr = _enumerator.GetDefaultAudioEndpoint(flow, role, out device);
                if (hr < 0)
                {
                    return null;
                }

                return GetDeviceId(device);
            }
            finally
            {
                CoreAudioNative.ReleaseComObject(device);
            }
        }

        private static void SortEndpoints(List<AudioEndpoint> endpoints)
        {
            // Keep the order stable (name based) so the list does not jump around when
            // the user switches the default device while the flyout is open.
            endpoints.Sort(delegate(AudioEndpoint left, AudioEndpoint right)
            {
                int byFlow = left.Flow.CompareTo(right.Flow);
                if (byFlow < 0)
                {
                    return byFlow;
                }

                return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("AudioDeviceService");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }
            catch
            {
                // Ignore: the audio service may already be shutting down.
            }

            CoreAudioNative.ReleaseComObject(_policyConfig);
            CoreAudioNative.ReleaseComObject(_enumerator);
        }
    }
}
