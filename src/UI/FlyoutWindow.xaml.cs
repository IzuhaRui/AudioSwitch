using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioSwitch.Audio;
using AudioSwitch.Platform;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Imaging;

namespace AudioSwitch.UI
{
    /// <summary>
    /// Windows 10 style device flyout anchored above the taskbar notification area.
    /// Pure view: it renders the <see cref="FlyoutViewModel"/> and reports user
    /// actions to its owner. Selecting a device keeps the panel open so the user can
    /// switch the speakers and the microphone in one go; every device also has its
    /// own volume slider with a mute button.
    /// </summary>
    public partial class FlyoutWindow : Window
    {
        private const double DefaultWidth = 380;
        private const double EdgeMargin = 8;
        private const double MaximumHeightRatio = 0.78;
        private const int ReopenGuardMilliseconds = 250;
        private const int VolumePollMilliseconds = 600;
        private const int AppRefreshMilliseconds = 1200;
        private const string PageSettingName = "FlyoutPage";

        private readonly FlyoutViewModel _viewModel;
        private readonly AppVolumeViewModel _appVolume;
        private readonly IThemeProvider _themeProvider;
        private readonly IAppSettings _settings;
        private readonly ILogger _logger;
        private readonly List<Button> _deviceButtons = new List<Button>();
        private readonly List<DeviceRowControls> _rows = new List<DeviceRowControls>();
        private readonly List<AppRowControls> _appRows = new List<AppRowControls>();
        private readonly DispatcherTimer _volumeTimer;
        private readonly DispatcherTimer _levelTimer;
        private readonly DispatcherTimer _appTimer;

        private int _lastHiddenTick;
        private bool _allowClose;
        private bool _suspendAutoHide;
        private bool _showAppsPage;
        private string _appSignature;
        private Slider _draggingSlider;

        public FlyoutWindow(FlyoutViewModel viewModel, AppVolumeViewModel appVolume,
            IThemeProvider themeProvider, IAppSettings settings, ILogger logger)
        {
            _viewModel = viewModel;
            _appVolume = appVolume;
            _themeProvider = themeProvider;
            _settings = settings;
            _logger = logger;

            InitializeComponent();
            Width = DefaultWidth;
            _showAppsPage = string.Equals(_settings.GetString(PageSettingName, "devices"), "apps",
                StringComparison.OrdinalIgnoreCase);
            ApplyTheme();

            _volumeTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(VolumePollMilliseconds)
            };
            _volumeTimer.Tick += OnVolumeTimerTick;

            _levelTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            _levelTimer.Tick += OnLevelTimerTick;

            _appTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(AppRefreshMilliseconds)
            };
            _appTimer.Tick += OnAppTimerTick;

            SourceInitialized += OnSourceInitialized;
            _themeProvider.ThemeChanged += OnThemeChanged;
            PreviewMouseMove += OnWindowPreviewMouseMove;
            PreviewMouseLeftButtonUp += OnWindowPreviewMouseLeftButtonUp;
        }

        public event Action<string> SwitchFailed;

        /// <summary>Used by the developer capture/screenshot modes so the window does not hide on focus loss.</summary>
        internal bool SuspendAutoHide
        {
            get { return _suspendAutoHide; }
            set { _suspendAutoHide = value; }
        }

        internal void ForceAppsPage(bool appsPage)
        {
            _showAppsPage = appsPage;
        }

        internal bool IsAppsPage
        {
            get { return _showAppsPage; }
        }

        internal int AppRowCount
        {
            get { return _appRows.Count; }
        }

        internal bool SwitchPageForTest(bool appsPage)
        {
            SwitchPage(appsPage);
            return _showAppsPage;
        }

        // ------------------------------------------------------------- window lifecycle

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            WindowShadow.EnableDwmShadow(this, _themeProvider.GetTheme().IsDarkTaskbar);
        }

        public void ToggleNearTray()
        {
            if (IsVisible)
            {
                HideFlyout();
                return;
            }

            if (unchecked(Environment.TickCount - _lastHiddenTick) < ReopenGuardMilliseconds)
            {
                return;
            }

            ShowNearTray();
        }

        public void ShowNearTray()
        {
            try
            {
                RefreshContent();
                PositionNearTray();
                Show();
                Activate();
                FocusCurrentDevice();
                StartVolumePolling();
                StartLevelMeter();
                if (_showAppsPage)
                {
                    StartAppTimer();
                }
                else
                {
                    StopAppTimer();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("显示音频设备面板失败。", ex);
            }
        }

        public void HideFlyout()
        {
            StopVolumePolling();
            StopLevelMeter();
            StopAppTimer();

            if (!IsVisible)
            {
                return;
            }

            _lastHiddenTick = Environment.TickCount;
            Hide();
        }

        public void CloseForExit()
        {
            StopVolumePolling();
            StopLevelMeter();
            StopAppTimer();
            _allowClose = true;
            Close();
        }

        public void RefreshIfVisible()
        {
            if (!IsVisible)
            {
                return;
            }

            double bottom = Top + ActualHeight;
            RefreshContent();
            Top = Math.Max(0, bottom - Height);
        }

        private void PositionNearTray()
        {
            Rect workArea = SystemParameters.WorkArea;
            Left = Math.Round(workArea.Right - Width - EdgeMargin);
            Top = Math.Round(workArea.Bottom - Height - EdgeMargin);
        }

        private void RefreshContent()
        {
            ApplyTheme();
            _viewModel.Refresh();
            BuildContent();
            FitHeightToContent();
        }

        /// <summary>Rebuilds the rows without losing the scroll position / keyboard focus.</summary>
        private void RefreshContentKeepingPosition(string focusDeviceId)
        {
            double offset = DeviceScrollViewer.VerticalOffset;
            RefreshContent();

            if (!IsVisible)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                DeviceScrollViewer.ScrollToVerticalOffset(offset);
                if (!string.IsNullOrEmpty(focusDeviceId))
                {
                    FocusDevice(focusDeviceId);
                }
            }));
        }

        private void FitHeightToContent()
        {
            RootBorder.InvalidateMeasure();
            RootBorder.Measure(new Size(Width, double.PositiveInfinity));
            double naturalHeight = Math.Ceiling(RootBorder.DesiredSize.Height);

            double workAreaHeight = SystemParameters.WorkArea.Height;
            double maximumHeight = Math.Max(240, workAreaHeight * MaximumHeightRatio);
            Height = Math.Max(1, Math.Min(naturalHeight, maximumHeight));
        }

        // ------------------------------------------------------------- theme

        private void ApplyTheme()
        {
            ThemeInfo theme = _themeProvider.GetTheme();
            bool light = theme.IsLightTaskbar;

            Color background = light ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x2B, 0x2B, 0x2B);
            Color text = light ? Colors.Black : Colors.White;
            Color secondary = light ? Color.FromRgb(0x5F, 0x5F, 0x5F) : Color.FromRgb(0xA8, 0xA8, 0xA8);
            Color hover = light ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromRgb(0x3A, 0x3A, 0x3A);
            Color pressed = light ? Color.FromRgb(0xDB, 0xDB, 0xDB) : Color.FromRgb(0x46, 0x46, 0x46);
            Color separator = light ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Color.FromRgb(0x3F, 0x3F, 0x3F);
            Color border = light ? Color.FromArgb(0x40, 0, 0, 0) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
            Color sliderTrack = light ? Color.FromRgb(0xC8, 0xC8, 0xC8) : Color.FromRgb(0x5A, 0x5A, 0x5A);
            Color sliderFill = light ? Color.FromRgb(0x30, 0x30, 0x30) : Color.FromRgb(0xB8, 0xB8, 0xB8);
            Color levelMeter = light ? Color.FromRgb(0x10, 0x10, 0x10) : Color.FromRgb(0xF0, 0xF0, 0xF0);
            Color thumbBorder = light ? Color.FromRgb(0x14, 0x14, 0x14) : Color.FromRgb(0x1A, 0x1A, 0x1A);
            Color thumbFill = light ? theme.AccentColor : Color.FromRgb(0xF0, 0xF0, 0xF0);
            Color checkColor = light ? theme.AccentColor : Color.FromRgb(0xF5, 0xF5, 0xF5);
            Color selectedBackground = light ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Color.FromRgb(0x3D, 0x3D, 0x3D);
            Color selectedVolumeBackground = light ? Color.FromRgb(0xE9, 0xE9, 0xE9) : Color.FromRgb(0x33, 0x33, 0x33);
            Color selectedHover = light ? Color.FromRgb(0xD6, 0xD6, 0xD6) : Color.FromRgb(0x47, 0x47, 0x47);
            Color selectedPressed = light ? Color.FromRgb(0xCC, 0xCC, 0xCC) : Color.FromRgb(0x51, 0x51, 0x51);

            SetBrush("FlyoutBackgroundBrush", background);
            SetBrush("PrimaryTextBrush", text);
            SetBrush("SecondaryTextBrush", secondary);
            SetBrush("SectionTextBrush", secondary);
            SetBrush("HoverBrush", hover);
            SetBrush("PressedBrush", pressed);
            SetBrush("SeparatorBrush", separator);
            SetBrush("FlyoutBorderBrush", border);
            SetBrush("SliderTrackBrush", sliderTrack);
            SetBrush("SliderFillBrush", sliderFill);
            SetBrush("LevelMeterBrush", levelMeter);
            SetBrush("SliderThumbBorderBrush", thumbBorder);
            SetBrush("SliderThumbBrush", thumbFill);
            SetBrush("DeviceCheckBrush", checkColor);
            SetBrush("SelectedRowBackgroundBrush", selectedBackground);
            SetBrush("SelectedVolumeBackgroundBrush", selectedVolumeBackground);
            SetBrush("SelectedRowHoverBrush", selectedHover);
            SetBrush("SelectedRowPressedBrush", selectedPressed);
            SetBrush("AccentBrush", theme.AccentColor);
            WindowShadow.UpdateDarkFrame(this, theme.IsDarkTaskbar);
        }

        private void SetBrush(string key, Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            Resources[key] = brush;

            // The device context menu is hosted in a separate popup visual tree, so
            // DynamicResource lookups there cannot see Window.Resources. Mirror the
            // theme brushes at application scope so the menu follows light/dark theme.
            if (Application.Current != null)
            {
                Application.Current.Resources[key] = brush;
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!IsVisible)
                {
                    return;
                }

                double bottom = Top + ActualHeight;
                RefreshContent();
                Top = Math.Max(0, bottom - Height);
            }));
        }

        // ------------------------------------------------------------- content

        private void BuildContent()
        {
            DeviceStackPanel.Children.Clear();
            _deviceButtons.Clear();
            _rows.Clear();
            _appRows.Clear();

            if (_showAppsPage)
            {
                BuildAppsContent();
                UpdatePageSwitchButton();
                return;
            }

            foreach (DeviceSectionViewModel section in _viewModel.Sections)
            {
                AddSection(section);
            }

            foreach (DeviceRowControls row in _rows)
            {
                LoadVolumeState(row);
            }

            UpdatePageSwitchButton();
        }

        // ------------------------------------------------------------- per-app page

        private void BuildAppsContent()
        {
            try
            {
                _appVolume.Refresh();
            }
            catch (Exception ex)
            {
                _logger.Error("刷新应用音频会话失败。", ex);
            }

            IList<AppVolumeItemViewModel> items = _appVolume.Items;

            TextBlock header = new TextBlock
            {
                Text = "应用音量",
                FontSize = 12,
                Foreground = (Brush)FindResource("SectionTextBrush"),
                Margin = new Thickness(16, 10, 16, 4)
            };
            DeviceStackPanel.Children.Add(header);

            if (items == null || items.Count == 0)
            {
                TextBlock empty = new TextBlock
                {
                    Text = "当前没有正在使用音频的应用",
                    FontSize = 13,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(16, 6, 16, 8)
                };
                DeviceStackPanel.Children.Add(empty);
                _appSignature = string.Empty;
                return;
            }

            StringBuilder signature = new StringBuilder();
            foreach (AppVolumeItemViewModel item in items)
            {
                signature.Append(item.Key).Append(';');
                DeviceStackPanel.Children.Add(CreateAppRow(item));
            }

            _appSignature = signature.ToString();
            foreach (AppRowControls row in _appRows)
            {
                LoadAppVolumeState(row);
            }
        }

        private FrameworkElement CreateAppRow(AppVolumeItemViewModel item)
        {
            AppRowControls row = new AppRowControls { Item = item };
            StackPanel panel = new StackPanel();

            Border nameArea = new Border { Background = Brushes.Transparent, Child = CreateAppNameArea(row) };

            Brush volumeNormal = Brushes.Transparent;
            Brush volumeHover = (Brush)FindResource("HoverBrush");
            Border volumeArea = new Border
            {
                Margin = new Thickness(0, 0, 0, 2),
                Background = volumeNormal,
                Child = CreateAppVolumeRow(row)
            };
            volumeArea.MouseEnter += delegate { volumeArea.Background = volumeHover; };
            volumeArea.MouseLeave += delegate { volumeArea.Background = volumeNormal; };

            panel.Children.Add(nameArea);
            panel.Children.Add(volumeArea);
            _appRows.Add(row);
            return panel;
        }

        private FrameworkElement CreateAppNameArea(AppRowControls row)
        {
            Grid grid = new Grid { Height = 40 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            FrameworkElement icon;
            ImageSource appIcon = row.Item.IsSystemSounds
                ? null
                : AppIconLoader.TryLoad(row.Item.ExecutablePath);
            if (appIcon != null)
            {
                Image image = new Image
                {
                    Source = appIcon,
                    Width = 20,
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                icon = image;
            }
            else
            {
                icon = new TextBlock
                {
                    Text = FlyoutVisuals.GlyphForApp(row.Item.IsSystemSounds),
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("PrimaryTextBrush")
                };
            }

            TextBlock name = new TextBlock
            {
                Text = row.Item.Name,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            };
            Grid.SetColumn(name, 1);

            grid.Children.Add(icon);
            grid.Children.Add(name);
            return grid;
        }

        private FrameworkElement CreateAppVolumeRow(AppRowControls row)
        {
            Grid grid = new Grid { Height = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            TextBlock glyph = new TextBlock
            {
                Text = VolumeGlyphs.ForVolume(100, false),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Width = 18,
                Height = 18,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            };

            Button muteButton = new Button
            {
                Style = (Style)FindResource("MuteButtonStyle"),
                Content = glyph,
                Tag = row,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(muteButton, row.Item.Name + " 静音");
            Grid.SetColumn(muteButton, 0);

            Slider slider = new Slider
            {
                Style = (Style)FindResource("VolumeSliderStyle"),
                Tag = row,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(slider, row.Item.Name + " 音量");
            Grid.SetColumn(slider, 1);

            TextBlock percent = new TextBlock
            {
                Text = "0",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            };
            Grid.SetColumn(percent, 2);

            muteButton.Click += OnAppMuteButtonClick;
            slider.ValueChanged += OnAppVolumeSliderValueChanged;
            slider.PreviewMouseLeftButtonDown += OnVolumeSliderMouseDown;
            slider.PreviewMouseMove += OnVolumeSliderMouseMove;
            slider.PreviewMouseLeftButtonUp += OnVolumeSliderMouseUp;
            slider.LostMouseCapture += OnSliderLostMouseCapture;
            slider.SizeChanged += delegate { UpdateSliderFill(slider); };

            row.VolumeSlider = slider;
            row.MuteButton = muteButton;
            row.MuteGlyph = glyph;
            row.PercentText = percent;

            grid.Children.Add(muteButton);
            grid.Children.Add(slider);
            grid.Children.Add(percent);
            return grid;
        }

        private void LoadAppVolumeState(AppRowControls row)
        {
            ApplyAppVolumeState(row, row.Item.Percent, row.Item.Muted);
        }

        private void ApplyAppVolumeState(AppRowControls row, int percent, bool muted)
        {
            if (row == null || row.VolumeSlider == null)
            {
                return;
            }

            row.Updating = true;
            try
            {
                row.VolumeSlider.Value = percent;
                row.PercentText.Text = percent.ToString();
                row.LastPercent = percent;
                row.LastMuted = muted;
                if (!muted)
                {
                    row.ExplicitMute = false;
                }

                row.DisplayedMuted = muted && (percent > 0 || row.ExplicitMute);
                if (row.MuteGlyph != null)
                {
                    row.MuteGlyph.Text = VolumeGlyphs.GetDisplayGlyph(percent, row.DisplayedMuted,
                        row.ExplicitMute);
                }

                UpdateSliderFill(row.VolumeSlider);
            }
            finally
            {
                row.Updating = false;
            }
        }

        private void OnAppVolumeSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Slider slider = sender as Slider;
            AppRowControls row = slider != null ? slider.Tag as AppRowControls : null;
            if (row == null || row.Updating)
            {
                return;
            }

            int percent = (int)Math.Round(slider.Value);
            _appVolume.TrySetVolume(row.Item, (float)(slider.Value / 100.0));
            if (row.DisplayedMuted)
            {
                _appVolume.TrySetMute(row.Item, true);
            }

            row.LastPercent = percent;
            if (row.PercentText != null)
            {
                row.PercentText.Text = percent.ToString();
            }

            if (row.MuteGlyph != null)
            {
                row.MuteGlyph.Text = VolumeGlyphs.GetDisplayGlyph(percent, row.DisplayedMuted,
                    row.ExplicitMute);
            }

            UpdateSliderFill(slider);
        }

        private void OnAppMuteButtonClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            AppRowControls row = button != null ? button.Tag as AppRowControls : null;
            if (row == null)
            {
                return;
            }

            bool newMuted = !row.DisplayedMuted;
            if (_appVolume.TrySetMute(row.Item, newMuted))
            {
                row.LastMuted = newMuted;
                row.ExplicitMute = newMuted;
                row.DisplayedMuted = newMuted && (row.LastPercent > 0 || row.ExplicitMute);

                if (row.MuteGlyph != null)
                {
                    row.MuteGlyph.Text = VolumeGlyphs.GetDisplayGlyph(row.LastPercent,
                        newMuted, row.ExplicitMute);
                }
            }
        }

        private void UpdatePageSwitchButton()
        {
            if (PageSwitchGlyph == null || PageSwitchLabel == null)
            {
                return;
            }

            PageSwitchGlyph.Text = _showAppsPage ? "\uE72B" : "\uE71D";
            PageSwitchLabel.Text = _showAppsPage ? "返回设备" : "应用音量";
            AutomationProperties.SetName(PageSwitchButton, PageSwitchLabel.Text);
        }

        private void OnPageSwitchClick(object sender, RoutedEventArgs e)
        {
            SwitchPage(!_showAppsPage);
        }

        private void SwitchPage(bool appsPage)
        {
            _showAppsPage = appsPage;
            try
            {
                _settings.SetString(PageSettingName, appsPage ? "apps" : "devices");
            }
            catch (Exception ex)
            {
                _logger.Error("保存面板页面设置失败。", ex);
            }

            double bottom = Top + ActualHeight;
            RefreshContent();
            if (IsVisible)
            {
                Top = Math.Max(0, bottom - Height);
                StartLevelMeter();
                if (_showAppsPage)
                {
                    StartAppTimer();
                }
                else
                {
                    StopAppTimer();
                }
            }

            if (!_showAppsPage)
            {
                FocusCurrentDevice();
            }
        }

        private void StartAppTimer()
        {
            _appTimer.Stop();
            _appTimer.Start();
        }

        private void StopAppTimer()
        {
            _appTimer.Stop();
        }

        private void OnAppTimerTick(object sender, EventArgs e)
        {
            if (!IsVisible || !_showAppsPage)
            {
                StopAppTimer();
                return;
            }

            if (_draggingSlider != null)
            {
                // Do not rebuild the list while the user is dragging a slider.
                return;
            }

            try
            {
                _appVolume.Refresh();
            }
            catch (Exception ex)
            {
                _logger.Error("定时刷新应用音频会话失败。", ex);
                return;
            }

            StringBuilder signature = new StringBuilder();
            foreach (AppVolumeItemViewModel item in _appVolume.Items)
            {
                signature.Append(item.Key).Append(';');
            }

            if (signature.ToString() != _appSignature)
            {
                double bottom = Top + ActualHeight;
                RefreshContent();
                Top = Math.Max(0, bottom - Height);
                return;
            }

            foreach (AppRowControls row in _appRows)
            {
                if (row.VolumeSlider == null || row.Updating || row.VolumeSlider.IsMouseCaptureWithin
                    || _draggingSlider == row.VolumeSlider)
                {
                    continue;
                }

                AppVolumeItemViewModel item = FindAppItem(row.Item.Key);
                if (item == null)
                {
                    continue;
                }

                if (item.Percent != row.LastPercent || item.Muted != row.LastMuted)
                {
                    ApplyAppVolumeState(row, item.Percent, item.Muted);
                }
            }
        }

        private AppVolumeItemViewModel FindAppItem(string key)
        {
            foreach (AppVolumeItemViewModel item in _appVolume.Items)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        private void AddSection(DeviceSectionViewModel section)
        {
            TextBlock header = new TextBlock
            {
                Text = section.Title,
                FontSize = 12,
                Foreground = (Brush)FindResource("SectionTextBrush"),
                Margin = new Thickness(16, 10, 16, 4)
            };
            DeviceStackPanel.Children.Add(header);

            if (section.Items.Count == 0)
            {
                TextBlock empty = new TextBlock
                {
                    Text = section.EmptyText,
                    FontSize = 13,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(16, 6, 16, 8)
                };
                DeviceStackPanel.Children.Add(empty);
                return;
            }

            foreach (DeviceItemViewModel item in section.Items)
            {
                DeviceStackPanel.Children.Add(CreateDeviceRow(item));
            }
        }

        private FrameworkElement CreateDeviceRow(DeviceItemViewModel item)
        {
            DeviceRowControls row = new DeviceRowControls { Item = item };

            StackPanel panel = new StackPanel();

            // The current default device stays visibly highlighted: a darker tint on a
            // light taskbar theme, a lighter tint on a dark taskbar theme. The name area
            // and the volume area are two separate blocks; the volume block uses a
            // slightly lighter shade so the row does not look too heavy.
            Border nameArea = new Border
            {
                Background = item.IsDefault
                    ? (Brush)FindResource("SelectedRowBackgroundBrush")
                    : Brushes.Transparent,
                Child = CreateSelectButton(row)
            };

            Brush volumeNormal = item.IsDefault
                ? (Brush)FindResource("SelectedVolumeBackgroundBrush")
                : Brushes.Transparent;
            Brush volumeHover = (Brush)FindResource(item.IsDefault
                ? "SelectedRowHoverBrush"
                : "HoverBrush");

            Border volumeArea = new Border
            {
                Margin = new Thickness(0, 0, 0, 2),
                Background = volumeNormal,
                Child = CreateVolumeRow(row)
            };
            volumeArea.MouseEnter += delegate { volumeArea.Background = volumeHover; };
            volumeArea.MouseLeave += delegate { volumeArea.Background = volumeNormal; };
            row.VolumeArea = volumeArea;

            panel.Children.Add(nameArea);
            panel.Children.Add(volumeArea);

            _rows.Add(row);
            return panel;
        }

        private Button CreateSelectButton(DeviceRowControls row)
        {
            DeviceItemViewModel item = row.Item;

            Button button = new Button
            {
                Style = (Style)FindResource(item.IsDefault ? "SelectedDeviceButtonStyle" : "DeviceButtonStyle"),
                Height = 40,
                Tag = row,
                ToolTip = item.Name
            };
            AutomationProperties.SetName(button, item.Name);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

            TextBlock icon = new TextBlock
            {
                Text = FlyoutVisuals.GlyphFor(item.Endpoint),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            };

            TextBlock name = new TextBlock
            {
                Text = item.Name,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            };
            Grid.SetColumn(name, 1);

            TextBlock check = new TextBlock
            {
                Text = FlyoutVisuals.GlyphCheck,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("DeviceCheckBrush"),
                Visibility = item.IsDefault ? Visibility.Visible : Visibility.Collapsed
            };
            Grid.SetColumn(check, 2);

            grid.Children.Add(icon);
            grid.Children.Add(name);
            grid.Children.Add(check);

            button.Content = grid;
            button.Click += OnDeviceClick;
            button.ContextMenu = CreateDeviceContextMenu(row);
            row.SelectButton = button;
            _deviceButtons.Add(button);
            return button;
        }

        /// <summary>Right-click menu on a device name: hide it from the panel.</summary>
        private ContextMenu CreateDeviceContextMenu(DeviceRowControls row)
        {
            ContextMenu menu = new ContextMenu
            {
                Style = (Style)FindResource("FlyoutContextMenuStyle")
            };

            MenuItem hideItem = new MenuItem
            {
                Header = "隐藏此设备",
                Tag = row,
                Style = (Style)FindResource("FlyoutMenuItemStyle"),
                ToolTip = "隐藏后可从托盘右键菜单「取消所有隐藏」恢复"
            };
            hideItem.Click += OnHideDeviceMenuClick;
            menu.Items.Add(hideItem);
            AutomationProperties.SetName(hideItem, "隐藏此设备");
            return menu;
        }

        private void OnHideDeviceMenuClick(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            DeviceRowControls row = menuItem != null ? menuItem.Tag as DeviceRowControls : null;
            if (row == null)
            {
                return;
            }

            if (_viewModel.HideDevice(row.Item))
            {
                _logger.Info("用户从面板隐藏了设备: " + row.Item.Name);
                RefreshContentKeepingPosition(null);
            }
        }

        private FrameworkElement CreateVolumeRow(DeviceRowControls row)
        {
            Grid grid = new Grid { Height = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            TextBlock percent = new TextBlock
            {
                Text = "0",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            };
            Grid.SetColumn(percent, 2);

            // Windows system volume icon; it changes with the volume level and mute state.
            TextBlock glyph = new TextBlock
            {
                Text = VolumeGlyphs.ForVolume(100, false),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Width = 18,
                Height = 18,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("PrimaryTextBrush")
            };

            Button muteButton = new Button
            {
                Style = (Style)FindResource("MuteButtonStyle"),
                Content = glyph,
                Tag = row,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(muteButton, "静音");
            Grid.SetColumn(muteButton, 0);

            Slider slider = new Slider
            {
                Style = (Style)FindResource("VolumeSliderStyle"),
                Tag = row,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(slider, row.Item.Name + " 音量");
            Grid.SetColumn(slider, 1);

            muteButton.Click += OnMuteButtonClick;
            slider.ValueChanged += OnVolumeSliderValueChanged;
            slider.PreviewMouseLeftButtonDown += OnVolumeSliderMouseDown;
            slider.PreviewMouseMove += OnVolumeSliderMouseMove;
            slider.PreviewMouseLeftButtonUp += OnVolumeSliderMouseUp;
            slider.LostMouseCapture += OnSliderLostMouseCapture;
            slider.SizeChanged += delegate { UpdateSliderFill(row); };

            row.VolumeSlider = slider;
            row.MuteButton = muteButton;
            row.MuteGlyph = glyph;
            row.PercentText = percent;

            grid.Children.Add(muteButton);
            grid.Children.Add(slider);
            grid.Children.Add(percent);
            return grid;
        }

        private void FocusCurrentDevice()
        {
            if (_deviceButtons.Count == 0)
            {
                return;
            }

            Button target = _deviceButtons[0];
            foreach (DeviceRowControls row in _rows)
            {
                if (row.Item.IsDefault && row.SelectButton != null)
                {
                    target = row.SelectButton;
                    break;
                }
            }

            target.Focus();
        }

        private void FocusDevice(string deviceId)
        {
            foreach (DeviceRowControls row in _rows)
            {
                if (row.SelectButton != null
                    && string.Equals(row.Item.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    row.SelectButton.Focus();
                    return;
                }
            }
        }

        private void MoveFocus(int direction)
        {
            if (_deviceButtons.Count == 0)
            {
                return;
            }

            int index = _deviceButtons.IndexOf(Keyboard.FocusedElement as Button);
            if (index < 0)
            {
                index = direction > 0 ? -1 : 0;
            }

            int next = (index + direction + _deviceButtons.Count) % _deviceButtons.Count;
            _deviceButtons[next].Focus();
        }

        // ------------------------------------------------------------- volume

        private void StartVolumePolling()
        {
            _volumeTimer.Stop();
            _volumeTimer.Start();
        }

        private void StopVolumePolling()
        {
            _volumeTimer.Stop();
        }

        private void StartLevelMeter()
        {
            _levelTimer.Stop();
            _levelTimer.Start();
        }

        private void StopLevelMeter()
        {
            _levelTimer.Stop();
        }

        private void OnLevelTimerTick(object sender, EventArgs e)
        {
            if (!IsVisible)
            {
                StopLevelMeter();
                return;
            }

            if (_showAppsPage)
            {
                try
                {
                    UpdateAppLevelMeters();
                }
                catch (Exception ex)
                {
                    _logger.Error("更新应用电平失败。", ex);
                }

                return;
            }

            foreach (DeviceRowControls row in _rows)
            {
                if (row.VolumeSlider == null)
                {
                    continue;
                }

                float peak;
                if (!_viewModel.TryGetPeakValue(row.Item, out peak))
                {
                    peak = 0f;
                }

                // Fast attack, slow release - like the Windows peak meters.
                float displayed = Math.Max(peak, row.DisplayedPeak * 0.82f);
                if (displayed < 0.01f)
                {
                    displayed = 0f;
                }

                row.DisplayedPeak = displayed;

                if (row.VolumeSlider != null)
                {
                    double usable = Math.Max(0, row.VolumeSlider.ActualWidth - 8);
                    double meterWidth = Math.Round(usable * displayed, 1);
                    LevelMeter.SetLevel(row.VolumeSlider, meterWidth < 0 ? 0 : meterWidth);
                    UpdateSliderFill(row);
                }
            }
        }

        private void UpdateAppLevelMeters()
        {
            foreach (AppRowControls row in _appRows)
            {
                if (row.VolumeSlider == null)
                {
                    continue;
                }

                float peak;
                if (!_appVolume.TryGetPeakValue(row.Item, out peak))
                {
                    peak = 0f;
                }

                float displayed = Math.Max(peak, row.DisplayedPeak * 0.82f);
                if (displayed < 0.01f)
                {
                    displayed = 0f;
                }

                row.DisplayedPeak = displayed;
                double usable = Math.Max(0, row.VolumeSlider.ActualWidth - 8);
                LevelMeter.SetLevel(row.VolumeSlider, Math.Round(usable * displayed, 1));
                UpdateSliderFill(row.VolumeSlider);
            }
        }

        /// <summary>Draws the square-ended volume fill line from the left edge to the thumb.</summary>
        private static void UpdateSliderFill(DeviceRowControls row)
        {
            if (row != null)
            {
                UpdateSliderFill(row.VolumeSlider);
            }
        }

        private static void UpdateSliderFill(Slider slider)
        {
            if (slider == null)
            {
                return;
            }

            const double ThumbWidth = 8;
            double usable = Math.Max(0, slider.ActualWidth - ThumbWidth);
            double width = (slider.Value / 100.0) * usable;
            LevelMeter.SetFillWidth(slider, Math.Max(0, Math.Round(width, 1)));
        }

        private void OnVolumeTimerTick(object sender, EventArgs e)
        {
            if (!IsVisible)
            {
                StopVolumePolling();
                return;
            }

            if (_showAppsPage)
            {
                return;
            }

            foreach (DeviceRowControls row in _rows)
            {
                if (row.Updating || row.VolumeSlider == null || row.VolumeSlider.IsMouseCaptureWithin)
                {
                    continue;
                }

                AudioVolumeState state;
                if (!_viewModel.TryGetVolumeState(row.Item, out state))
                {
                    continue;
                }

                if (state.Percent != row.LastPercent || state.Muted != row.LastMuted)
                {
                    ApplyVolumeState(row, state);
                }
            }
        }

        private void LoadVolumeState(DeviceRowControls row)
        {
            AudioVolumeState state;
            if (!_viewModel.TryGetVolumeState(row.Item, out state))
            {
                if (row.VolumeSlider != null)
                {
                    row.VolumeSlider.IsEnabled = false;
                }

                if (row.MuteButton != null)
                {
                    row.MuteButton.IsEnabled = false;
                }

                return;
            }

            ApplyVolumeState(row, state);
        }

        private void ApplyVolumeState(DeviceRowControls row, AudioVolumeState state)
        {
            if (row.VolumeSlider == null || state == null)
            {
                return;
            }

            row.Updating = true;
            try
            {
                row.VolumeSlider.Value = state.Percent;
                row.LastPercent = state.Percent;
                row.LastMuted = state.Muted;
                if (!state.Muted)
                {
                    // External unmute clears the "user explicitly muted" flag.
                    row.ExplicitMute = false;
                }

                row.DisplayedMuted = state.Muted && (state.Percent > 0 || row.ExplicitMute);
                if (row.MuteGlyph != null)
                {
                    row.MuteGlyph.Text = VolumeGlyphs.GetDisplayGlyph(state.Percent,
                        state.Muted, row.ExplicitMute);
                }

                if (row.PercentText != null)
                {
                    row.PercentText.Text = state.Percent.ToString();
                }

                UpdateSliderFill(row);

                if (row.MuteButton != null)
                {
                        AutomationProperties.SetName(row.MuteButton, state.Muted ? "取消静音" : "静音");
                }
            }
            finally
            {
                row.Updating = false;
            }
        }

        private void OnVolumeSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Slider slider = sender as Slider;
            DeviceRowControls row = slider != null ? slider.Tag as DeviceRowControls : null;
            if (row == null || row.Updating)
            {
                return;
            }

            int percent = (int)Math.Round(slider.Value);
            _viewModel.TrySetVolume(row.Item, (float)(slider.Value / 100.0));

            // Some audio drivers auto-unmute when the volume is changed. If the device is
            // currently shown as muted (explicit button click or an external mute at a
            // non-zero volume), keep it muted while the user adjusts the volume.
            if (row.DisplayedMuted)
            {
                _viewModel.TrySetMute(row.Item, true);
            }

            row.LastPercent = percent;
            UpdateVolumeGlyph(row, percent);

            if (row.PercentText != null)
            {
                row.PercentText.Text = percent.ToString();
            }

            UpdateSliderFill(row);
        }

        private void OnMuteButtonClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            DeviceRowControls row = button != null ? button.Tag as DeviceRowControls : null;
            if (row == null)
            {
                return;
            }

            bool newMuted = !row.DisplayedMuted;
            if (_viewModel.TrySetMute(row.Item, newMuted))
            {
                row.LastMuted = newMuted;
                row.ExplicitMute = newMuted;
                row.DisplayedMuted = newMuted && (row.LastPercent > 0 || row.ExplicitMute);

                if (row.MuteGlyph != null)
                {
                    row.MuteGlyph.Text = VolumeGlyphs.GetDisplayGlyph(row.LastPercent,
                        newMuted, row.ExplicitMute);
                }

                if (row.MuteButton != null)
                {
                        AutomationProperties.SetName(row.MuteButton, newMuted ? "取消静音" : "静音");
                }
            }
        }

        // ------------------------------------------------------------- slider dragging

        private const double SliderThumbSize = 8;

        private static double ComputeSliderValue(Slider slider, double x)
        {
            double width = slider.ActualWidth;
            if (width <= SliderThumbSize)
            {
                return slider.Minimum;
            }

            double usable = width - SliderThumbSize;
            double ratio = (x - SliderThumbSize / 2.0) / usable;
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            return slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        }

        private static void ApplySliderPoint(Slider slider, double x)
        {
            slider.Value = ComputeSliderValue(slider, x);
        }

        private void OnVolumeSliderMouseDown(object sender, MouseButtonEventArgs e)
        {
            Slider slider = sender as Slider;
            if (slider == null || !slider.IsEnabled)
            {
                return;
            }

            // Allow dragging from any point of the bar, like the Windows volume slider,
            // instead of only when the thumb itself is grabbed.
            _draggingSlider = slider;
            slider.Focus();
            slider.CaptureMouse();
            ApplySliderPoint(slider, e.GetPosition(slider).X);
            e.Handled = true;
        }

        private void OnVolumeSliderMouseMove(object sender, MouseEventArgs e)
        {
            Slider slider = sender as Slider;
            if (slider == null || !slider.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            ApplySliderPoint(slider, e.GetPosition(slider).X);
            e.Handled = true;
        }

        private void OnVolumeSliderMouseUp(object sender, MouseButtonEventArgs e)
        {
            Slider slider = sender as Slider;
            if (slider == null || !slider.IsMouseCaptured)
            {
                return;
            }

            ApplySliderPoint(slider, e.GetPosition(slider).X);
            slider.ReleaseMouseCapture();
            if (_draggingSlider == slider)
            {
                _draggingSlider = null;
            }

            e.Handled = true;
        }

        private void OnSliderLostMouseCapture(object sender, MouseEventArgs e)
        {
            // Keep the drag alive when the button is still down: the window level
            // handlers below continue to track the pointer even without capture.
            Slider slider = sender as Slider;
            if (Mouse.LeftButton != MouseButtonState.Pressed && _draggingSlider == slider)
            {
                _draggingSlider = null;
            }
        }

        private void OnWindowPreviewMouseMove(object sender, MouseEventArgs e)
        {
            Slider slider = _draggingSlider;
            if (slider == null || slider.IsMouseCaptured)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _draggingSlider = null;
                return;
            }

            ApplySliderPoint(slider, e.GetPosition(slider).X);
            e.Handled = true;
        }

        private void OnWindowPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Slider slider = _draggingSlider;
            if (slider == null || slider.IsMouseCaptured)
            {
                return;
            }

            ApplySliderPoint(slider, e.GetPosition(slider).X);
            _draggingSlider = null;
            e.Handled = true;
        }

        private void UpdateVolumeGlyph(DeviceRowControls row, int percent)
        {
            if (row == null || row.MuteGlyph == null)
            {
                return;
            }

            // Use the muted state that is actually displayed. If Windows reported
            // muted=true only because the volume was 0% (auto-mute), the first drag step
            // must not flash the speaker+X while the mouse is still held down.
            row.MuteGlyph.Text = VolumeGlyphs.GetDisplayGlyph(percent, row.DisplayedMuted,
                row.ExplicitMute);
        }

        // ------------------------------------------------------------- user actions

        private void OnDeviceClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            DeviceRowControls row = button != null ? button.Tag as DeviceRowControls : null;
            if (row == null)
            {
                return;
            }

            // Deliberately keep the flyout open: users often switch the speakers and
            // the microphone one after another.
            try
            {
                _viewModel.Select(row.Item);
            }
            catch (Exception ex)
            {
                _logger.Error("切换默认音频设备失败: " + row.Item.Name, ex);
                Action<string> handler = SwitchFailed;
                if (handler != null)
                {
                    handler("无法切换到“" + row.Item.Name + "”。\n" + ex.Message);
                }
            }

            RefreshContentKeepingPosition(row.Item.Id);
        }



        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            if (!_suspendAutoHide)
            {
                HideFlyout();
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideFlyout();
                e.Handled = true;
                return;
            }

            // Let sliders handle their own arrow keys.
            if (Keyboard.FocusedElement is Slider)
            {
                return;
            }

            if (e.Key == Key.Up)
            {
                MoveFocus(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                MoveFocus(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                Button focused = Keyboard.FocusedElement as Button;
                if (focused != null)
                {
                    focused.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                }
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_allowClose)
            {
                return;
            }

            e.Cancel = true;
            HideFlyout();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopVolumePolling();
            StopLevelMeter();
            StopAppTimer();

            try
            {
                _themeProvider.ThemeChanged -= OnThemeChanged;
            }
            catch
            {
                // Ignore.
            }

            base.OnClosed(e);
        }

        // ------------------------------------------------------------- developer helpers

        internal void RenderPreviewToPng(string path)
        {
            ApplyTheme();
            _viewModel.Refresh();
            BuildContent();
            RootBorder.Measure(new Size(DefaultWidth, double.PositiveInfinity));
            RootBorder.Arrange(new Rect(0, 0, DefaultWidth, RootBorder.DesiredSize.Height));
            RootBorder.UpdateLayout();

            const double scale = 2.0;
            double height = Math.Max(1, RootBorder.ActualHeight);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(DefaultWidth * scale),
                (int)Math.Ceiling(height * scale),
                96 * scale,
                96 * scale,
                PixelFormats.Pbgra32);
            bitmap.Render(RootBorder);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        internal void ShowAt(double left, double top)
        {
            ApplyTheme();
            _viewModel.Refresh();
            BuildContent();
            FitHeightToContent();
            Left = left;
            Top = top;
            Show();
            Activate();
            FocusCurrentDevice();
            StartVolumePolling();
            StartLevelMeter();
            if (_showAppsPage)
            {
                StartAppTimer();
            }
            else
            {
                StopAppTimer();
            }
        }

        internal void CaptureWindowToPng(string path)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(handle, out rect))
            {
                throw new InvalidOperationException("无法获取窗口位置。");
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("窗口尺寸无效。");
            }

            using (Drawing.Bitmap bitmap = new Drawing.Bitmap(width, height, Drawing2D.PixelFormat.Format24bppRgb))
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Drawing.Size(width, height),
                    Drawing.CopyPixelOperation.SourceCopy);
                bitmap.Save(path, Drawing2D.ImageFormat.Png);
            }
        }

        internal string DescribeWindow()
        {
            if (!IsVisible)
            {
                return "面板: 隐藏";
            }

            IntPtr handle = new WindowInteropHelper(this).Handle;
            NativeMethods.RECT rect;
            if (NativeMethods.GetWindowRect(handle, out rect))
            {
                return string.Format("面板: 可见 位置=({0},{1}) 尺寸={2}x{3}",
                    rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }

            return "面板: 可见（无法读取窗口位置）";
        }

        // ------------------------------------------------------------- interaction test helpers

        internal int RowCount
        {
            get { return _rows.Count; }
        }

        internal string GetRowId(int index)
        {
            return index >= 0 && index < _rows.Count ? _rows[index].Item.Id : null;
        }

        internal bool HasVolumeTooltips(int index)
        {
            if (index < 0 || index >= _rows.Count)
            {
                return false;
            }

            DeviceRowControls row = _rows[index];
            return (row.VolumeSlider != null && row.VolumeSlider.ToolTip != null)
                || (row.MuteButton != null && row.MuteButton.ToolTip != null);
        }

        internal bool HasHideContextMenu(int index)
        {
            if (index < 0 || index >= _rows.Count || _rows[index].SelectButton == null)
            {
                return false;
            }

            ContextMenu menu = _rows[index].SelectButton.ContextMenu;
            if (menu == null || menu.Style == null)
            {
                return false;
            }

            // Verify the themed brushes actually resolved for the popup visual tree,
            // not just that the styles were assigned.
            SolidColorBrush background = menu.Background as SolidColorBrush;
            SolidColorBrush expectedBackground = FindResource("FlyoutBackgroundBrush") as SolidColorBrush;
            SolidColorBrush foreground = menu.Foreground as SolidColorBrush;
            SolidColorBrush expectedForeground = FindResource("PrimaryTextBrush") as SolidColorBrush;
            if (background == null || expectedBackground == null
                || background.Color != expectedBackground.Color
                || foreground == null || expectedForeground == null
                || foreground.Color != expectedForeground.Color)
            {
                return false;
            }

            foreach (object entry in menu.Items)
            {
                MenuItem menuItem = entry as MenuItem;
                if (menuItem != null
                    && menuItem.Style != null
                    && string.Equals(menuItem.Header as string, "隐藏此设备", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal string DescribeDeviceContextMenu(int index)
        {
            if (index < 0 || index >= _rows.Count || _rows[index].SelectButton == null)
            {
                return "(无)";
            }

            ContextMenu menu = _rows[index].SelectButton.ContextMenu;
            if (menu == null)
            {
                return "(无菜单)";
            }

            SolidColorBrush background = menu.Background as SolidColorBrush;
            SolidColorBrush foreground = menu.Foreground as SolidColorBrush;
            return "样式=" + (menu.Style != null ? "已应用" : "缺失")
                + "，背景=" + (background != null ? background.Color.ToString() : "?")
                + "，前景=" + (foreground != null ? foreground.Color.ToString() : "?");
        }

        internal bool HideRowByIndex(int index)
        {
            if (index < 0 || index >= _rows.Count)
            {
                return false;
            }

            DeviceItemViewModel item = _rows[index].Item;
            if (!_viewModel.HideDevice(item))
            {
                return false;
            }

            RefreshContentKeepingPosition(null);
            return true;
        }

        internal bool RestoreAllHiddenDevices()
        {
            if (!_viewModel.RestoreAllDevices())
            {
                return false;
            }

            RefreshContentKeepingPosition(null);
            return true;
        }

        /// <summary>
        /// Developer test helper: applies the same coordinate mapping as real mouse
        /// dragging, for environments where synthetic mouse input is not allowed.
        /// </summary>
        internal bool SimulateSliderDrag(int index, double startFraction, double endFraction)
        {
            if (index < 0 || index >= _rows.Count || _rows[index].VolumeSlider == null)
            {
                return false;
            }

            Slider slider = _rows[index].VolumeSlider;
            double usable = Math.Max(1.0, slider.ActualWidth - SliderThumbSize);
            ApplySliderPoint(slider, SliderThumbSize / 2.0 + startFraction * usable);
            ApplySliderPoint(slider, SliderThumbSize / 2.0 + endFraction * usable);
            return true;
        }

        internal string DescribeSliderState(int index)
        {
            if (index < 0 || index >= _rows.Count || _rows[index].VolumeSlider == null)
            {
                return "(无滑块)";
            }

            Slider slider = _rows[index].VolumeSlider;
            NativeMethods.POINT cursor;
            NativeMethods.GetCursorPos(out cursor);
            return string.Format("value={0:0} captured={1} mouseOver={2} active={3} cursor=({4},{5})",
                slider.Value, slider.IsMouseCaptured, slider.IsMouseOver, IsActive, cursor.X, cursor.Y);
        }

        internal Rect GetSliderScreenBounds(int index)
        {
            if (index < 0 || index >= _rows.Count || _rows[index].VolumeSlider == null)
            {
                return Rect.Empty;
            }

            Slider slider = _rows[index].VolumeSlider;
            Point origin = slider.PointToScreen(new Point(0, 0));
            DpiScale dpi = VisualTreeHelper.GetDpi(slider);
            return new Rect(origin.X, origin.Y,
                slider.ActualWidth * dpi.DpiScaleX, slider.ActualHeight * dpi.DpiScaleY);
        }

        internal string DescribeMeters()
        {
            StringBuilder builder = new StringBuilder();
            foreach (DeviceRowControls row in _rows)
            {
                builder.Append(row.Item.Name);
                builder.Append(" 音量=").Append(row.PercentText != null ? row.PercentText.Text : "?");
                builder.Append("% 电平=").Append(row.VolumeSlider != null
                    ? LevelMeter.GetLevel(row.VolumeSlider).ToString("0")
                    : "-");
                builder.Append("； ");
            }

            return builder.ToString();
        }

        internal string DescribeRows()
        {
            StringBuilder builder = new StringBuilder();
            foreach (DeviceRowControls row in _rows)
            {
                builder.Append(row.Item.Name);
                builder.Append(row.Item.IsDefault ? "[默认]" : string.Empty);
                builder.Append(" 音量=").Append(row.LastPercent).Append('%');
                builder.Append(row.LastMuted ? "[静音]" : string.Empty);
                builder.Append("； ");
            }

            return builder.ToString();
        }

        internal void SimulateSelectRow(int index)
        {
            if (index >= 0 && index < _rows.Count && _rows[index].SelectButton != null)
            {
                _rows[index].SelectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        internal void SimulateMuteRow(int index)
        {
            if (index >= 0 && index < _rows.Count && _rows[index].MuteButton != null)
            {
                _rows[index].MuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        internal void SimulateVolumeRow(int index, double percent)
        {
            if (index >= 0 && index < _rows.Count && _rows[index].VolumeSlider != null)
            {
                _rows[index].VolumeSlider.Value = percent;
            }
        }

        private sealed class DeviceRowControls
        {
            public DeviceItemViewModel Item;

            public Button SelectButton;

            public Slider VolumeSlider;

            public Button MuteButton;

            public TextBlock MuteGlyph;

            public Border VolumeArea;

            public TextBlock PercentText;

            public float DisplayedPeak;

            public bool Updating;

            public int LastPercent = -1;

            public bool LastMuted;

            public bool ExplicitMute;

            public bool DisplayedMuted;
        }

        private sealed class AppRowControls
        {
            public AppVolumeItemViewModel Item;

            public Slider VolumeSlider;

            public Button MuteButton;

            public TextBlock MuteGlyph;

            public TextBlock PercentText;

            public float DisplayedPeak;

            public bool Updating;

            public int LastPercent = -1;

            public bool LastMuted;

            public bool ExplicitMute;

            public bool DisplayedMuted;
        }
    }
}
