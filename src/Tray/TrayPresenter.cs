using System;
using System.Collections.Generic;
using System.Windows.Threading;
using AudioSwitch.Audio;
using AudioSwitch.Platform;
using AudioSwitch.UI;

namespace AudioSwitch.Tray
{
    /// <summary>
    /// Connects the tray icon, the context menu, the audio devices and the flyout UI.
    /// The tray icon is redrawn from vector glyphs whenever the main output volume or
    /// its mute state changes, so it always mirrors the current volume level.
    /// </summary>
    public sealed class TrayPresenter : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly ITrayIconHost _trayHost;
        private readonly IAudioDeviceService _audio;
        private readonly IAudioVolumeService _volume;
        private readonly IThemeProvider _theme;
        private readonly IAppSettings _settings;
        private readonly IDeviceVisibilityService _visibility;
        private readonly IShellLauncher _shell;
        private readonly ILogger _logger;
        private readonly Func<FlyoutWindow> _flyoutFactory;
        private readonly TrayContextMenu _contextMenu;
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _volumeTimer;
        private readonly DispatcherTimer _trayUpdateTimer;
        private readonly DispatcherTimer _soundTimer;

        private TrayIconImage _iconImage;
        private FlyoutWindow _flyoutWindow;
        private bool _disposed;

        private string _defaultPlaybackId;
        private string _defaultPlaybackName;
        private string _defaultRecordingName;
        private int _lastVolumePercent = -1;
        private string _explicitMuteDeviceId;
        private bool _explicitMuteValue;
        private bool _lastMuted;
        private bool _lastHasVolumeState;

        public TrayPresenter(Dispatcher dispatcher, ITrayIconHost trayHost, IAudioDeviceService audio,
            IAudioVolumeService volume, IThemeProvider theme, IAppSettings settings,
            IDeviceVisibilityService visibility, IStartupRegistration startup,
            IShellLauncher shell, ILogger logger, Func<FlyoutWindow> flyoutFactory)
        {
            _dispatcher = dispatcher;
            _trayHost = trayHost;
            _audio = audio;
            _volume = volume;
            _theme = theme;
            _settings = settings;
            _visibility = visibility;
            _shell = shell;
            _logger = logger;
            _flyoutFactory = flyoutFactory;

            _contextMenu = new TrayContextMenu(startup, settings, visibility, logger, ShowErrorBalloon, ShowInfoBalloon,
                ShowFlyout, RefreshNow, shell.OpenSoundSettings, shell.OpenSoundControlPanel, RaiseExitRequested);

            _trayHost.SetContextMenu(_contextMenu.Menu);
            _trayHost.Activated += OnTrayActivated;
            _audio.DevicesChanged += OnAudioDevicesChanged;
            _theme.ThemeChanged += OnThemeChanged;
            _volume.VolumeChanged += OnVolumeChanged;
            _visibility.Changed += OnHiddenDevicesChanged;

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _refreshTimer.Tick += OnRefreshTimerTick;

            _volumeTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _volumeTimer.Tick += OnVolumeTimerTick;

            _trayUpdateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _trayUpdateTimer.Tick += OnTrayUpdateTimerTick;

            _soundTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _soundTimer.Tick += OnSoundTimerTick;
        }

        public event EventHandler ExitRequested;

        internal TrayContextMenu ContextMenuForTest
        {
            get { return _contextMenu; }
        }

        public void Start()
        {
            RefreshEndpointCache();
            _volumeTimer.Start();

            _logger.Info("通知区域图标已创建（矢量绘制，随音量/静音动态更新）。权限: "
                + IntegrityInspector.Describe() + "；主题: " + _theme.GetTheme());
            ScheduleFirstRunHint();
        }

        public void ShowFlyout()
        {
            try
            {
                if (_flyoutWindow == null)
                {
                    _flyoutWindow = _flyoutFactory();
                    _flyoutWindow.SwitchFailed += OnFlyoutSwitchFailed;
                }

                _flyoutWindow.ShowNearTray();
                _logger.Info("已显示音频设备面板。");
            }
            catch (Exception ex)
            {
                _logger.Error("显示音频设备面板失败。", ex);
            }
        }

        public void ToggleFlyout()
        {
            try
            {
                if (_flyoutWindow == null)
                {
                    ShowFlyout();
                    return;
                }

                _flyoutWindow.ToggleNearTray();
            }
            catch (Exception ex)
            {
                _logger.Error("切换音频设备面板失败。", ex);
            }
        }

        public void RefreshNow()
        {
            RefreshEndpointCache();

            if (_flyoutWindow != null && _flyoutWindow.IsVisible)
            {
                _flyoutWindow.RefreshIfVisible();
            }
        }

        private void OnTrayActivated(object sender, EventArgs e)
        {
            ToggleFlyout();
        }

        private void OnHiddenDevicesChanged(object sender, EventArgs e)
        {
            // Queue the refresh at background priority: the visibility service raises
            // Changed while the view model is still being updated, so refreshing the
            // window immediately would rebuild its content twice (and can flicker).
            RunOnUiThread(delegate
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (_flyoutWindow != null && _flyoutWindow.IsVisible)
                    {
                        _flyoutWindow.RefreshIfVisible();
                    }
                }));
            });
        }

        private void OnAudioDevicesChanged(object sender, EventArgs e)
        {
            RunOnUiThread(delegate
            {
                _refreshTimer.Stop();
                _refreshTimer.Start();
            });
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            _refreshTimer.Stop();
            RefreshNow();
        }

        private void OnVolumeChanged(object sender, AudioVolumeChangedEventArgs e)
        {
            // Coalesce the rapid volume events that a slider drag produces.
            RunOnUiThread(delegate
            {
                if (e != null && e.IsMuteChange && !string.IsNullOrEmpty(e.DeviceId))
                {
                    // Track an explicit mute action (as opposed to Windows reporting
                    // muted=true automatically at 0%). Only the default output device
                    // matters for the tray icon.
                    _explicitMuteDeviceId = e.DeviceId;
                    _explicitMuteValue = e.State != null && e.State.Muted;
                }

                _trayUpdateTimer.Stop();
                _trayUpdateTimer.Start();
                TryScheduleVolumeFeedback(e);
            });
        }

        /// <summary>Plays the Windows volume-change beep once the slider has settled.</summary>
        private void TryScheduleVolumeFeedback(AudioVolumeChangedEventArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.DeviceId))
            {
                return;
            }

            if (!string.Equals(args.DeviceId, _defaultPlaybackId, StringComparison.OrdinalIgnoreCase))
            {
                // Only the current main output device: no beep for microphone volumes.
                return;
            }

            if (!_settings.GetFlag("VolumeFeedbackSound", true))
            {
                return;
            }

            _soundTimer.Stop();
            _soundTimer.Start();
        }

        private void OnSoundTimerTick(object sender, EventArgs e)
        {
            _soundTimer.Stop();
            if (_settings.GetFlag("VolumeFeedbackSound", true))
            {
                SystemSoundFeedback.PlayVolumeFeedback();
            }
        }

        private void OnTrayUpdateTimerTick(object sender, EventArgs e)
        {
            _trayUpdateTimer.Stop();
            RefreshTrayVisual(false);
        }

        private void OnVolumeTimerTick(object sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            RefreshTrayVisual(false);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            RunOnUiThread(delegate
            {
                RefreshTrayVisual(true);
                if (_flyoutWindow != null && _flyoutWindow.IsVisible)
                {
                    _flyoutWindow.RefreshIfVisible();
                }
            });
        }

        /// <summary>Re-reads the default endpoints and updates the tray tooltip / icon.</summary>
        private void RefreshEndpointCache()
        {
            string playbackId = null;
            string playbackName = null;
            string recordingName = null;

            try
            {
                foreach (AudioEndpoint endpoint in _audio.GetEndpoints())
                {
                    if (endpoint == null || !endpoint.IsDefault)
                    {
                        continue;
                    }

                    if (endpoint.Flow == AudioDataFlow.Playback && playbackId == null)
                    {
                        playbackId = endpoint.Id;
                        playbackName = endpoint.Name;
                    }
                    else if (endpoint.Flow == AudioDataFlow.Recording && recordingName == null)
                    {
                        recordingName = endpoint.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("刷新默认设备缓存失败。", ex);
            }

            // Prune only against ids that still exist in ANY device state. Using the
            // active list would silently delete the user's hide choice for a temporarily
            // unplugged Bluetooth headset / USB microphone. Skip when enumeration fails
            // or returns nothing (fail-safe against data loss).
            if (_visibility != null)
            {
                try
                {
                    IList<string> knownDeviceIds = _audio.GetKnownDeviceIds();
                    if (knownDeviceIds != null && knownDeviceIds.Count > 0)
                    {
                        _visibility.Prune(knownDeviceIds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("枚举已安装音频设备失败，跳过隐藏设置清理。", ex);
                }
            }

            _defaultPlaybackId = playbackId;
            _defaultPlaybackName = playbackName;
            _defaultRecordingName = recordingName;

            RefreshTrayVisual(true);
        }

        /// <summary>
        /// Redraws the tray icon (Windows volume glyphs: muted / 0 / 1-3 sound waves)
        /// according to the volume of the current main output device.
        /// </summary>
        private void RefreshTrayVisual(bool force)
        {
            int percent = 100;
            bool muted = false;
            bool hasVolumeState = false;

            try
            {
                if (!string.IsNullOrEmpty(_defaultPlaybackId))
                {
                    AudioVolumeState state;
                    if (_volume.TryGetState(_defaultPlaybackId, out state))
                    {
                        percent = state.Percent;
                        muted = state.Muted;
                        hasVolumeState = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("读取主输出设备音量失败。", ex);
            }

            if (!muted)
            {
                _explicitMuteValue = false;
            }

            bool explicitMute = _explicitMuteValue
                && string.Equals(_explicitMuteDeviceId, _defaultPlaybackId,
                    StringComparison.OrdinalIgnoreCase);
            bool displayMuted = VolumeGlyphs.ShouldDisplayMuted(percent, muted, explicitMute);

            if (!force
                && percent == _lastVolumePercent
                && displayMuted == _lastMuted
                && hasVolumeState == _lastHasVolumeState)
            {
                return;
            }

            _lastVolumePercent = percent;
            _lastMuted = displayMuted;
            _lastHasVolumeState = hasVolumeState;

            try
            {
                TrayIconImage newImage = TrayIconArtwork.Create(_theme.GetTheme(), percent,
                    displayMuted, _logger);
                if (newImage != null)
                {
                    _trayHost.SetIcon(newImage.Icon);
                    TrayIconImage oldImage = _iconImage;
                    _iconImage = newImage;

                    if (oldImage != null)
                    {
                        oldImage.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("更新托盘图标失败。", ex);
            }

            UpdateTooltip(percent, displayMuted, hasVolumeState);
        }

        private void UpdateTooltip(int percent, bool muted, bool hasVolumeState)
        {
            try
            {
                string text;
                if (!string.IsNullOrEmpty(_defaultPlaybackName))
                {
                    text = "输出：" + _defaultPlaybackName;
                    if (hasVolumeState)
                    {
                        text += muted ? "  已静音" : "  " + percent + "%";
                    }
                }
                else if (!string.IsNullOrEmpty(_defaultRecordingName))
                {
                    text = "输入：" + _defaultRecordingName;
                }
                else
                {
                    text = "音频设备切换器";
                }

                _trayHost.SetTooltip(text);
            }
            catch (Exception ex)
            {
                _logger.Error("更新托盘提示失败。", ex);
            }
        }

        private void ScheduleFirstRunHint()
        {
            if (_settings.GetFlag("FirstRunHintShown", false))
            {
                return;
            }

            DispatcherTimer hintTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            hintTimer.Tick += delegate
            {
                hintTimer.Stop();
                _settings.SetFlag("FirstRunHintShown", true);
                _trayHost.ShowBalloon("音频设备切换器",
                    "程序已在通知区域运行。左键单击图标打开切换面板；"
                    + "如果任务栏上看不到图标，请点击右下角的 ^ 展开隐藏的图标（可把它拖到任务栏上固定显示）。",
                    TrayNotificationKind.Info);
            };
            hintTimer.Start();
        }

        private void OnFlyoutSwitchFailed(string message)
        {
            ShowErrorBalloon(message);
        }

        private void ShowErrorBalloon(string message)
        {
            _trayHost.ShowBalloon("音频设备切换器", message, TrayNotificationKind.Warning);
        }

        private void ShowInfoBalloon(string message)
        {
            _trayHost.ShowBalloon("音频设备切换器", message, TrayNotificationKind.Info);
        }

        private void RaiseExitRequested()
        {
            EventHandler handler = ExitRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RunOnUiThread(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _dispatcher.BeginInvoke(action);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _refreshTimer.Stop();
            _volumeTimer.Stop();
            _trayUpdateTimer.Stop();
            _soundTimer.Stop();

            try
            {
                _trayHost.Activated -= OnTrayActivated;
                _audio.DevicesChanged -= OnAudioDevicesChanged;
                _theme.ThemeChanged -= OnThemeChanged;
                _volume.VolumeChanged -= OnVolumeChanged;
                _visibility.Changed -= OnHiddenDevicesChanged;
            }
            catch
            {
                // Ignore unsubscribe failures during shutdown.
            }

            if (_flyoutWindow != null)
            {
                try
                {
                    _flyoutWindow.SwitchFailed -= OnFlyoutSwitchFailed;
                    _flyoutWindow.CloseForExit();
                }
                catch
                {
                    // Ignore window teardown failures during shutdown.
                }

                _flyoutWindow = null;
            }

            if (_contextMenu != null)
            {
                _contextMenu.Dispose();
            }

            if (_iconImage != null)
            {
                _iconImage.Dispose();
                _iconImage = null;
            }

            _trayHost.Dispose();
        }
    }
}
