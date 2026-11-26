using System;

namespace AudioSwitch.Platform
{
    /// <summary>Supplies the Windows shell theme (light/dark taskbar and accent colour).</summary>
    public interface IThemeProvider : IDisposable
    {
        /// <summary>Raised when the user changes the Windows theme; may be raised off the UI thread.</summary>
        event EventHandler ThemeChanged;

        ThemeInfo GetTheme();
    }
}
