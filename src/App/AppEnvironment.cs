using System;
using System.Diagnostics;
using System.Reflection;

namespace AudioSwitch.Host
{
    /// <summary>Information about the current executable, used by startup registration and diagnostics.</summary>
    internal static class AppEnvironment
    {
        public static string ResolveExecutablePath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                // Fall back to the assembly location.
            }

            try
            {
                Assembly entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly != null && !string.IsNullOrEmpty(entryAssembly.Location))
                {
                    return entryAssembly.Location;
                }
            }
            catch
            {
                // Fall back to the command line.
            }

            try
            {
                return Environment.GetCommandLineArgs()[0];
            }
            catch
            {
                return "AudioSwitch.exe";
            }
        }
    }
}
