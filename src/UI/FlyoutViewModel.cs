using System;
using System.Collections.Generic;
using AudioSwitch.Audio;
using AudioSwitch.Platform;

namespace AudioSwitch.UI
{
    /// <summary>
    /// View model of the device flyout: groups endpoints into sections, switches the
    /// default device and exposes per-device volume / mute state.
    /// </summary>
    public sealed class FlyoutViewModel
    {
        private readonly IAudioDeviceService _audio;
        private readonly IAudioVolumeService _volume;
        private readonly IDeviceVisibilityService _visibility;

        public FlyoutViewModel(IAudioDeviceService audio, IAudioVolumeService volume,
            IDeviceVisibilityService visibility)
        {
            if (audio == null)
            {
                throw new ArgumentNullException("audio");
            }

            if (volume == null)
            {
                throw new ArgumentNullException("volume");
            }

            if (visibility == null)
            {
                throw new ArgumentNullException("visibility");
            }

            _audio = audio;
            _volume = volume;
            _visibility = visibility;
            Sections = new List<DeviceSectionViewModel>();
        }

        public IList<DeviceSectionViewModel> Sections { get; private set; }

        public void Refresh()
        {
            IList<AudioEndpoint> endpoints = _audio.GetEndpoints();

            // Do NOT prune hidden ids here: only a small subset of devices is active at
            // any time, and a temporarily unplugged device must not lose its hide choice.
            // Safe cleanup is done by TrayPresenter using GetKnownDeviceIds() (all states).
            List<DeviceSectionViewModel> sections = new List<DeviceSectionViewModel>();
            sections.Add(BuildSection("输出", "未检测到播放设备", endpoints, AudioDataFlow.Playback));
            sections.Add(BuildSection("输入", "未检测到录音设备", endpoints, AudioDataFlow.Recording));
            Sections = sections;
        }

        /// <summary>Hides one device from the panel (persisted; restore from the tray menu).</summary>
        public bool HideDevice(DeviceItemViewModel item)
        {
            if (item == null)
            {
                return false;
            }

            bool changed = _visibility.Hide(item.Endpoint.Id);
            if (changed)
            {
                Refresh();
            }

            return changed;
        }

        /// <summary>Shows every previously hidden device again.</summary>
        public bool RestoreAllDevices()
        {
            bool changed = _visibility.ShowAll();
            if (changed)
            {
                Refresh();
            }

            return changed;
        }

        public void Select(DeviceItemViewModel item)
        {
            if (item == null)
            {
                return;
            }

            if (item.IsDefault)
            {
                return;
            }

            _audio.SetDefaultDevice(item.Endpoint.Id);
        }

        public bool TryGetVolumeState(DeviceItemViewModel item, out AudioVolumeState state)
        {
            state = null;
            return item != null && _volume.TryGetState(item.Endpoint.Id, out state);
        }

        public bool TrySetVolume(DeviceItemViewModel item, float volume)
        {
            return item != null && _volume.TrySetVolume(item.Endpoint.Id, volume);
        }

        public bool TrySetMute(DeviceItemViewModel item, bool muted)
        {
            return item != null && _volume.TrySetMute(item.Endpoint.Id, muted);
        }

        public bool TryGetPeakValue(DeviceItemViewModel item, out float peak)
        {
            peak = 0f;
            return item != null && _volume.TryGetPeakValue(item.Endpoint.Id, out peak);
        }

        private DeviceSectionViewModel BuildSection(string title, string emptyText,
            IList<AudioEndpoint> endpoints, AudioDataFlow flow)
        {
            List<DeviceItemViewModel> items = new List<DeviceItemViewModel>();
            int deviceCount = 0;

            if (endpoints != null)
            {
                foreach (AudioEndpoint endpoint in endpoints)
                {
                    if (endpoint == null || endpoint.Flow != flow)
                    {
                        continue;
                    }

                    deviceCount++;
                    if (!_visibility.IsHidden(endpoint.Id))
                    {
                        items.Add(new DeviceItemViewModel(endpoint));
                    }
                }
            }

            // Make it obvious when the section is empty because every device was hidden,
            // instead of claiming no hardware exists.
            if (deviceCount > 0 && items.Count == 0)
            {
                emptyText = "已隐藏全部设备（右键托盘图标 → 取消所有隐藏）";
            }

            return new DeviceSectionViewModel(title, emptyText, items);
        }
    }
}
