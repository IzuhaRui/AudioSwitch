using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AudioSwitch.Platform;

namespace AudioSwitch.Tray
{
    /// <summary>Notification area icon backed by the battle tested WinForms NotifyIcon.</summary>
    public sealed class NotifyIconTrayHost : ITrayIconHost
    {
        private const int MaximumTooltipLength = 63;
        private const int MaximumBalloonTextLength = 250;

        private readonly ILogger _logger;
        private readonly NotifyIcon _notifyIcon;
        private Icon _icon;
        private bool _disposed;

        public NotifyIconTrayHost(ILogger logger)
        {
            _logger = logger;
            _notifyIcon = new NotifyIcon();

            try
            {
                _notifyIcon.MouseUp += OnMouseUp;
                _notifyIcon.Visible = true;
            }
            catch (Exception ex)
            {
                _notifyIcon.Dispose();
                throw new InvalidOperationException("无法创建 Windows 通知区域图标。", ex);
            }
        }

        public event EventHandler Activated;

        public void SetContextMenu(ContextMenuStrip menu)
        {
            _notifyIcon.ContextMenuStrip = menu;
        }

        public void SetIcon(Icon icon)
        {
            _icon = icon;
            _notifyIcon.Icon = icon;
        }

        public void SetTooltip(string tooltip)
        {
            try
            {
                _notifyIcon.Text = Truncate(tooltip, MaximumTooltipLength);
            }
            catch (Exception ex)
            {
                _logger.Error("更新托盘提示文字失败。", ex);
            }
        }

        public void ShowBalloon(string title, string text, TrayNotificationKind kind)
        {
            try
            {
                ToolTipIcon icon;
                switch (kind)
                {
                    case TrayNotificationKind.Warning:
                        icon = ToolTipIcon.Warning;
                        break;
                    case TrayNotificationKind.Error:
                        icon = ToolTipIcon.Error;
                        break;
                    default:
                        icon = ToolTipIcon.Info;
                        break;
                }

                _notifyIcon.ShowBalloonTip(4000, Truncate(title, MaximumTooltipLength),
                    Truncate(text, MaximumBalloonTextLength), icon);
            }
            catch (Exception ex)
            {
                _logger.Error("显示托盘气泡提示失败。", ex);
            }
        }

        public bool TryGetIconRectangle(out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;

            try
            {
                Type notifyIconType = typeof(NotifyIcon);
                FieldInfo windowField = notifyIconType.GetField("window",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object nativeWindow = windowField != null ? windowField.GetValue(_notifyIcon) : null;
                if (nativeWindow == null)
                {
                    return false;
                }

                PropertyInfo handleProperty = nativeWindow.GetType().GetProperty("Handle",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                IntPtr windowHandle = handleProperty != null
                    ? (IntPtr)handleProperty.GetValue(nativeWindow, null)
                    : IntPtr.Zero;

                FieldInfo idField = notifyIconType.GetField("id", BindingFlags.Instance | BindingFlags.NonPublic);
                int iconId = idField != null ? (int)idField.GetValue(_notifyIcon) : 0;

                NativeMethods.NOTIFYICONIDENTIFIER identifier = new NativeMethods.NOTIFYICONIDENTIFIER
                {
                    cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONIDENTIFIER)),
                    hWnd = windowHandle,
                    uID = iconId,
                    guidItem = Guid.Empty
                };

                NativeMethods.RECT nativeRect;
                int hr = NativeMethods.Shell_NotifyIconGetRect(ref identifier, out nativeRect);
                if (hr != 0)
                {
                    return false;
                }

                rectangle = new Rectangle(nativeRect.Left, nativeRect.Top,
                    nativeRect.Right - nativeRect.Left, nativeRect.Bottom - nativeRect.Top);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("读取托盘图标位置失败。", ex);
                return false;
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            EventHandler handler = Activated;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static string Truncate(string text, int maximumLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maximumLength)
            {
                return text;
            }

            return text.Substring(0, maximumLength - 1) + "…";
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
                _notifyIcon.MouseUp -= OnMouseUp;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch
            {
                // Ignore teardown failures.
            }

            _icon = null;
        }
    }
}
