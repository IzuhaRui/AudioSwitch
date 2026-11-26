namespace AudioSwitch.UI
{
    /// <summary>
    /// Segoe MDL2 Assets glyphs of the Windows 10 system volume icon. They are vector
    /// font outlines, so they stay crisp at every DPI and are shared by the flyout and
    /// the notification area icon.
    /// </summary>
    internal static class VolumeGlyphs
    {
        public const string Muted = "\uE74F";
        public const string Zero = "\uE992";
        public const string Low = "\uE993";
        public const string Medium = "\uE994";
        public const string High = "\uE995";

        /// <summary>
        /// Glyph actually shown next to the volume slider. A volume of 0% must look like
        /// a plain speaker (Windows sometimes reports "muted" at 0% automatically), while
        /// an explicit mute action (or mute at a non-zero volume) shows the speaker+X.
        /// </summary>
        public static bool ShouldDisplayMuted(int percent, bool muted, bool explicitlyMuted)
        {
            return muted && (percent > 0 || explicitlyMuted);
        }

        public static string GetDisplayGlyph(int percent, bool muted, bool explicitlyMuted)
        {
            return ForVolume(percent, ShouldDisplayMuted(percent, muted, explicitlyMuted));
        }

        public static string ForVolume(int percent, bool muted)
        {
            if (muted)
            {
                return Muted;
            }

            if (percent <= 0)
            {
                return Zero;
            }

            if (percent <= 33)
            {
                return Low;
            }

            if (percent <= 66)
            {
                return Medium;
            }

            return High;
        }
    }
}