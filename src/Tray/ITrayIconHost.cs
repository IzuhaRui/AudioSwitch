using System;
using System.Drawing;
using System.Windows.Forms;

namespace AudioSwitch.Tray
{
    public enum TrayNotificationKind
    {
        Info = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>Abstraction over the Windows notification area icon.</summary>
    public interface ITrayIconHost : IDisposable
    {
        /// <summary>Raised when the user left-clicks the tray icon.</summary>
        event EventHandler Activated;

        void SetContextMenu(ContextMenuStrip menu);

        void SetIcon(Icon icon);

        void SetTooltip(string tooltip);

        void ShowBalloon(string title, string text, TrayNotificationKind kind);

        /// <summary>Best effort: returns the icon rectangle in screen pixels when the shell knows it.</summary>
        bool TryGetIconRectangle(out Rectangle rectangle);
    }
}
