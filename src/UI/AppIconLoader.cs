using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace AudioSwitch.UI
{
    /// <summary>
    /// Loads the executable icon of an application for the per-app volume page, with a
    /// small cache. Falls back to null when the process path is inaccessible so the UI
    /// can draw a generic MDL2 glyph instead.
    /// </summary>
    internal static class AppIconLoader
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource TryLoad(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath))
            {
                return null;
            }

            lock (SyncRoot)
            {
                ImageSource cached;
                if (Cache.TryGetValue(executablePath, out cached))
                {
                    return cached;
                }
            }

            ImageSource source = null;
            try
            {
                using (Drawing.Icon icon = Drawing.Icon.ExtractAssociatedIcon(executablePath))
                {
                    if (icon != null)
                    {
                        BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle,
                            Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(20, 20));
                        bitmap.Freeze();
                        source = bitmap;
                    }
                }
            }
            catch
            {
                // Missing/inaccessible executable or icon: caller falls back to a glyph.
            }

            lock (SyncRoot)
            {
                Cache[executablePath] = source;
            }

            return source;
        }
    }
}
