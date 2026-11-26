using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using AudioSwitch.Platform;
using AudioSwitch.UI;

namespace AudioSwitch.Tray
{
    /// <summary>Owns a tray icon image and its native handle.</summary>
    public sealed class TrayIconImage : IDisposable
    {
        private readonly IntPtr _handle;
        private bool _disposed;

        internal TrayIconImage(Bitmap bitmap, Icon icon, IntPtr handle)
        {
            Bitmap = bitmap;
            Icon = icon;
            _handle = handle;
        }

        public Icon Icon { get; private set; }

        internal Bitmap Bitmap { get; private set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (Icon != null)
            {
                Icon.Dispose();
                Icon = null;
            }

            if (_handle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_handle);
            }

            if (Bitmap != null)
            {
                Bitmap.Dispose();
                Bitmap = null;
            }
        }
    }

    /// <summary>
    /// Renders the notification area icon from the Segoe MDL2 Assets vector font at the
    /// exact shell icon size, so it stays crisp on every display scale factor. The icon
    /// mirrors the Windows 10 volume icon and follows the current volume / mute state.
    /// </summary>
    public static class TrayIconArtwork
    {
        private const string GlyphFontName = "Segoe MDL2 Assets";
        private const double GlyphSizeRatio = 0.95;

        public static TrayIconImage Create(ThemeInfo theme, int volumePercent, bool muted, ILogger logger)
        {
            return Create(theme, volumePercent, muted, NativeMethods.GetSmallIconSize(), logger);
        }

        public static TrayIconImage Create(ThemeInfo theme, int volumePercent, bool muted, int size, ILogger logger)
        {
            try
            {
                if (size < 8)
                {
                    size = 16;
                }

                Bitmap bitmap = RenderGlyphBitmap(VolumeGlyphs.ForVolume(volumePercent, muted),
                    size, GetIconColor(theme));
                IntPtr handle = bitmap.GetHicon();
                return new TrayIconImage(bitmap, Icon.FromHandle(handle), handle);
            }
            catch (Exception ex)
            {
                logger.Error("绘制托盘图标失败。", ex);
                return null;
            }
        }

        internal static Bitmap RenderGlyphBitmap(string glyph, int size, Color color)
        {
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = new Font(GlyphFontName, (float)Math.Round(size * GlyphSizeRatio, 2),
                GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
            }

            return bitmap;
        }

        private static Color GetIconColor(ThemeInfo theme)
        {
            // Windows draws dark tray icons on a light taskbar and white ones on a dark taskbar.
            return theme != null && theme.IsLightTaskbar
                ? Color.FromArgb(255, 0, 0, 0)
                : Color.FromArgb(255, 255, 255, 255);
        }
    }
}
