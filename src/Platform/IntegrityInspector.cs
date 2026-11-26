using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AudioSwitch.Platform
{
    public enum ProcessIntegrity
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        System = 4
    }

    /// <summary>
    /// Detects the process integrity level. A Low integrity process (which is what a
    /// sandboxed launcher produces) cannot talk to the Windows shell, so tray icons
    /// fail with ERROR_ACCESS_DENIED. We detect that early and tell the user how to
    /// start the application properly instead of silently showing no icon.
    /// </summary>
    public static class IntegrityInspector
    {
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenIntegrityLevel = 25;
        private const int TokenIsAppContainer = 29;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information,
            int informationLength, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        public static ProcessIntegrity GetCurrentIntegrity()
        {
            IntPtr token = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token))
                {
                    return ProcessIntegrity.Unknown;
                }

                int requiredSize;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out requiredSize);
                if (requiredSize <= 0)
                {
                    requiredSize = 128;
                }

                buffer = Marshal.AllocHGlobal(requiredSize);
                int returnedSize;
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, requiredSize, out returnedSize))
                {
                    return ProcessIntegrity.Unknown;
                }

                // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; } }
                IntPtr sidPointer = Marshal.ReadIntPtr(buffer);
                if (sidPointer == IntPtr.Zero)
                {
                    return ProcessIntegrity.Unknown;
                }

                SecurityIdentifier sid = new SecurityIdentifier(sidPointer);
                switch (sid.Value)
                {
                    case "S-1-16-4096":
                        return ProcessIntegrity.Low;
                    case "S-1-16-8192":
                        return ProcessIntegrity.Medium;
                    case "S-1-16-12288":
                        return ProcessIntegrity.High;
                    case "S-1-16-16384":
                        return ProcessIntegrity.System;
                    default:
                        return ProcessIntegrity.Unknown;
                }
            }
            catch
            {
                return ProcessIntegrity.Unknown;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
            }
        }

        public static bool IsRunningInAppContainer()
        {
            IntPtr token = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token))
                {
                    return false;
                }

                buffer = Marshal.AllocHGlobal(4);
                int returnedSize;
                if (!GetTokenInformation(token, TokenIsAppContainer, buffer, 4, out returnedSize))
                {
                    return false;
                }

                return Marshal.ReadInt32(buffer) != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
            }
        }

        public static string Describe()
        {
            string integrity;
            switch (GetCurrentIntegrity())
            {
                case ProcessIntegrity.Low:
                    integrity = "低 (Low)";
                    break;
                case ProcessIntegrity.Medium:
                    integrity = "中 (Medium)";
                    break;
                case ProcessIntegrity.High:
                    integrity = "高 (High)";
                    break;
                case ProcessIntegrity.System:
                    integrity = "系统 (System)";
                    break;
                default:
                    integrity = "未知";
                    break;
            }

            return integrity + (IsRunningInAppContainer() ? " / AppContainer" : string.Empty);
        }

        /// <summary>
        /// True when the process is sandboxed/restricted in a way that prevents the
        /// Windows shell from accepting a notification area icon.
        /// </summary>
        public static bool IsRestrictedForShellIntegration()
        {
            return GetCurrentIntegrity() == ProcessIntegrity.Low || IsRunningInAppContainer();
        }
    }
}
