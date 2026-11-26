using System;
using System.Runtime.InteropServices;
using AudioSwitch.Audio.Interop;

namespace AudioSwitch.Audio
{
    /// <summary>Receives device change notifications from the Windows audio service.</summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class AudioNotificationClient : IMMNotificationClient
    {
        private readonly Action _onChanged;

        public AudioNotificationClient(Action onChanged)
        {
            if (onChanged == null)
            {
                throw new ArgumentNullException("onChanged");
            }

            _onChanged = onChanged;
        }

        public int OnDeviceStateChanged(string deviceId, int newState)
        {
            Notify();
            return AudioConstants.S_OK;
        }

        public int OnDeviceAdded(string deviceId)
        {
            Notify();
            return AudioConstants.S_OK;
        }

        public int OnDeviceRemoved(string deviceId)
        {
            Notify();
            return AudioConstants.S_OK;
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
        {
            Notify();
            return AudioConstants.S_OK;
        }

        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key)
        {
            Notify();
            return AudioConstants.S_OK;
        }

        private void Notify()
        {
            try
            {
                _onChanged();
            }
            catch
            {
                // A notification handler must never bubble an exception back into COM.
            }
        }
    }
}
