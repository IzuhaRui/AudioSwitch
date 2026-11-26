using System;
using System.Collections.Generic;

namespace AudioSwitch.Platform
{
    /// <summary>
    /// Stores the hidden endpoint ids as a ';' separated string in the per-user
    /// application settings (registry).
    /// </summary>
    public sealed class DeviceVisibilityService : IDeviceVisibilityService
    {
        private const string SettingName = "HiddenDevices";
        private const char Separator = ';';

        private readonly IAppSettings _settings;
        private readonly ILogger _logger;
        private readonly List<string> _hidden;
        private bool _disposed;

        public DeviceVisibilityService(IAppSettings settings, ILogger logger)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            _settings = settings;
            _logger = logger;
            _hidden = new List<string>();
            Load();
        }

        public event EventHandler Changed;

        public bool HasHiddenDevices
        {
            get { return _hidden.Count > 0; }
        }

        public bool IsHidden(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

            foreach (string hidden in _hidden)
            {
                if (string.Equals(hidden, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool Hide(string deviceId)
        {
            if (_disposed || string.IsNullOrEmpty(deviceId) || IsHidden(deviceId))
            {
                return false;
            }

            _hidden.Add(deviceId);
            Save();
            _logger.Info("已隐藏音频设备: " + deviceId);
            RaiseChanged();
            return true;
        }

        public bool ShowAll()
        {
            if (_disposed || _hidden.Count == 0)
            {
                return false;
            }

            _hidden.Clear();
            Save();
            _logger.Info("已取消隐藏所有音频设备。");
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Removes stored ids that no longer match any existing endpoint (for example a
        /// permanently removed/renamed device). Returns the number of removed entries.
        /// </summary>
        public int Prune(IEnumerable<string> knownDeviceIds)
        {
            if (_disposed || _hidden.Count == 0 || knownDeviceIds == null)
            {
                return 0;
            }

            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in knownDeviceIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    known.Add(id);
                }
            }

            // Fail-safe: an empty/partial enumeration (audio service glitch, sandbox, no
            // active devices) must never erase the user's hidden-device preferences.
            if (known.Count == 0)
            {
                return 0;
            }

            int removed = _hidden.RemoveAll(delegate(string id) { return !known.Contains(id); });
            if (removed > 0)
            {
                Save();
                _logger.Info("已清理 " + removed + " 个失效的隐藏设备记录。");
                RaiseChanged();
            }

            return removed;
        }

        private void Load()
        {
            try
            {
                string raw = _settings.GetString(SettingName, null);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }

                foreach (string part in raw.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string id = part.Trim();
                    if (id.Length > 0 && !Contains(id))
                    {
                        _hidden.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("读取隐藏设备设置失败。", ex);
            }
        }

        private bool Contains(string deviceId)
        {
            foreach (string hidden in _hidden)
            {
                if (string.Equals(hidden, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void Save()
        {
            try
            {
                _settings.SetString(SettingName, string.Join(Separator.ToString(), _hidden));
            }
            catch (Exception ex)
            {
                _logger.Error("保存隐藏设备设置失败。", ex);
            }
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
