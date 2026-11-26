using System;
using System.IO;

namespace AudioSwitch.Host
{
    internal enum AppMode
    {
        Normal,
        Diag,
        Preview,
        Capture,
        Screenshot,
        Verify,
        CheckEnvironment,
        UiTest,
        TrayIcons,
        TrayTest,
        AppsTest
    }

    /// <summary>Parsed command line. Normal startup has no arguments.</summary>
    internal sealed class CommandLineOptions
    {
        private CommandLineOptions()
        {
            Mode = AppMode.Normal;
        }

        public AppMode Mode { get; private set; }

        public string OutputPath { get; private set; }

        public bool Demo { get; private set; }

        public bool SwitchTest { get; private set; }

        public bool VolumeTest { get; private set; }

        public bool ShowFlyoutOnStartup { get; private set; }
        public bool AppsPage { get; private set; }

        public static CommandLineOptions Parse(string[] args)
        {
            CommandLineOptions options = new CommandLineOptions();
            if (args == null || args.Length == 0)
            {
                return options;
            }

            string mode = (args[0] ?? string.Empty).Trim().ToLowerInvariant();
            switch (mode)
            {
                case "--diag":
                    options.Mode = AppMode.Diag;
                    break;
                case "--preview":
                    options.Mode = AppMode.Preview;
                    break;
                case "--capture":
                    options.Mode = AppMode.Capture;
                    break;
                case "--screenshot":
                    options.Mode = AppMode.Screenshot;
                    break;
                case "--verify":
                    options.Mode = AppMode.Verify;
                    break;
                case "--check-env":
                    options.Mode = AppMode.CheckEnvironment;
                    break;
                case "--uitest":
                    options.Mode = AppMode.UiTest;
                    break;
                case "--trayicons":
                    options.Mode = AppMode.TrayIcons;
                    break;
                case "--traytest":
                    options.Mode = AppMode.TrayTest;
                    break;
                case "--appstest":
                    options.Mode = AppMode.AppsTest;
                    break;
            }

            if (options.Mode != AppMode.Normal)
            {
                options.OutputPath = ReadPathArgument(args, 1, GetDefaultOutputPath(options.Mode));
            }

            options.Demo = HasFlag(args, "--demo");
            options.SwitchTest = HasFlag(args, "--switch-test");
            options.VolumeTest = HasFlag(args, "--volume-test");
            options.ShowFlyoutOnStartup = HasFlag(args, "--show");
            options.AppsPage = HasFlag(args, "--apps");
            return options;
        }

        public static bool HasFlag(string[] args, string value)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string argument in args)
            {
                if (string.Equals(argument, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadPathArgument(string[] args, int index, string fallback)
        {
            if (args != null && args.Length > index)
            {
                string candidate = args[index];
                if (!string.IsNullOrWhiteSpace(candidate) && !candidate.StartsWith("--", StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return fallback;
        }

        private static string GetDefaultOutputPath(AppMode mode)
        {
            string temp = Path.GetTempPath();
            switch (mode)
            {
                case AppMode.Diag:
                    return Path.Combine(temp, "AudioSwitch-diag.txt");
                case AppMode.Preview:
                    return Path.Combine(temp, "AudioSwitch-preview.png");
                case AppMode.Capture:
                    return Path.Combine(temp, "AudioSwitch-capture.png");
                case AppMode.Screenshot:
                    return Path.Combine(temp, "AudioSwitch-screenshot.png");
                case AppMode.Verify:
                    return Path.Combine(temp, "AudioSwitch-verify.txt");
                case AppMode.CheckEnvironment:
                    return Path.Combine(temp, "AudioSwitch-env.txt");
                case AppMode.UiTest:
                    return Path.Combine(temp, "AudioSwitch-uitest.txt");
                case AppMode.TrayIcons:
                    return Path.Combine(temp, "AudioSwitch-trayicons.png");
                case AppMode.TrayTest:
                    return Path.Combine(temp, "AudioSwitch-traytest.txt");
                case AppMode.AppsTest:
                    return Path.Combine(temp, "AudioSwitch-appstest.txt");
                default:
                    return Path.Combine(temp, "AudioSwitch-output.txt");
            }
        }
    }
}
