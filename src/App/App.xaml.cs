using System;
using System.Windows;
using System.Windows.Threading;
using AudioSwitch.Host;
using AudioSwitch.Platform;

namespace AudioSwitch
{
    /// <summary>
    /// Application entry point and composition root. It only wires the Host layer
    /// (modes, single instance, environment checks) together; all real work lives in
    /// the Audio, Platform, Tray and UI modules.
    /// </summary>
    public partial class App : Application
    {
        private ILogger _logger;
        private SingleInstanceGuard _singleInstanceGuard;
        private AppHost _appHost;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            _logger = new FileLogger();

            CommandLineOptions options = CommandLineOptions.Parse(e.Args);
            if (options.Mode != AppMode.Normal)
            {
                bool completed = DevCommands.Execute(options);
                if (completed)
                {
                    Shutdown();
                }

                return;
            }

            // A sandboxed (Low integrity) process cannot create a notification area icon,
            // the shell rejects Shell_NotifyIcon with ERROR_ACCESS_DENIED. Fail with a clear
            // instruction instead of silently running without a tray icon.
            if (IntegrityInspector.IsRestrictedForShellIntegration())
            {
                UiMessages.ShowFatal(_logger, "音频设备切换器",
                    UiMessages.BuildRestrictedEnvironmentMessage(AppEnvironment.ResolveExecutablePath()));
                Shutdown(2);
                return;
            }

            _singleInstanceGuard = SingleInstanceGuard.TryAcquire(Dispatcher);
            if (!_singleInstanceGuard.IsPrimary)
            {
                if (!_singleInstanceGuard.SignalPrimary())
                {
                    // Do not leave the user with a silent "double click does nothing":
                    // explain that another instance exists but could not be reached.
                    UiMessages.ShowFatal(_logger, "音频设备切换器",
                        UiMessages.BuildSecondInstanceSignalFailureMessage(_logger.LogFilePath));
                }

                Shutdown();
                return;
            }

            _singleInstanceGuard.ShowRequested += OnShowRequested;

            try
            {
                _appHost = new AppHost(_logger, Dispatcher);
                _appHost.ExitRequested += OnExitRequested;

                string errorMessage;
                if (!_appHost.Start(out errorMessage))
                {
                    UiMessages.ShowFatal(_logger, "音频设备切换器", errorMessage);
                    Shutdown(2);
                    return;
                }

                _logger.Info("音频设备切换器已启动。");

                // A show request may have arrived between acquiring the guard and the
                // AppHost being ready; deliver it now instead of losing it.
                if (_singleInstanceGuard.ConsumePendingShowRequest())
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                    {
                        if (_appHost != null)
                        {
                            _appHost.ShowFlyout();
                        }
                    }));
                }

                if (options.ShowFlyoutOnStartup)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                    {
                        if (_appHost != null)
                        {
                            _appHost.ShowFlyout();
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("启动失败。", ex);
                UiMessages.ShowFatal(_logger, "音频设备切换器",
                    UiMessages.BuildStartupFailureMessage(ex, _logger.LogFilePath));
                Shutdown(1);
            }
        }

        private void OnShowRequested(object sender, EventArgs e)
        {
            if (_appHost == null)
            {
                // Startup still in progress: leave the request buffered; it will be
                // consumed right after AppHost.Start() completes.
                return;
            }

            if (_singleInstanceGuard != null)
            {
                _singleInstanceGuard.ConsumePendingShowRequest();
            }

            _appHost.ShowFlyout();
        }

        private void OnExitRequested(object sender, EventArgs e)
        {
            Shutdown();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _logger.Error("未处理的界面线程异常。", e.Exception);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            _logger.Error("未处理的异常。", e.ExceptionObject as Exception);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_appHost != null)
            {
                try
                {
                    _appHost.ExitRequested -= OnExitRequested;
                    _appHost.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放应用组件失败。", ex);
                }

                _appHost = null;
            }

            if (_singleInstanceGuard != null)
            {
                try
                {
                    _singleInstanceGuard.ShowRequested -= OnShowRequested;
                    _singleInstanceGuard.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放单实例守卫失败。", ex);
                }

                _singleInstanceGuard = null;
            }

            _logger.Info("音频设备切换器已退出。");
            base.OnExit(e);
        }
    }
}
