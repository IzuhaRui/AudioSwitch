using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Media;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AudioSwitch.Audio;
using AudioSwitch.Platform;
using AudioSwitch.Tray;
using AudioSwitch.UI;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Imaging;

namespace AudioSwitch.Host
{
    /// <summary>Hidden command line modes used for diagnostics and UI verification.</summary>
    internal static class DevCommands
    {
        /// <summary>Returns true when the command finished synchronously and the app can shut down.</summary>
        public static bool Execute(CommandLineOptions options)
        {
            FileLogger logger = new FileLogger();

            switch (options.Mode)
            {
                case AppMode.Diag:
                    RunDiagnostics(options, logger);
                    return true;
                case AppMode.CheckEnvironment:
                    RunCheckEnvironment(options, logger);
                    return true;
                case AppMode.Preview:
                    RunPreview(options, logger);
                    return true;
                case AppMode.Capture:
                    RunCapture(options, logger);
                    return false;
                case AppMode.Screenshot:
                    RunScreenshot(options, logger);
                    return false;
                case AppMode.Verify:
                    RunVerify(options, logger);
                    return false;
                case AppMode.UiTest:
                    RunUiTest(options, logger);
                    return false;
                case AppMode.TrayIcons:
                    RunTrayIcons(options, logger);
                    return true;
                case AppMode.TrayTest:
                    RunTrayTest(options, logger);
                    return true;
                case AppMode.AppsTest:
                    RunAppsTest(options, logger);
                    return true;
                default:
                    return true;
            }
        }

        // ------------------------------------------------------------------ diagnostics

        private static void RunDiagnostics(CommandLineOptions options, ILogger logger)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("AudioSwitch 诊断信息");
            report.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("操作系统: " + Environment.OSVersion.VersionString);
            report.AppendLine("进程: " + (Environment.Is64BitProcess ? "64 位" : "32 位")
                + "，CLR " + Environment.Version);
            report.AppendLine("区域: " + CultureInfo.CurrentCulture.Name);
            report.AppendLine("程序: " + AppEnvironment.ResolveExecutablePath());
            report.AppendLine("日志: " + logger.LogFilePath);
            report.AppendLine("权限级别: " + IntegrityInspector.Describe());
            AppendDpiReport(report);
            report.AppendLine("任务栏窗口: " + (NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero
                ? "存在" : "未找到"));
            report.AppendLine("托盘注册能力: " + TrayRegistrationProbe.Describe());
            AppendDpiReport(report);
            report.AppendLine();

            IThemeProvider theme = null;
            AudioDeviceService audio = null;
            AudioVolumeService volume = null;
            try
            {
                theme = new SystemThemeProvider(logger);
                report.AppendLine("系统主题: " + theme.GetTheme());
                report.AppendLine();

                audio = new AudioDeviceService(logger);
                volume = new AudioVolumeService(logger);
                AppendEndpointReport(report, audio, volume, options.SwitchTest, options.VolumeTest);
                report.AppendLine("结果: OK");
            }
            catch (Exception ex)
            {
                report.AppendLine("结果: 失败");
                report.AppendLine(ex.ToString());
                logger.Error("诊断模式失败。", ex);
            }
            finally
            {
                if (volume != null)
                {
                    volume.Dispose();
                }

                if (audio != null)
                {
                    audio.Dispose();
                }

                if (theme != null)
                {
                    theme.Dispose();
                }
            }

            WriteReport(options.OutputPath, report.ToString(), logger);
        }

        private static void RunCheckEnvironment(CommandLineOptions options, ILogger logger)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("AudioSwitch 环境检查");
            report.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("程序: " + AppEnvironment.ResolveExecutablePath());
            report.AppendLine("权限级别: " + IntegrityInspector.Describe());
            report.AppendLine("沙箱受限: " + (IntegrityInspector.IsRestrictedForShellIntegration() ? "是" : "否"));
            report.AppendLine("AppContainer: " + (IntegrityInspector.IsRunningInAppContainer() ? "是" : "否"));
            report.AppendLine("任务栏窗口: " + (NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero
                ? "存在" : "未找到"));
            report.AppendLine("托盘注册能力: " + TrayRegistrationProbe.Describe());
            AppendDpiReport(report);
            report.AppendLine("日志文件: " + logger.LogFilePath);

            IThemeProvider theme = null;
            try
            {
                theme = new SystemThemeProvider(logger);
                report.AppendLine("系统主题: " + theme.GetTheme());
            }
            catch (Exception ex)
            {
                report.AppendLine("系统主题读取失败: " + ex.Message);
            }
            finally
            {
                if (theme != null)
                {
                    theme.Dispose();
                }
            }

            WriteReport(options.OutputPath, report.ToString(), logger);
        }

        private static void AppendDpiReport(StringBuilder report)
        {
            uint dpi = NativeMethods.GetDpiForSystem();
            int percent = dpi > 0 ? (int)Math.Round(dpi / 96.0 * 100.0) : 100;
            int monitors = NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS);
            report.AppendLine("DPI: 系统 " + dpi + " (" + percent + "%)，托盘图标 "
                + NativeMethods.GetSmallIconSize() + "px，显示器数量 " + monitors
                + "；manifest 声明 PerMonitorV2");
        }

        private static void AppendEndpointReport(StringBuilder report, IAudioDeviceService audio,
            IAudioVolumeService volume, bool switchTest, bool volumeTest)
        {
            try
            {
                int knownCount = audio.GetKnownDeviceIds().Count;
                report.AppendLine("已安装音频设备（含禁用/未插入状态）数量: " + knownCount);
            }
            catch (Exception ex)
            {
                report.AppendLine("已安装音频设备枚举失败: " + ex.Message);
            }

            foreach (AudioDataFlow flow in new[] { AudioDataFlow.Playback, AudioDataFlow.Recording })
            {
                string flowName = flow == AudioDataFlow.Playback ? "播放" : "录音";
                report.AppendLine("[" + flowName + "设备] 当前默认: " + (audio.GetDefaultEndpointId(flow) ?? "(无)"));
                report.AppendLine("  默认    形态  类型          音量  静音  名称 / ID");

                int count = 0;
                foreach (AudioEndpoint endpoint in audio.GetEndpoints())
                {
                    if (endpoint.Flow != flow)
                    {
                        continue;
                    }

                    count++;
                    AudioVolumeState volumeState;
                    bool hasVolume = volume.TryGetState(endpoint.Id, out volumeState);
                    report.AppendLine(string.Format("  {0,-6} {1,-4} {2,-12} {3,-5} {4,-5} {5}  [{6}]",
                        endpoint.IsDefault ? "是" : "否",
                        (int)endpoint.FormFactor,
                        endpoint.TypeLabel,
                        hasVolume ? volumeState.Percent + "%" : "n/a",
                        hasVolume ? (volumeState.Muted ? "是" : "否") : "n/a",
                        endpoint.Name,
                        endpoint.Id));
                }

                if (count == 0)
                {
                    report.AppendLine("  （没有活动设备）");
                }

                report.AppendLine();
            }

            if (switchTest)
            {
                string playbackDefault = audio.GetDefaultEndpointId(AudioDataFlow.Playback);
                report.AppendLine("切换接口测试（用当前默认设备回写，不会实际改变设备）:");
                if (string.IsNullOrEmpty(playbackDefault))
                {
                    report.AppendLine("  跳过：没有默认播放设备。");
                }
                else
                {
                    try
                    {
                        audio.SetDefaultDevice(playbackDefault);
                        string after = audio.GetDefaultEndpointId(AudioDataFlow.Playback);
                        bool unchanged = string.Equals(after, playbackDefault, StringComparison.OrdinalIgnoreCase);
                        report.AppendLine("  SetDefaultEndpoint 调用成功；回读默认: " + (after ?? "(无)"));
                        report.AppendLine("  结果: " + (unchanged ? "通过（默认设备未发生变化）" : "警告（默认设备发生变化）"));
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine("  SetDefaultEndpoint 调用失败: " + ex.Message);
                    }
                }

                report.AppendLine();
            }

            if (volumeTest)
            {
                report.AppendLine("音量/静音接口测试（读-回写-再读，不会改变实际音量）:");
                foreach (AudioDataFlow flow in new[] { AudioDataFlow.Playback, AudioDataFlow.Recording })
                {
                    string flowName = flow == AudioDataFlow.Playback ? "播放" : "录音";
                    AudioEndpoint target = null;
                    foreach (AudioEndpoint endpoint in audio.GetEndpoints())
                    {
                        if (endpoint.Flow != flow)
                        {
                            continue;
                        }

                        target = endpoint;
                        if (endpoint.IsDefault)
                        {
                            break;
                        }
                    }

                    if (target == null)
                    {
                        report.AppendLine("  " + flowName + "：没有活动设备，跳过。");
                        continue;
                    }

                    AudioVolumeState state;
                    if (!volume.TryGetState(target.Id, out state))
                    {
                        report.AppendLine("  " + flowName + "：读取音量失败 (" + target.Name + ")");
                        continue;
                    }

                    bool wroteVolume = volume.TrySetVolume(target.Id, state.Volume);
                    bool wroteMute = volume.TrySetMute(target.Id, state.Muted);

                    AudioVolumeState after;
                    bool readBack = volume.TryGetState(target.Id, out after);
                    report.AppendLine(string.Format("  {0}：{1}，音量={2}% 静音={3}；回写={4}；回读={5}",
                        flowName,
                        target.Name,
                        state.Percent,
                        state.Muted ? "是" : "否",
                        wroteVolume && wroteMute ? "成功" : "失败",
                        readBack
                            ? string.Format("音量={0}% 静音={1}", after.Percent, after.Muted ? "是" : "否")
                            : "失败"));
                }

                report.AppendLine();
            }
        }

        // ------------------------------------------------------------------ UI preview modes

        private static void RunPreview(CommandLineOptions options, ILogger logger)
        {
            IAudioDeviceService audio = null;
            IAudioVolumeService volume = null;
            IThemeProvider theme = null;
            FlyoutWindow window = null;

            try
            {
                audio = CreateAudioService(options.Demo, logger);
                volume = CreateVolumeService(options.Demo, logger);
                theme = new SystemThemeProvider(logger);
                FlyoutViewModel viewModel = new FlyoutViewModel(audio, volume, new DeviceVisibilityService(new InMemoryAppSettings(), logger));
                window = CreateFlyoutWindow(viewModel, theme, logger, options.Demo, options.AppsPage);
                window.RenderPreviewToPng(options.OutputPath);
                logger.Info("已生成界面预览: " + options.OutputPath);
            }
            catch (Exception ex)
            {
                logger.Error("生成界面预览失败。", ex);
            }
            finally
            {
                if (window != null && window.IsVisible)
                {
                    SafeClose(window);
                }

                if (theme != null)
                {
                    theme.Dispose();
                }

                if (volume != null)
                {
                    volume.Dispose();
                }

                if (audio != null)
                {
                    audio.Dispose();
                }
            }
        }

        private static void RunCapture(CommandLineOptions options, ILogger logger)
        {
            AudioDeviceService audio = null;
            AudioVolumeService volume = null;
            IThemeProvider theme = null;

            try
            {
                audio = new AudioDeviceService(logger);
                volume = new AudioVolumeService(logger);
                theme = new SystemThemeProvider(logger);
                FlyoutViewModel viewModel = new FlyoutViewModel(audio, volume, new DeviceVisibilityService(new InMemoryAppSettings(), logger));
                FlyoutWindow window = CreateFlyoutWindow(viewModel, theme, logger, options.Demo, options.AppsPage);
                window.SuspendAutoHide = true;
                window.IsHitTestVisible = false;
                window.ShowNearTray();

                DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(900)
                };
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        if (!window.IsVisible)
                        {
                            window.ShowNearTray();
                        }

                        window.CaptureWindowToPng(options.OutputPath);
                        logger.Info("已保存面板截图: " + options.OutputPath);
                    }
                    catch (Exception ex)
                    {
                        logger.Error("保存面板截图失败。", ex);
                    }

                    FinishDeferredMode(window, theme, audio, volume);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                logger.Error("截图模式启动失败。", ex);
                FinishDeferredMode(null, theme, audio, volume);
            }
        }

        private static void RunScreenshot(CommandLineOptions options, ILogger logger)
        {
            IAudioDeviceService audio = null;
            IAudioVolumeService volume = null;
            IThemeProvider theme = null;
            Window backdrop = null;
            FlyoutWindow window = null;

            try
            {
                audio = CreateAudioService(options.Demo, logger);
                volume = CreateVolumeService(options.Demo, logger);
                theme = new SystemThemeProvider(logger);
                FlyoutViewModel viewModel = new FlyoutViewModel(audio, volume, new DeviceVisibilityService(new InMemoryAppSettings(), logger));
                window = CreateFlyoutWindow(viewModel, theme, logger, options.Demo, options.AppsPage);
                window.SuspendAutoHide = true;
                window.IsHitTestVisible = false;

                backdrop = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    AllowsTransparency = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                    Left = 60,
                    Top = 40,
                    Width = 900,
                    Height = 800
                };
                backdrop.Show();

                window.ShowAt(backdrop.Left + 260, backdrop.Top + 110);

                DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(1000)
                };
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        CaptureWindow(backdrop, options.OutputPath);
                        logger.Info("已保存带阴影的界面截图: " + options.OutputPath);
                    }
                    catch (Exception ex)
                    {
                        logger.Error("保存界面截图失败。", ex);
                    }

                    if (window != null)
                    {
                        SafeClose(window);
                    }

                    if (backdrop != null)
                    {
                        SafeClose(backdrop);
                    }

                    if (theme != null)
                    {
                        theme.Dispose();
                    }

                    if (volume != null)
                    {
                        volume.Dispose();
                    }

                    if (audio != null)
                    {
                        audio.Dispose();
                    }

                    ShutdownApplication();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                logger.Error("截图模式启动失败。", ex);
                if (window != null)
                {
                    SafeClose(window);
                }

                if (backdrop != null)
                {
                    SafeClose(backdrop);
                }

                if (theme != null)
                {
                    theme.Dispose();
                }

                if (volume != null)
                {
                    volume.Dispose();
                }

                if (audio != null)
                {
                    audio.Dispose();
                }

                ShutdownApplication();
            }
        }

        private static void RunVerify(CommandLineOptions options, ILogger logger)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("AudioSwitch 验证报告");
            report.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("程序: " + AppEnvironment.ResolveExecutablePath());
            report.AppendLine("权限级别: " + IntegrityInspector.Describe());
            AppendDpiReport(report);
            report.AppendLine("任务栏窗口: " + (NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero
                ? "存在" : "未找到"));
            report.AppendLine("托盘注册能力: " + TrayRegistrationProbe.Describe());
            report.AppendLine();

            AudioDeviceService audio = null;
            AudioVolumeService volume = null;
            IThemeProvider theme = null;

            try
            {
                audio = new AudioDeviceService(logger);
                volume = new AudioVolumeService(logger);
                theme = new SystemThemeProvider(logger);
                report.AppendLine("系统主题: " + theme.GetTheme());
                report.AppendLine("默认播放设备: " + (audio.GetDefaultEndpointId(AudioDataFlow.Playback) ?? "(无)"));
                report.AppendLine("默认录音设备: " + (audio.GetDefaultEndpointId(AudioDataFlow.Recording) ?? "(无)"));
                report.AppendLine("默认播放音量: "
                    + DescribeVolume(volume, audio.GetDefaultEndpointId(AudioDataFlow.Playback)));
                report.AppendLine("默认录音音量: "
                    + DescribeVolume(volume, audio.GetDefaultEndpointId(AudioDataFlow.Recording)));
                report.AppendLine("开机启动测试: " + TestStartupRegistration(
                    new RegistryStartupRegistration(AppEnvironment.ResolveExecutablePath(), logger)));
                report.AppendLine();

                FlyoutViewModel viewModel = new FlyoutViewModel(audio, volume, new DeviceVisibilityService(new InMemoryAppSettings(), logger));
                FlyoutWindow window = CreateFlyoutWindow(viewModel, theme, logger, options.Demo, options.AppsPage);
                window.SuspendAutoHide = true;
                window.IsHitTestVisible = false;
                window.ShowNearTray();
                report.AppendLine(window.DescribeWindow());

                DispatcherTimer hideTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(700)
                };
                hideTimer.Tick += delegate
                {
                    hideTimer.Stop();
                    window.ToggleNearTray();

                    DispatcherTimer afterHideTimer = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(350)
                    };
                    afterHideTimer.Tick += delegate
                    {
                        afterHideTimer.Stop();
                        report.AppendLine("再次点击托盘后: "
                            + (window.IsVisible ? "仍可见（异常）" : "已隐藏（正常）"));
                        report.AppendLine("结果: 验证流程完成");

                        FinishDeferredMode(window, theme, audio, volume);
                        WriteReport(options.OutputPath, report.ToString(), logger);
                    };
                    afterHideTimer.Start();
                };
                hideTimer.Start();
            }
            catch (Exception ex)
            {
                logger.Error("验证模式失败。", ex);
                report.AppendLine("验证过程失败: " + ex);
                FinishDeferredMode(null, theme, audio, volume);
                WriteReport(options.OutputPath, report.ToString(), logger);
            }
        }

        private static string DescribeVolume(IAudioVolumeService volume, string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return "(无默认设备)";
            }

            AudioVolumeState state;
            if (!volume.TryGetState(deviceId, out state))
            {
                return "(读取失败)";
            }

            return state.Percent + "%" + (state.Muted ? "，已静音" : string.Empty);
        }

        private static string TestStartupRegistration(IStartupRegistration startup)
        {
            try
            {
                bool original = startup.IsEnabled;
                startup.SetEnabled(true);
                bool enabled = startup.IsEnabled;
                startup.SetEnabled(false);
                bool disabled = startup.IsEnabled;

                if (original)
                {
                    startup.SetEnabled(true);
                }

                return string.Format("写入={0}；删除后={1}；原始={2}",
                    enabled ? "成功" : "失败",
                    disabled ? "仍存在（异常）" : "已移除",
                    original ? "已启用" : "未启用");
            }
            catch (Exception ex)
            {
                return "失败: " + ex.Message;
            }
        }

        private static void RunUiTest(CommandLineOptions options, ILogger logger)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("AudioSwitch 界面交互测试");
            report.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine();

            DemoAudioDeviceService audio = new DemoAudioDeviceService();
            DemoAudioVolumeService volume = new DemoAudioVolumeService();
            InMemoryAppSettings settings = new InMemoryAppSettings();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, logger);
            IThemeProvider theme = null;
            FlyoutWindow window = null;

            try
            {
                theme = new SystemThemeProvider(logger);
                FlyoutViewModel viewModel = new FlyoutViewModel(audio, volume, visibility);
                AppVolumeViewModel appVolume = new AppVolumeViewModel(new DemoAudioSessionService());
                window = new FlyoutWindow(viewModel, appVolume, theme, settings, logger);
                window.SuspendAutoHide = true;
                window.ShowAt(300, 200);

                report.AppendLine("设备行数: " + window.RowCount);
                report.AppendLine("初始默认播放: " + (audio.GetDefaultEndpointId(AudioDataFlow.Playback) ?? "(无)"));
                report.AppendLine("初始行状态: " + window.DescribeRows());
                report.AppendLine();

                report.AppendLine("[步骤 0] 右键隐藏设备 / 托盘菜单取消所有隐藏");
                int rowsBeforeHide = window.RowCount;
                string hideId = window.GetRowId(0);
                bool contextMenuOk = window.HasHideContextMenu(0);
                bool volumeTooltipRemoved = !window.HasVolumeTooltips(0);
                report.AppendLine("第 1 行右键菜单: " + (contextMenuOk ? "存在「隐藏此设备」" : "缺失"));
                report.AppendLine("音量区域悬停说明: " + (volumeTooltipRemoved ? "已移除" : "仍存在"));
                report.AppendLine("菜单主题样式: " + window.DescribeDeviceContextMenu(0));
                bool hideOk = window.HideRowByIndex(0);
                int rowsAfterHide = window.RowCount;
                bool hiddenStateOk = visibility.IsHidden(hideId)
                    && rowsAfterHide == rowsBeforeHide - 1
                    && !string.Equals(window.GetRowId(0), hideId, StringComparison.OrdinalIgnoreCase);
                DeviceVisibilityService reloadedVisibility = new DeviceVisibilityService(settings, logger);
                bool persistedOk = reloadedVisibility.IsHidden(hideId);
                reloadedVisibility.Dispose();
                report.AppendLine("右键隐藏第 1 行后行数: " + rowsAfterHide + "（原 " + rowsBeforeHide + "）"
                    + "，隐藏记录: " + (visibility.HasHiddenDevices ? "有" : "无")
                    + "，当前首行: " + (window.GetRowId(0) ?? "(无)"));
                report.AppendLine("隐藏状态/持久化: " + (hiddenStateOk ? "正确" : "错误")
                    + " / " + (persistedOk ? "可恢复" : "未保存"));
                bool restoreOk = window.RestoreAllHiddenDevices();
                bool restoredOk = window.RowCount == rowsBeforeHide && !visibility.HasHiddenDevices;
                report.AppendLine("取消所有隐藏后行数: " + window.RowCount
                    + "，隐藏记录: " + (visibility.HasHiddenDevices ? "仍有" : "已清空")
                    + (restoreOk && restoredOk ? "（正确）" : "（错误）"));
                report.AppendLine();

                report.AppendLine("[步骤 0.5] 页面切换：设备页 <-> 应用音量页，并记住选项");
                bool switchToApps = window.SwitchPageForTest(true);
                int appRowCount = window.AppRowCount;
                bool appsListed = appRowCount > 0;
                bool pageSettingSaved = string.Equals(settings.GetString("FlyoutPage", "devices"),
                    "apps", StringComparison.OrdinalIgnoreCase);
                bool switchBackOk = !window.SwitchPageForTest(false);
                bool devicesRestored = !window.IsAppsPage && window.RowCount == rowsBeforeHide;
                report.AppendLine("切换到应用音量页: " + (switchToApps ? "成功" : "失败")
                    + "，应用行数: " + appRowCount
                    + "，设置已保存: " + (pageSettingSaved ? "是" : "否"));
                report.AppendLine("切回设备页: " + (switchBackOk && devicesRestored ? "正确" : "错误"));
                report.AppendLine();

                bool hideFeatureOk = contextMenuOk && volumeTooltipRemoved && hideOk
                    && hiddenStateOk && persistedOk && restoreOk && restoredOk
                    && switchToApps && appsListed && pageSettingSaved && switchBackOk && devicesRestored;

                DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                timer.Tick += delegate
                {
                    timer.Stop();

                    string firstId = window.GetRowId(0);
                    report.AppendLine("[步骤 1] 模拟点击第 2 行（非默认设备）");
                    window.SimulateSelectRow(1);

                    DispatcherTimer afterSelect = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(350)
                    };
                    afterSelect.Tick += delegate
                    {
                        afterSelect.Stop();
                        report.AppendLine("点击后面板可见: "
                            + (window.IsVisible ? "是（正确：未自动关闭）" : "否（错误：被自动关闭）"));
                        report.AppendLine("点击后默认播放: "
                            + (audio.GetDefaultEndpointId(AudioDataFlow.Playback) ?? "(无)"));
                        report.AppendLine("点击后行状态: " + window.DescribeRows());
                        report.AppendLine("电平表（示例电平）: " + window.DescribeMeters());
                        report.AppendLine();

                        report.AppendLine("[步骤 2] 模拟点击第 1 行的静音按钮");
                        window.SimulateMuteRow(0);
                        AudioVolumeState muteState;
                        volume.TryGetState(firstId, out muteState);
                        report.AppendLine("第 1 行静音状态: "
                            + (muteState != null ? (muteState.Muted ? "已静音（正确）" : "未静音（错误）") : "读取失败"));
                        report.AppendLine();

                        report.AppendLine("[步骤 3] 模拟把第 3 行音量拖动到 25%");
                        string thirdId = window.GetRowId(2);
                        window.SimulateVolumeRow(2, 25);
                        AudioVolumeState volumeState;
                        volume.TryGetState(thirdId, out volumeState);
                        report.AppendLine("第 3 行音量: "
                            + (volumeState != null ? volumeState.Percent + "%" : "读取失败"));
                        report.AppendLine();

                        report.AppendLine("[步骤 4] 用真实鼠标输入从音量条任意位置按下并拖到 85%");
                        int dragStartedCount = 0;
                        int dragCompletedCount = 0;
                        bool dragCompletedChanged = false;
                        window.VolumeDragStarted += delegate { dragStartedCount++; };
                        window.VolumeDragCompleted += delegate(string id, bool changed)
                        {
                            dragCompletedCount++;
                            dragCompletedChanged = changed;
                        };
                        Rect sliderBounds = window.GetSliderScreenBounds(2);
                        string dragId = window.GetRowId(2);
                        if (sliderBounds.IsEmpty || sliderBounds.Width < 40)
                        {
                            report.AppendLine("跳过：无法获取滑块屏幕位置。");
                            FinishUiTest(report, window, theme, audio, volume, options, logger,
                                muteState, volumeState, null, hideFeatureOk);
                            return;
                        }

                        report.AppendLine(string.Format("第 3 行滑块屏幕范围: ({0},{1}) 宽 {2:0} 高 {3:0}",
                            sliderBounds.Left, sliderBounds.Top, sliderBounds.Width, sliderBounds.Height));

                        NativeMethods.POINT originalCursor;
                        NativeMethods.GetCursorPos(out originalCursor);

                        bool canSimulateInput = NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
                        if (!canSimulateInput)
                        {
                            report.AppendLine("本会话禁止模拟鼠标输入（SetCursorPos 失败），"
                                + "改用同一套坐标映射逻辑验证拖动：从 15% 处按下并拖到 85%");
                            string simulatedId = window.GetRowId(2);
                            AudioVolumeState simulatedState;
                            window.SimulateSliderDrag(2, 0.15, 0.85);
                            volume.TryGetState(simulatedId, out simulatedState);
                            report.AppendLine("拖动后第 3 行音量: "
                                + (simulatedState != null
                                    ? simulatedState.Percent + "%（期望约 85%）"
                                    : "读取失败"));
                            bool simulatedPanelMuteOk = CheckPanelMutePersistence(window, report);
                            FinishUiTest(report, window, theme, audio, volume, options, logger,
                                muteState, volumeState, simulatedState, hideFeatureOk, true,
                                simulatedPanelMuteOk);
                            return;
                        }

                        int centerY = (int)Math.Round(sliderBounds.Top + sliderBounds.Height / 2.0);
                        int startX = (int)Math.Round(sliderBounds.Left + sliderBounds.Width * 0.15);
                        int middleX = (int)Math.Round(sliderBounds.Left + sliderBounds.Width * 0.50);
                        int endX = (int)Math.Round(sliderBounds.Left + sliderBounds.Width * 0.85);

                        int dragStep = 0;
                        DispatcherTimer dragTimer = new DispatcherTimer(DispatcherPriority.Background)
                        {
                            Interval = TimeSpan.FromMilliseconds(90)
                        };
                        dragTimer.Tick += delegate
                        {
                            dragStep++;
                            switch (dragStep)
                            {
                                case 1:
                                    NativeMethods.SetCursorPos(startX, centerY);
                                    report.AppendLine("  步1 移动光标: " + window.DescribeSliderState(2));
                                    break;
                                case 2:
                                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                                    report.AppendLine("  步2 按下: " + window.DescribeSliderState(2));
                                    break;
                                case 3:
                                    NativeMethods.SetCursorPos(middleX, centerY);
                                    report.AppendLine("  步3 拖到中点: " + window.DescribeSliderState(2));
                                    break;
                                case 4:
                                    NativeMethods.SetCursorPos(endX, centerY);
                                    report.AppendLine("  步4 拖到终点: " + window.DescribeSliderState(2));
                                    break;
                                case 5:
                                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                                    report.AppendLine("  步5 松开: " + window.DescribeSliderState(2)
                                        + "，拖动事件反馈: 开始=" + dragStartedCount
                                        + "，完成=" + dragCompletedCount
                                        + "，有变化=" + dragCompletedChanged);
                                    break;
                                default:
                                    dragTimer.Stop();
                                    NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
                                    AudioVolumeState dragState;
                                    volume.TryGetState(dragId, out dragState);
                                    report.AppendLine("拖动后第 3 行音量: "
                                        + (dragState != null ? dragState.Percent + "%" : "读取失败"));
                                    bool dragFeedbackOk = dragStartedCount == 1
                                        && dragCompletedCount == 1 && dragCompletedChanged;
                                    report.AppendLine("音量拖动事件回调: 开始=" + dragStartedCount
                                        + "，完成=" + dragCompletedCount
                                        + "，有变化=" + dragCompletedChanged
                                        + "（期望 1/1/True，松开才提示一次）");
                                    bool panelMutePersistenceOk = CheckPanelMutePersistence(window, report);
                                    FinishUiTest(report, window, theme, audio, volume, options, logger,
                                        muteState, volumeState, dragState, hideFeatureOk, dragFeedbackOk,
                                        panelMutePersistenceOk);
                                    break;
                            }
                        };
                        dragTimer.Start();
                    };
                    afterSelect.Start();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                logger.Error("界面交互测试失败。", ex);
                report.AppendLine("测试失败: " + ex);
                FinishDeferredMode(window, theme, audio, volume);
                WriteReport(options.OutputPath, report.ToString(), logger);
            }
        }

        private static bool CheckPanelMutePersistence(FlyoutWindow window, StringBuilder report)
        {
            if (window.RowCount == 0)
            {
                report.AppendLine("跳过静音图标保持测试：没有设备行。");
                return false;
            }

            report.AppendLine("[步骤 5] 0% 手动静音后关闭并重新打开面板");
            window.SimulateMuteRow(0); // 先解除第 2 步造成的静音
            window.SimulateVolumeRow(0, 0);
            window.SimulateMuteRow(0); // 在 0% 音量下手动静音

            string before = window.GetRowMuteGlyph(0);
            window.HideFlyout();
            window.ShowAt(300, 200);
            string after = window.GetRowMuteGlyph(0);
            bool ok = string.Equals(before, VolumeGlyphs.Muted, StringComparison.Ordinal)
                && string.Equals(after, VolumeGlyphs.Muted, StringComparison.Ordinal);
            report.AppendLine("0% 手动静音图标: 重新打开前=" + DescribeGlyph(before)
                + "，重新打开后=" + DescribeGlyph(after)
                + "（期望均为 扬声器+X）");
            return ok;
        }

        private static string DescribeGlyph(string glyph)
        {
            if (string.Equals(glyph, VolumeGlyphs.Muted, StringComparison.Ordinal))
            {
                return "扬声器+X";
            }

            if (string.Equals(glyph, VolumeGlyphs.Zero, StringComparison.Ordinal))
            {
                return "扬声器无X";
            }

            return string.IsNullOrEmpty(glyph) ? "(无)" : glyph;
        }

        private static void FinishUiTest(StringBuilder report, FlyoutWindow window, IThemeProvider theme,
            IAudioDeviceService audio, IAudioVolumeService volume, CommandLineOptions options, ILogger logger,
            AudioVolumeState muteState, AudioVolumeState volumeState, AudioVolumeState dragState,
            bool hideFeatureOk, bool volumeDragFeedbackOk = true, bool panelMutePersistenceOk = true)
        {
            report.AppendLine();
            report.AppendLine("最终行状态: " + window.DescribeRows());

            bool muteOk = muteState != null && muteState.Muted;
            bool volumeOk = volumeState != null && volumeState.Percent == 25;
            bool dragOk = dragState == null || (dragState.Percent >= 70 && dragState.Percent <= 100);
            report.AppendLine("结果: "
                + (window.IsVisible && muteOk && volumeOk && dragOk && hideFeatureOk
                    && volumeDragFeedbackOk && panelMutePersistenceOk
                    ? "通过" : "请检查上方步骤"));

            FinishDeferredMode(window, theme, audio, volume);
            WriteReport(options.OutputPath, report.ToString(), logger);
        }

        private static void RunTrayTest(CommandLineOptions options, ILogger logger)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("AudioSwitch 托盘图标动态测试");
            report.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine();

            FakeTrayIconHost host = new FakeTrayIconHost();
            DemoAudioDeviceService audio = new DemoAudioDeviceService();
            DemoAudioVolumeService volume = new DemoAudioVolumeService();
            FakeThemeProvider theme = new FakeThemeProvider(new ThemeInfo(true, Color.FromRgb(0, 120, 215)));
            InMemoryAppSettings settings = new InMemoryAppSettings();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, logger);
            FakeStartupRegistration startup = new FakeStartupRegistration();
            FakeShellLauncher shell = new FakeShellLauncher();
            TrayPresenter presenter = null;

            try
            {
                presenter = new TrayPresenter(Dispatcher.CurrentDispatcher, host, audio, volume, theme,
                    settings, visibility, startup, shell, logger, delegate { return null; });
                presenter.Start();

                report.AppendLine("初始: 提示='" + host.LastTooltip + "'，图标更新次数=" + host.IconUpdateCount);
                bool initialOk = host.IconUpdateCount > 0
                    && host.LastTooltip != null && host.LastTooltip.Contains("65%");

                volume.TrySetVolume("demo-playback-1", 0.42f);
                presenter.RefreshNow();
                report.AppendLine("把主输出音量设为 42%: 提示='" + host.LastTooltip
                    + "'，图标更新次数=" + host.IconUpdateCount);
                bool volumeOk = host.IconUpdateCount >= 2
                    && host.LastTooltip != null && host.LastTooltip.Contains("42%");

                volume.TrySetMute("demo-playback-1", true);
                presenter.RefreshNow();
                report.AppendLine("静音后: 提示='" + host.LastTooltip + "'");
                bool muteOk = host.LastTooltip != null && host.LastTooltip.Contains("已静音");

                volume.TrySetMute("demo-playback-1", false);
                volume.TrySetVolume("demo-playback-1", 1.0f);
                presenter.RefreshNow();
                report.AppendLine("取消静音并设为 100%: 提示='" + host.LastTooltip + "'");
                bool fullOk = host.LastTooltip != null && host.LastTooltip.Contains("100%");

                int beforeTheme = host.IconUpdateCount;
                theme.SetTheme(new ThemeInfo(false, Color.FromRgb(0, 120, 215)));
                report.AppendLine("切换深色任务栏主题: 图标更新次数 " + beforeTheme + " -> "
                    + host.IconUpdateCount);
                bool themeOk = host.IconUpdateCount > beforeTheme;

                volume.SimulateExternalState("demo-playback-1", 0f, true);
                presenter.RefreshNow();
                bool autoMuteAtZeroOk = host.LastTooltip != null
                    && host.LastTooltip.Contains("0%")
                    && !host.LastTooltip.Contains("已静音");
                report.AppendLine("0% 自动静音（无手动点击）提示: '" + host.LastTooltip + "'"
                    + (autoMuteAtZeroOk ? "（正确：不算静音）" : "（错误）"));

                volume.SimulateExternalState("demo-playback-1", 0f, false);
                presenter.RefreshNow();
                volume.TrySetMute("demo-playback-1", true);
                presenter.RefreshNow();
                bool explicitMuteAtZeroOk = host.LastTooltip != null
                    && host.LastTooltip.Contains("已静音");
                report.AppendLine("0% 手动点击静音提示: '" + host.LastTooltip + "'"
                    + (explicitMuteAtZeroOk ? "（正确：显示已静音）" : "（错误）"));
                volume.TrySetMute("demo-playback-1", false);
                presenter.RefreshNow();

                visibility.Hide("demo-playback-2");
                bool hiddenBeforeRestore = visibility.HasHiddenDevices;
                presenter.ContextMenuForTest.ClickRestoreHiddenItemForTest();
                bool restoreMenuOk = hiddenBeforeRestore && !visibility.HasHiddenDevices;
                report.AppendLine("托盘菜单「取消所有隐藏」: "
                    + (restoreMenuOk ? "有效（隐藏记录已清空）" : "失败"));

                report.AppendLine();
                report.AppendLine("结果: "
                    + (initialOk && volumeOk && muteOk && fullOk && themeOk
                        && autoMuteAtZeroOk && explicitMuteAtZeroOk && restoreMenuOk
                        ? "通过" : "失败"));
                WriteReport(options.OutputPath, report.ToString(), logger);
            }
            catch (Exception ex)
            {
                logger.Error("托盘图标动态测试失败。", ex);
                report.AppendLine("测试失败: " + ex);
                WriteReport(options.OutputPath, report.ToString(), logger);
            }
            finally
            {
                if (presenter != null)
                {
                    presenter.Dispose();
                }

                audio.Dispose();
                volume.Dispose();
                theme.Dispose();
                visibility.Dispose();
            }
        }

        private static void RunAppsTest(CommandLineOptions options, ILogger logger)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("AudioSwitch 应用音量接口测试");
            report.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine();

            AudioSessionService sessions = null;
            try
            {
                sessions = new AudioSessionService(logger);
                IList<AudioSessionInfo> before = sessions.GetSessions();
                report.AppendLine("应用会话数量: " + before.Count);
                foreach (AudioSessionInfo session in before)
                {
                    report.AppendLine(string.Format("  {0} | pid={1} | 音量={2}% | 静音={3}",
                        session.DisplayName, session.ProcessId, session.Percent,
                        session.Muted ? "是" : "否"));
                }

                AudioSessionInfo target = null;
                foreach (AudioSessionInfo session in before)
                {
                    if (!session.IsSystemSounds)
                    {
                        target = session;
                        break;
                    }
                }

                SoundPlayer testPlayer = null;
                if (target == null)
                {
                    // Play a short Windows WAV so this process creates a real app session,
                    // then test volume/mute write + readback through the same service.
                    try
                    {
                        string wav = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                            "Media", "Windows Ding.wav");
                        if (System.IO.File.Exists(wav))
                        {
                            testPlayer = new SoundPlayer(wav);
                            testPlayer.Play();
                            Thread.Sleep(500);
                            before = sessions.GetSessions();
                            report.AppendLine("已播放测试音频以创建应用会话（会话数: " + before.Count + "）。");
                            foreach (AudioSessionInfo session in before)
                            {
                                if (!session.IsSystemSounds)
                                {
                                    target = session;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception playEx)
                    {
                        report.AppendLine("创建测试会话失败: " + playEx.Message);
                    }
                }

                if (target == null)
                {
                    report.AppendLine("没有可测试的应用会话（请播放一段音频后重试）。");
                    report.AppendLine();
                    report.AppendLine("结果: 跳过");
                    if (testPlayer != null)
                    {
                        try { testPlayer.Stop(); } catch { }
                        testPlayer.Dispose();
                        testPlayer = null;
                    }
                }
                else
                {
                    report.AppendLine();
                    report.AppendLine("测试应用: " + target.DisplayName + " (" + target.Key + ")");
                    bool wroteVolume = sessions.TrySetVolume(target.Key, 0.37f);
                    AudioSessionInfo afterVolume = FindSession(sessions.GetSessions(), target.Key);
                    bool volumeReadBack = afterVolume != null && afterVolume.Percent == 37;
                    report.AppendLine("写音量 37%: 调用=" + (wroteVolume ? "成功" : "失败")
                        + "，回读=" + (afterVolume != null ? afterVolume.Percent + "%" : "无"));

                    bool wroteMute = sessions.TrySetMute(target.Key, true);
                    AudioSessionInfo afterMute = FindSession(sessions.GetSessions(), target.Key);
                    bool muteReadBack = afterMute != null && afterMute.Muted;
                    report.AppendLine("写静音: 调用=" + (wroteMute ? "成功" : "失败")
                        + "，回读=" + (afterMute != null && afterMute.Muted ? "已静音" : "未静音"));

                    float peak;
                    bool peakOk = sessions.TryGetPeakValue(target.Key, out peak);
                    report.AppendLine("应用电平读取: " + (peakOk ? "成功，峰值=" + peak.ToString("0.00") : "失败"));

                    sessions.TrySetVolume(target.Key, target.Volume);
                    sessions.TrySetMute(target.Key, target.Muted);
                    report.AppendLine("已恢复原音量/静音状态。");
                    if (testPlayer != null)
                    {
                        try { testPlayer.Stop(); } catch { }
                        testPlayer.Dispose();
                        testPlayer = null;
                    }
                    report.AppendLine();
                    report.AppendLine("结果: " + (wroteVolume && volumeReadBack && wroteMute && muteReadBack && peakOk
                        ? "通过" : "失败"));
                }
            }
            catch (Exception ex)
            {
                logger.Error("应用音量接口测试失败。", ex);
                report.AppendLine("结果: 失败");
                report.AppendLine(ex.ToString());
            }
            finally
            {
                if (sessions != null)
                {
                    sessions.Dispose();
                }
            }

            WriteReport(options.OutputPath, report.ToString(), logger);
        }

        private static AudioSessionInfo FindSession(IList<AudioSessionInfo> sessions, string key)
        {
            foreach (AudioSessionInfo session in sessions)
            {
                if (string.Equals(session.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }

            return null;
        }

        private static void RunTrayIcons(CommandLineOptions options, ILogger logger)
        {
            try
            {
                int[] sizes = { 16, 20, 24, 32 };
                string[] labels = { "静音", "0%", "25%", "50%", "85%", "100%" };
                int[] percents = { 0, 0, 25, 50, 85, 100 };
                bool[] muted = { true, false, false, false, false, false };

                const int scale = 6;
                int cellWidth = 32 * scale + 30;
                int cellHeight = 32 * scale + 34;
                int width = 70 + labels.Length * cellWidth + 20;
                int height = 54 + sizes.Length * cellHeight + 10;

                using (Drawing.Bitmap sheet = new Drawing.Bitmap(width, height,
                    Drawing2D.PixelFormat.Format32bppArgb))
                {
                    using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(sheet))
                    {
                        graphics.Clear(Drawing.Color.FromArgb(255, 243, 243, 243));
                        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                        using (Drawing.Font titleFont = new Drawing.Font("Microsoft YaHei UI", 14,
                            Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
                        using (Drawing.Font labelFont = new Drawing.Font("Microsoft YaHei UI", 12,
                            Drawing.GraphicsUnit.Pixel))
                        using (Drawing.SolidBrush textBrush = new Drawing.SolidBrush(
                            Drawing.Color.FromArgb(255, 70, 70, 70)))
                        {
                            graphics.DrawString("托盘图标：矢量绘制，随主输出音量 / 静音状态变化",
                                titleFont, Drawing.Brushes.Black, 16, 12);

                            int y = 44;
                            foreach (int size in sizes)
                            {
                                graphics.DrawString(size + " px", labelFont, textBrush, 12,
                                    y + size * scale / 2 - 8);

                                for (int i = 0; i < labels.Length; i++)
                                {
                                    int x = 70 + i * cellWidth;
                                    string glyph = VolumeGlyphs.ForVolume(percents[i], muted[i]);
                                    using (Drawing.Bitmap icon = TrayIconArtwork.RenderGlyphBitmap(
                                        glyph, size, Drawing.Color.Black))
                                    {
                                        graphics.InterpolationMode =
                                            System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                                        graphics.PixelOffsetMode =
                                            System.Drawing.Drawing2D.PixelOffsetMode.Half;
                                        graphics.DrawImage(icon, x, y, size * scale, size * scale);
                                    }

                                    graphics.DrawString(labels[i], labelFont, textBrush,
                                        x + 2, y + size * scale + 2);
                                }

                                y += cellHeight;
                            }
                        }
                    }

                    sheet.Save(options.OutputPath, Drawing2D.ImageFormat.Png);
                }

                logger.Info("已生成托盘图标预览: " + options.OutputPath);
            }
            catch (Exception ex)
            {
                logger.Error("生成托盘图标预览失败。", ex);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static FlyoutWindow CreateFlyoutWindow(FlyoutViewModel viewModel,
            IThemeProvider theme, ILogger logger, bool demo, bool appsPage)
        {
            IAudioSessionService sessions = demo
                ? (IAudioSessionService)new DemoAudioSessionService()
                : new AudioSessionService(logger);
            AppVolumeViewModel appVolume = new AppVolumeViewModel(sessions);
            InMemoryAppSettings settings = new InMemoryAppSettings();
            FlyoutWindow window = new FlyoutWindow(viewModel, appVolume, theme, settings, logger);
            if (appsPage)
            {
                window.ForceAppsPage(true);
            }

            return window;
        }

        private static IAudioDeviceService CreateAudioService(bool demo, ILogger logger)
        {
            if (demo)
            {
                return new DemoAudioDeviceService();
            }

            return new AudioDeviceService(logger);
        }

        private static IAudioVolumeService CreateVolumeService(bool demo, ILogger logger)
        {
            if (demo)
            {
                return new DemoAudioVolumeService();
            }

            return new AudioVolumeService(logger);
        }

        private static void FinishDeferredMode(FlyoutWindow window, IThemeProvider theme,
            IAudioDeviceService audio, IAudioVolumeService volume)
        {
            if (window != null)
            {
                SafeClose(window);
            }

            if (theme != null)
            {
                try
                {
                    theme.Dispose();
                }
                catch
                {
                    // Ignore.
                }
            }

            if (volume != null)
            {
                try
                {
                    volume.Dispose();
                }
                catch
                {
                    // Ignore.
                }
            }

            if (audio != null)
            {
                try
                {
                    audio.Dispose();
                }
                catch
                {
                    // Ignore.
                }
            }

            ShutdownApplication();
        }

        private static void CaptureWindow(Window window, string path)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(handle, out rect))
            {
                throw new InvalidOperationException("无法获取背景窗口位置。");
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("背景窗口尺寸无效。");
            }

            using (Drawing.Bitmap bitmap = new Drawing.Bitmap(width, height, Drawing2D.PixelFormat.Format24bppRgb))
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Drawing.Size(width, height),
                    Drawing.CopyPixelOperation.SourceCopy);
                bitmap.Save(path, Drawing2D.ImageFormat.Png);
            }
        }

        private static void SafeClose(Window window)
        {
            try
            {
                FlyoutWindow flyoutWindow = window as FlyoutWindow;
                if (flyoutWindow != null)
                {
                    flyoutWindow.CloseForExit();
                }
                else
                {
                    window.Close();
                }
            }
            catch
            {
                // Ignore teardown failures.
            }
        }

        private static void ShutdownApplication()
        {
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Shutdown();
                }
            }
            catch
            {
                // Ignore.
            }
        }

        private static void WriteReport(string path, string text, ILogger logger)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, text, new UTF8Encoding(true));
                logger.Info("已写入报告: " + path);
            }
            catch (Exception ex)
            {
                logger.Error("写入报告失败: " + path, ex);
            }
        }
    }
}
