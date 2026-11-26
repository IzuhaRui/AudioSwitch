using System;
using System.Runtime.InteropServices;

namespace AudioSwitch.Platform
{
    /// <summary>
    /// Plays the Windows "Default Beep" system sound event - the same short sound
    /// Windows itself produces when the volume is changed with the volume keys/OSD.
    /// It respects the user's current sound scheme (Control Panel -> Sound -> Default Beep).
    /// </summary>
    public static class SystemSoundFeedback
    {
        private const uint MB_OK = 0x00000000;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MessageBeep(uint type);

        public static void PlayVolumeFeedback()
        {
            try
            {
                MessageBeep(MB_OK);
            }
            catch
            {
                // Sound feedback must never break volume changes.
            }
        }
    }
}
