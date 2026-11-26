using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AudioSwitch.Platform;
using AudioSwitch.Tray;

namespace AudioSwitch.Host
{
    /// <summary>Test doubles used by the hidden --traytest / --uitest diagnostic modes.</summary>
    internal sealed class FakeTrayIconHost : ITrayIconHost
    {
        public int IconUpdateCount { get; private set; }

        public Icon LastIcon { get; private set; }

        public string LastTooltip { get; private set; }

        public int BalloonCount { get; private set; }

        public event EventHandler Activated;

        public void SetContextMenu(ContextMenuStrip menu)
        {
        }

        public void SetIcon(Icon icon)
        {
            LastIcon = icon;
            IconUpdateCount++;
        }

        public void SetTooltip(string tooltip)
        {
            LastTooltip = tooltip;
        }

        public void ShowBalloon(string title, string text, TrayNotificationKind kind)
        {
            BalloonCount++;
        }

        public bool TryGetIconRectangle(out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            return false;
        }

        public void RaiseActivated()
        {
            EventHandler handler = Activated;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
        }
    }

    internal sealed class FakeThemeProvider : IThemeProvider
    {
        private ThemeInfo _theme;

        public FakeThemeProvider(ThemeInfo theme)
        {
            _theme = theme;
        }

        public event EventHandler ThemeChanged;

        public ThemeInfo GetTheme()
        {
            return _theme;
        }

        public void SetTheme(ThemeInfo theme)
        {
            _theme = theme;
            EventHandler handler = ThemeChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
        }
    }

    internal sealed class InMemoryAppSettings : IAppSettings
    {
        private readonly Dictionary<string, bool> _flags =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _strings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool GetFlag(string name, bool defaultValue)
        {
            bool value;
            return _flags.TryGetValue(name, out value) ? value : defaultValue;
        }

        public void SetFlag(string name, bool value)
        {
            _flags[name] = value;
        }

        public string GetString(string name, string defaultValue)
        {
            string value;
            return _strings.TryGetValue(name, out value) ? value : defaultValue;
        }

        public void SetString(string name, string value)
        {
            _strings[name] = value;
        }
    }

    internal sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }

        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
        }
    }

    internal sealed class FakeShellLauncher : IShellLauncher
    {
        public int SoundSettingsCount { get; private set; }

        public int SoundPanelCount { get; private set; }

        public void OpenSoundSettings()
        {
            SoundSettingsCount++;
        }

        public void OpenSoundControlPanel()
        {
            SoundPanelCount++;
        }
    }
}
