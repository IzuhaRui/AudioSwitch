using System;
using System.Windows.Threading;
using AudioSwitch.Audio;
using AudioSwitch.Platform;
using AudioSwitch.Tray;
using AudioSwitch.UI;

namespace AudioSwitch.Host
{
    /// <summary>
    /// Composition root: creates the platform and domain services, wires them together
    /// through the tray presenter and owns their lifetime. This is the only place where
    /// concrete implementations meet.
    /// </summary>
    internal sealed class AppHost : IDisposable
    {
        private readonly ILogger _logger;
        private readonly Dispatcher _dispatcher;

        private AudioDeviceService _audio;
        private AudioVolumeService _audioVolume;
        private AudioSessionService _audioSessions;
        private AppVolumeViewModel _appVolumeViewModel;
        private IThemeProvider _theme;
        private IAppSettings _settings;
        private IDeviceVisibilityService _deviceVisibility;
        private IStartupRegistration _startup;
        private IShellLauncher _shellLauncher;
        private ITrayIconHost _trayHost;
        private FlyoutViewModel _flyoutViewModel;
        private TrayPresenter _presenter;
        private bool _disposed;

        public AppHost(ILogger logger, Dispatcher dispatcher)
        {
            _logger = logger;
            _dispatcher = dispatcher;
        }

        public event EventHandler ExitRequested;

        public bool Start(out string errorMessage)
        {
            errorMessage = null;

            try
            {
                if (NativeMethods.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
                {
                    errorMessage = "找不到 Windows 任务栏。请确认“Windows 资源管理器”正在运行后重新启动本程序。\n\n"
                        + "日志文件：" + _logger.LogFilePath;
                    return false;
                }

                _audio = new AudioDeviceService(_logger);
                _audioVolume = new AudioVolumeService(_logger);
                _audioSessions = new AudioSessionService(_logger);
                _appVolumeViewModel = new AppVolumeViewModel(_audioSessions);
                _theme = new SystemThemeProvider(_logger);
                _settings = new RegistryAppSettings(_logger);
                _deviceVisibility = new DeviceVisibilityService(_settings, _logger);
                _startup = new RegistryStartupRegistration(AppEnvironment.ResolveExecutablePath(), _logger);
                _shellLauncher = new ShellLauncher(_logger);

                try
                {
                    _trayHost = new NotifyIconTrayHost(_logger);
                }
                catch (Exception ex)
                {
                    errorMessage = "无法创建任务栏通知区域图标。\n\n" + ex.Message
                        + "\n\n请在“文件资源管理器”中双击 AudioSwitch.exe 重新启动。\n\n日志文件：" + _logger.LogFilePath;
                    _logger.Error("创建托盘图标失败。", ex);
                    Cleanup();
                    return false;
                }

                _flyoutViewModel = new FlyoutViewModel(_audio, _audioVolume, _deviceVisibility);
                _presenter = new TrayPresenter(_dispatcher, _trayHost, _audio, _audioVolume, _theme, _settings,
                    _deviceVisibility, _startup, _shellLauncher, _logger, CreateFlyoutWindow);
                _presenter.ExitRequested += OnPresenterExitRequested;
                _presenter.Start();

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("初始化应用组件失败。", ex);
                errorMessage = "音频设备切换器启动失败。\n\n" + ex.Message
                    + "\n\n日志文件：" + _logger.LogFilePath;
                Cleanup();
                return false;
            }
        }

        public void ShowFlyout()
        {
            if (_presenter != null)
            {
                _presenter.ShowFlyout();
            }
        }

        private FlyoutWindow CreateFlyoutWindow()
        {
            return new FlyoutWindow(_flyoutViewModel, _appVolumeViewModel, _theme, _settings, _logger);
        }

        private void OnPresenterExitRequested(object sender, EventArgs e)
        {
            EventHandler handler = ExitRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void Cleanup()
        {
            // Must run for both failure and normal-exit paths: the theme provider
            // unsubscribes SystemEvents and the audio services release COM objects.
            if (_trayHost != null)
            {
                try
                {
                    _trayHost.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放托盘宿主失败。", ex);
                }

                _trayHost = null;
            }

            if (_deviceVisibility != null)
            {
                try
                {
                    _deviceVisibility.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放隐藏设备服务失败。", ex);
                }

                _deviceVisibility = null;
            }

            if (_theme != null)
            {
                try
                {
                    _theme.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放主题服务失败。", ex);
                }

                _theme = null;
            }

            if (_audioSessions != null)
            {
                try
                {
                    _audioSessions.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放音频会话服务失败。", ex);
                }

                _audioSessions = null;
            }

            if (_audioVolume != null)
            {
                try
                {
                    _audioVolume.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放音量服务失败。", ex);
                }

                _audioVolume = null;
            }

            if (_audio != null)
            {
                try
                {
                    _audio.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放音频设备服务失败。", ex);
                }

                _audio = null;
            }

            _settings = null;
            _startup = null;
            _shellLauncher = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_presenter != null)
            {
                try
                {
                    _presenter.ExitRequested -= OnPresenterExitRequested;
                    _presenter.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放托盘组件失败。", ex);
                }

                _presenter = null;
                _trayHost = null;
            }

            Cleanup();
        }
    }
}
