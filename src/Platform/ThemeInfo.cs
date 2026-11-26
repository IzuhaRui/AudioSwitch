using System.Windows.Media;

namespace AudioSwitch.Platform
{
    /// <summary>Current Windows shell theme information used by the flyout.</summary>
    public sealed class ThemeInfo
    {
        public ThemeInfo(bool isLightTaskbar, Color accentColor)
        {
            IsLightTaskbar = isLightTaskbar;
            AccentColor = accentColor;
        }

        public bool IsLightTaskbar { get; private set; }

        public bool IsDarkTaskbar
        {
            get { return !IsLightTaskbar; }
        }

        public Color AccentColor { get; private set; }

        public override string ToString()
        {
            return (IsLightTaskbar ? "浅色任务栏" : "深色任务栏") + "，强调色 " + AccentColor;
        }
    }
}
