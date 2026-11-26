using System;
using AudioSwitch.Platform;

namespace AudioSwitch.Host
{
    /// <summary>Text shown to the user when the application cannot start normally.</summary>
    internal static class UiMessages
    {
        public static void ShowFatal(ILogger logger, string title, string message)
        {
            logger.Error(message);

            if (string.Equals(Environment.GetEnvironmentVariable("AUDIOSWITCH_SILENT_ERRORS"), "1",
                StringComparison.Ordinal))
            {
                return;
            }

            NativeMethods.MessageBox(IntPtr.Zero, message, title,
                NativeMethods.MB_OK | NativeMethods.MB_ICONWARNING | NativeMethods.MB_TOPMOST
                | NativeMethods.MB_SETFOREGROUND);
        }

        public static string BuildRestrictedEnvironmentMessage(string executablePath)
        {
            string directory = executablePath;
            string fileName = "AudioSwitch.exe";
            try
            {
                string parsedDirectory = System.IO.Path.GetDirectoryName(executablePath);
                if (!string.IsNullOrEmpty(parsedDirectory))
                {
                    directory = parsedDirectory;
                }

                string parsedFileName = System.IO.Path.GetFileName(executablePath);
                if (!string.IsNullOrEmpty(parsedFileName))
                {
                    fileName = parsedFileName;
                }
            }
            catch
            {
                // Keep the raw path fallback.
            }

            return "音频设备切换器无法在任务栏显示图标。\n\n"
                + "原因：程序当前以“受限权限 / 沙箱”方式运行（常见于从 AI 助手的对话界面里"
                + "直接点击文件卡片打开），Windows 不允许这种低权限进程创建通知区域图标。\n\n"
                + "请这样启动：\n"
                + "1. 打开 Windows 的“文件资源管理器”；\n"
                + "2. 打开文件夹：\n   " + directory + "\n"
                + "3. 双击其中的 " + fileName + " 运行。\n\n"
                + "（不要从 AI 助手里直接“打开”这个 exe。）";
        }

        public static string BuildSecondInstanceSignalFailureMessage(string logPath)
        {
            return "音频设备切换器已经在运行，但无法让它显示面板。\n\n"
                + "可能的原因：\n"
                + "1. 正在运行的是以管理员身份启动的旧版本（升级过渡）；\n"
                + "2. 系统安全策略阻止了进程间通知。\n\n"
                + "请查看任务栏右下角（包括 ^ 隐藏区）的托盘图标：\n"
                + "· 有图标：可以直接使用，或右键退出后重新启动；\n"
                + "· 没有图标：请在任务管理器中结束旧的 AudioSwitch.exe 后再重新启动。\n\n"
                + "日志文件：" + logPath;
        }

        public static string BuildTrayUnavailableMessage(int errorCode, string errorMessage, string logPath)
        {
            return "无法创建任务栏通知区域图标。\n\n"
                + "错误代码：" + errorCode + "\n"
                + "详细信息：" + (errorMessage ?? "未知错误") + "\n\n"
                + "请确认 Windows 资源管理器正在运行，然后在“文件资源管理器”中双击 AudioSwitch.exe 重新启动。\n\n"
                + "日志文件：" + logPath;
        }

        public static string BuildStartupFailureMessage(Exception exception, string logPath)
        {
            return "音频设备切换器启动失败。\n\n"
                + exception.Message + "\n\n"
                + "日志文件：" + logPath;
        }
    }
}
