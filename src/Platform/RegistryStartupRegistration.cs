using System;
using Microsoft.Win32;

namespace AudioSwitch.Platform
{
    public sealed class RegistryStartupRegistration : IStartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AudioSwitch";

        private readonly string _executablePath;
        private readonly ILogger _logger;

        public RegistryStartupRegistration(string executablePath, ILogger logger)
        {
            _executablePath = executablePath;
            _logger = logger;
        }

        public bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    {
                        if (key == null)
                        {
                            return false;
                        }

                        string value = key.GetValue(ValueName) as string;
                        return !string.IsNullOrWhiteSpace(value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("读取开机启动设置失败。", ex);
                    return false;
                }
            }
        }

        public void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开注册表启动项。");
                }

                if (enabled)
                {
                    key.SetValue(ValueName, "\"" + _executablePath + "\"", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
