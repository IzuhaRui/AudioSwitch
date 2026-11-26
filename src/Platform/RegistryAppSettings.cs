using System;
using Microsoft.Win32;

namespace AudioSwitch.Platform
{
    /// <summary>Stores flags under HKCU\Software\AudioSwitch.</summary>
    public sealed class RegistryAppSettings : IAppSettings
    {
        private const string SettingsKeyPath = @"Software\AudioSwitch";
        private readonly ILogger _logger;

        public RegistryAppSettings(ILogger logger)
        {
            _logger = logger;
        }

        public bool GetFlag(string name, bool defaultValue)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(name);
                        if (value is int)
                        {
                            return (int)value != 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("读取设置失败: " + name, ex);
            }

            return defaultValue;
        }

        public string GetString(string name, string defaultValue)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(name);
                        string text = value as string;
                        if (text != null)
                        {
                            return text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("读取文本设置失败: " + name, ex);
            }

            return defaultValue;
        }

        public void SetString(string name, string value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value ?? string.Empty, RegistryValueKind.String);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("保存文本设置失败: " + name, ex);
            }
        }

        public void SetFlag(string name, bool value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("保存设置失败: " + name, ex);
            }
        }
    }
}
