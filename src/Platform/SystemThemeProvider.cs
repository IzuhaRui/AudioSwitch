using System;
using System.Windows.Media;
using Microsoft.Win32;

namespace AudioSwitch.Platform
{
    /// <summary>Reads the Windows 10 shell theme and accent colour from the registry.</summary>
    public sealed class SystemThemeProvider : IThemeProvider
    {
        private static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x78, 0xD7);

        private readonly ILogger _logger;
        private bool _disposed;

        public SystemThemeProvider(ILogger logger)
        {
            _logger = logger;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        public event EventHandler ThemeChanged;

        public ThemeInfo GetTheme()
        {
            bool isLightTaskbar = IsLightTaskbar();
            return new ThemeInfo(isLightTaskbar, GetAccentColor(isLightTaskbar));
        }

        private bool IsLightTaskbar()
        {
            string forced = Environment.GetEnvironmentVariable("AUDIOSWITCH_FORCE_THEME");
            if (!string.IsNullOrEmpty(forced))
            {
                return !string.Equals(forced, "dark", StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("SystemUsesLightTheme");
                        if (value is int)
                        {
                            return (int)value != 0;
                        }
                    }
                }
            }
            catch
            {
                // Assume the default (dark) taskbar theme.
            }

            return false;
        }

        private Color GetAccentColor(bool isLightTaskbar)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AccentColor");
                        if (value is int)
                        {
                            // Stored as 0xAABBGGRR.
                            int raw = (int)value;
                            Color accent = Color.FromRgb(
                                (byte)(raw & 0xFF),
                                (byte)((raw >> 8) & 0xFF),
                                (byte)((raw >> 16) & 0xFF));

                            Color background = isLightTaskbar
                                ? Color.FromRgb(0xF2, 0xF2, 0xF2)
                                : Color.FromRgb(0x2B, 0x2B, 0x2B);

                            if (ContrastRatio(accent, background) >= 2.1)
                            {
                                return accent;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through to the classic Windows 10 accent.
            }

            return DefaultAccent;
        }

        private static double ContrastRatio(Color first, Color second)
        {
            double firstLuminance = RelativeLuminance(first);
            double secondLuminance = RelativeLuminance(second);
            double lighter = Math.Max(firstLuminance, secondLuminance);
            double darker = Math.Min(firstLuminance, secondLuminance);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(Color color)
        {
            return 0.2126 * ToLinear(color.R) + 0.7152 * ToLinear(color.G) + 0.0722 * ToLinear(color.B);
        }

        private static double ToLinear(byte component)
        {
            double value = component / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General
                || e.Category == UserPreferenceCategory.Color
                || e.Category == UserPreferenceCategory.VisualStyle)
            {
                RaiseThemeChanged();
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            RaiseThemeChanged();
        }

        private void RaiseThemeChanged()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                EventHandler handler = ThemeChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("主题变更通知处理失败。", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            catch
            {
                // Ignore teardown failures.
            }
        }
    }
}
