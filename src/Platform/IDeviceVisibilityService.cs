using System;
using System.Collections.Generic;

namespace AudioSwitch.Platform
{
    /// <summary>
    /// Keeps track of devices the user chose to hide from the flyout panel. The
    /// selection is persisted so it survives restarts; the tray menu can restore all
    /// devices at once.
    /// </summary>
    public interface IDeviceVisibilityService : IDisposable
    {
        /// <summary>Raised when the hidden-device set changes.</summary>
        event EventHandler Changed;

        bool HasHiddenDevices { get; }

        bool IsHidden(string deviceId);

        /// <summary>Hides a device. Returns false when it was already hidden.</summary>
        bool Hide(string deviceId);

        /// <summary>Shows every hidden device again. Returns false when nothing was hidden.</summary>
        bool ShowAll();

        /// <summary>Drops stored ids that no longer exist; returns how many were removed.</summary>
        int Prune(IEnumerable<string> knownDeviceIds);
    }
}
