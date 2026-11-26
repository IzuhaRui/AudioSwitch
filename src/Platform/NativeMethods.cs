using System;
using System.Runtime.InteropServices;

namespace AudioSwitch.Platform
{
    /// <summary>Central place for the Win32 APIs used by the application.</summary>
    internal static class NativeMethods
    {
        public const int GWL_STYLE = -16;
        public const int WS_THICKFRAME = 0x00040000;

        public const int WM_NCCALCSIZE = 0x0083;
        public const int WM_NCHITTEST = 0x0084;
        public const int HTCLIENT = 1;

        public const int WM_APP = 0x8000;
        public const int MSGFLT_ALLOW = 1;
        public const int SM_CXSMICON = 49;
        public const int SM_CMONITORS = 80;
        public const int SM_CYSMICON = 50;

        public const uint MB_OK = 0x00000000;
        public const uint MB_ICONWARNING = 0x00000030;
        public const uint MB_SETFOREGROUND = 0x00010000;
        public const uint MB_TOPMOST = 0x00040000;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public Guid guidItem;
        }

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr window, int index, int newValue);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter,
            string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeWindowMessageFilterEx(IntPtr window, int message, int action,
            IntPtr changeFilterStruct);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);

        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetricsForDpi(int index, uint dpi);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref MARGINS margins);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("shell32.dll", PreserveSig = true)]
        public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

        public static int GetSmallIconSize()
        {
            int size = 0;
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi > 0)
                {
                    size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
                }
            }
            catch (EntryPointNotFoundException)
            {
                size = 0;
            }

            if (size <= 0)
            {
                size = GetSystemMetrics(SM_CXSMICON);
            }

            return size > 0 ? size : 16;
        }
    }
}
