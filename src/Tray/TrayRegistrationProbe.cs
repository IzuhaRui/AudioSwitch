using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AudioSwitch.Tray
{
    /// <summary>
    /// Diagnostic helper: tries to add and immediately remove a temporary notification
    /// area icon so the application can report whether the current process is allowed
    /// to talk to the Windows shell (sandboxed processes are not).
    /// </summary>
    public static class TrayRegistrationProbe
    {
        private const int NIM_ADD = 0;
        private const int NIM_DELETE = 2;
        private const int NIF_MESSAGE = 0x1;
        private const int NIF_ICON = 0x2;
        private const int NIF_TIP = 0x4;
        private const int WS_OVERLAPPED = 0;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int extendedStyle, string className, string windowName,
            int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        public static bool TryRegister(out int errorCode, out string errorMessage)
        {
            errorCode = 0;
            errorMessage = null;
            IntPtr window = IntPtr.Zero;

            try
            {
                window = CreateWindowEx(0, "STATIC", "AudioSwitchTrayProbe", WS_OVERLAPPED,
                    0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (window == IntPtr.Zero)
                {
                    errorCode = Marshal.GetLastWin32Error();
                    errorMessage = new Win32Exception(errorCode).Message;
                    return false;
                }

                NOTIFYICONDATA data = new NOTIFYICONDATA();
                data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
                data.hWnd = window;
                data.uID = 0x4E;
                data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
                data.uCallbackMessage = 0x0400 + 1;
                data.hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
                data.szTip = "AudioSwitch 托盘测试";

                bool added = Shell_NotifyIcon(NIM_ADD, ref data);
                int lastError = Marshal.GetLastWin32Error();
                if (added)
                {
                    Shell_NotifyIcon(NIM_DELETE, ref data);
                    return true;
                }

                errorCode = lastError;
                errorMessage = lastError != 0
                    ? new Win32Exception(lastError).Message
                    : "Shell_NotifyIcon 返回失败。";
                return false;
            }
            catch (Exception ex)
            {
                errorCode = Marshal.GetLastWin32Error();
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                if (window != IntPtr.Zero)
                {
                    DestroyWindow(window);
                }
            }
        }

        public static string Describe()
        {
            int errorCode;
            string errorMessage;
            if (TryRegister(out errorCode, out errorMessage))
            {
                return "可用（Shell_NotifyIcon 注册成功）";
            }

            return string.Format("不可用（错误 {0}: {1}）", errorCode, errorMessage);
        }
    }
}
