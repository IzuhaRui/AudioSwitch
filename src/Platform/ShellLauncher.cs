using System;
using System.Diagnostics;

namespace AudioSwitch.Platform
{
    public sealed class ShellLauncher : IShellLauncher
    {
        private readonly ILogger _logger;

        public ShellLauncher(ILogger logger)
        {
            _logger = logger;
        }

        public void OpenSoundSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Error("打开 Windows 声音设置失败。", ex);
                OpenSoundControlPanel();
            }
        }

        public void OpenSoundControlPanel()
        {
            try
            {
                Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,0") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Error("打开传统声音控制面板失败。", ex);
            }
        }
    }
}
