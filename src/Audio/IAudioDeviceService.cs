using System;
using System.Collections.Generic;

namespace AudioSwitch.Audio
{
    /// <summary>Reads active audio endpoints and changes the system default endpoint.</summary>
    public interface IAudioDeviceService : IDisposable
    {
        /// <summary>Raised when the audio service reports a device or default endpoint change.</summary>
        event EventHandler DevicesChanged;

        /// <summary>
        /// Active playback endpoints first, then active recording endpoints.
        /// Within each flow the current default comes first, followed by name order.
        /// </summary>
        IList<AudioEndpoint> GetEndpoints();

        /// <summary>Gets the current default endpoint id (console role) for a data flow.</summary>
        string GetDefaultEndpointId(AudioDataFlow flow);

        /// <summary>
        /// Endpoint ids in every device state (active/disabled/unplugged/not present).
        /// Used only for safe cleanup of hidden-device preferences: a temporarily
        /// unplugged device must not lose the user's "hide" choice.
        /// </summary>
        IList<string> GetKnownDeviceIds();

        /// <summary>Makes the endpoint the default for console, multimedia and communications roles.</summary>
        void SetDefaultDevice(string deviceId);
    }
}
