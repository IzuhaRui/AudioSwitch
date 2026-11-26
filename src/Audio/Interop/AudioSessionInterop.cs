using System;
using System.Runtime.InteropServices;

namespace AudioSwitch.Audio.Interop
{
    /// <summary>
    /// Core Audio session interfaces used by the per-application volume page.
    /// Only the methods used by the application are declared, in vtable order.
    /// </summary>
    internal static class AudioSessionIids
    {
        public static readonly Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        public static readonly Guid IID_IAudioSessionEnumerator = new Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8");
        public static readonly Guid IID_IAudioSessionControl = new Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD");
        public static readonly Guid IID_IAudioSessionControl2 = new Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
        public static readonly Guid IID_ISimpleAudioVolume = new Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8");
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(ref Guid audioSessionGuid, int streamFlags,
            out IAudioSessionControl sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(ref Guid audioSessionGuid, int streamFlags,
            out ISimpleAudioVolume audioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);

        [PreserveSig]
        int RegisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int UnregisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int RegisterDuckNotification(string sessionId, IntPtr duckNotification);

        [PreserveSig]
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);

        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName(out IntPtr name);

        [PreserveSig]
        int SetDisplayName(string name, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath(out IntPtr iconPath);

        [PreserveSig]
        int SetIconPath(string iconPath, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingId);

        [PreserveSig]
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr sessionNotification);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName(out IntPtr name);

        [PreserveSig]
        int SetDisplayName(string name, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath(out IntPtr iconPath);

        [PreserveSig]
        int SetIconPath(string iconPath, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingId);

        [PreserveSig]
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int GetSessionIdentifier(out IntPtr sessionIdentifier);

        [PreserveSig]
        int GetSessionInstanceIdentifier(out IntPtr sessionInstanceIdentifier);

        [PreserveSig]
        int GetProcessId(out int processId);

        [PreserveSig]
        int IsSystemSoundsSession();

        [PreserveSig]
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig]
        int SetMasterVolume(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolume(out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
