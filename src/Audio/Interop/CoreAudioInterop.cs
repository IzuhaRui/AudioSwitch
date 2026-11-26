using System;
using System.Runtime.InteropServices;

namespace AudioSwitch.Audio.Interop
{
    public enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    public enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    internal static class AudioIids
    {
        public static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        public static readonly Guid CLSID_CPolicyConfigClient = new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
        public static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        public static readonly Guid IID_IAudioMeterInformation = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    }

    internal static class AudioConstants
    {
        public const int DEVICE_STATE_ACTIVE = 0x00000001;
        public const int DEVICE_STATEMASK_ALL = 0x0000000F;
        public const int STGM_READ = 0x00000000;
        public const int S_OK = 0;
        public const int CLSCTX_ALL = 0x17;

        public const ushort VT_I4 = 3;
        public const ushort VT_UI4 = 19;
        public const ushort VT_LPWSTR = 31;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;

        public PROPERTYKEY(Guid formatId, int propertyId)
        {
            fmtid = formatId;
            pid = propertyId;
        }
    }

    /// <summary>
    /// Native PROPVARIANT. Deliberately declared larger than the 32 bit layout so the
    /// marshaller can never under-allocate when Windows writes the 64 bit layout.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort reserved1;
        [FieldOffset(4)] public ushort reserved2;
        [FieldOffset(6)] public ushort reserved3;
        [FieldOffset(8)] public IntPtr pointerValue;
        [FieldOffset(8)] public int intValue;
        [FieldOffset(8)] public uint uintValue;
        [FieldOffset(8)] public long longValue;
        [FieldOffset(8)] public double doubleValue;
    }

    internal static class PropertyKeys
    {
        public static PROPERTYKEY DeviceFriendlyName
        {
            get { return new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14); }
        }

        public static PROPERTYKEY DeviceDescription
        {
            get { return new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2); }
        }

        public static PROPERTYKEY AudioEndpointFormFactor
        {
            get { return new PROPERTYKEY(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0); }
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback([MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback([MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int Item(int index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int classContext, IntPtr activationParams, out IntPtr instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore properties);

        [PreserveSig]
        int GetId(out IntPtr deviceId);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out int propertyCount);

        [PreserveSig]
        int GetAt(int propertyIndex, out PROPERTYKEY key);

        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }

    /// <summary>
    /// Per-endpoint volume and mute control (IAudioEndpointVolume).
    /// Only the methods used by the application are declared, in vtable order.
    /// </summary>
    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notification);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notification);

        [PreserveSig]
        int GetChannelCount(out int channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(int channel, float level, ref Guid eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(int channel, float level, ref Guid eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(int channel, out float level);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(int channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);

        [PreserveSig]
        int GetVolumeStepInfo(out int step, out int stepCount);

        [PreserveSig]
        int VolumeStepUp(ref Guid eventContext);

        [PreserveSig]
        int VolumeStepDown(ref Guid eventContext);

        [PreserveSig]
        int QueryHardwareSupport(out int hardwareSupportMask);

        [PreserveSig]
        int GetVolumeRange(out float minimum, out float maximum, out float increment);
    }

    /// <summary>
    /// Per-endpoint peak meter (IAudioMeterInformation). Used to show a live level
    /// indicator so the user can see whether the device is actually playing/capturing.
    /// </summary>
    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);

        [PreserveSig]
        int GetMeteringChannelCount(out int channelCount);

        [PreserveSig]
        int GetChannelsPeakValues(int channelCount, IntPtr peakValues);

        [PreserveSig]
        int QueryHardwareSupport(out int hardwareSupportMask);
    }

    /// <summary>
    /// Undocumented but stable Windows policy configuration interface used by the OS
    /// itself to change the default audio endpoint.
    /// </summary>
    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, out IntPtr format);

        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName,
            [MarshalAs(UnmanagedType.Bool)] bool isDefault, out IntPtr format);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName);

        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr endpointFormat, IntPtr mixFormat);

        [PreserveSig]
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName,
            [MarshalAs(UnmanagedType.Bool)] bool isDefault, out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref long period);

        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, out IntPtr mode);

        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);

        [PreserveSig]
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PROPERTYKEY key,
            out PropVariant value);

        [PreserveSig]
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PROPERTYKEY key,
            ref PropVariant value);

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ERole role);

        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceName,
            [MarshalAs(UnmanagedType.Bool)] bool visible);
    }

    [ComVisible(true)]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);

        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int OnDefaultDeviceChanged(EDataFlow flow, ERole role,
            [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);

        [PreserveSig]
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PROPERTYKEY key);
    }

    internal static class CoreAudioNative
    {
        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int PropVariantClear(ref PropVariant variant);

        public static T CreateComObject<T>(Guid clsid) where T : class
        {
            Type type = Type.GetTypeFromCLSID(clsid, true);
            object instance = Activator.CreateInstance(type);
            return (T)instance;
        }

        public static void ReleaseComObject(object instance)
        {
            if (instance == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(instance))
                {
                    Marshal.ReleaseComObject(instance);
                }
            }
            catch
            {
                // Releasing is best effort only.
            }
        }

        public static string ReadString(IPropertyStore store, PROPERTYKEY key)
        {
            if (store == null)
            {
                return null;
            }

            PropVariant value;
            int hr = store.GetValue(ref key, out value);
            if (hr != AudioConstants.S_OK)
            {
                return null;
            }

            try
            {
                if (value.vt == AudioConstants.VT_LPWSTR && value.pointerValue != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(value.pointerValue);
                }

                return null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }

        public static uint? ReadUInt32(IPropertyStore store, PROPERTYKEY key)
        {
            if (store == null)
            {
                return null;
            }

            PropVariant value;
            int hr = store.GetValue(ref key, out value);
            if (hr != AudioConstants.S_OK)
            {
                return null;
            }

            try
            {
                if (value.vt == AudioConstants.VT_UI4)
                {
                    return value.uintValue;
                }

                if (value.vt == AudioConstants.VT_I4)
                {
                    return unchecked((uint)value.intValue);
                }

                return null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
    }
}
