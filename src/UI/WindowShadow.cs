using System;
using System.Windows;
using System.Windows.Interop;
using AudioSwitch.Platform;

namespace AudioSwitch.UI
{
    /// <summary>
    /// Gives a borderless, non-transparent WPF window the native Windows 10 drop shadow.
    /// The window keeps crisp ClearType text and full hardware acceleration because it
    /// stays an opaque window; only the DWM frame is extended by one pixel.
    /// </summary>
    internal static class WindowShadow
    {
        public static void EnableDwmShadow(Window window, bool useDarkFrame)
        {
            if (window == null)
            {
                return;
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(handle, NativeMethods.GWL_STYLE, style | NativeMethods.WS_THICKFRAME);

            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != null)
            {
                source.AddHook(WndProc);
            }

            NativeMethods.MARGINS margins = new NativeMethods.MARGINS
            {
                cxLeftWidth = 1,
                cxRightWidth = 1,
                cyTopHeight = 1,
                cyBottomHeight = 1
            };
            NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins);

            UpdateDarkFrame(window, useDarkFrame);
        }

        /// <summary>
        /// Windows 10 1809+: lets the DWM frame/border follow the dark theme instead of
        /// always drawing the light glass border. Safe to call when the handle is not
        /// created yet (for example from a theme change handler).
        /// </summary>
        public static void UpdateDarkFrame(Window window, bool useDarkFrame)
        {
            if (window == null)
            {
                return;
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int darkMode = useDarkFrame ? 1 : 0;
            try
            {
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref darkMode, sizeof(int));
            }
            catch (EntryPointNotFoundException)
            {
                // Older Windows builds do not support the attribute.
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Remove the non-client area added by WS_THICKFRAME so the WPF content
            // covers the whole window, while DWM keeps drawing the native shadow.
            if (message == NativeMethods.WM_NCCALCSIZE && wParam != IntPtr.Zero)
            {
                handled = true;
                return IntPtr.Zero;
            }

            // The window is a fixed-position flyout, so every point is client area.
            if (message == NativeMethods.WM_NCHITTEST)
            {
                handled = true;
                return new IntPtr(NativeMethods.HTCLIENT);
            }

            return IntPtr.Zero;
        }
    }
}
