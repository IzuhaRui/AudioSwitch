using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using AudioSwitch.Host;
using AudioSwitch.Audio;
using AudioSwitch.Platform;
using AudioSwitch.UI;

namespace AudioSwitch.UnitTests
{
    /// <summary>
    /// Small dependency-free unit test runner (no NuGet / no test framework needed).
    /// Covers pure logic that used to be reachable only through integration smoke tests:
    /// hidden-device persistence/pruning, glyph mapping, volume percent clamping and the
    /// flyout view model's filtering / empty-section behaviour.
    ///
    /// Run via test.ps1; the process writes a report and returns 0 when all tests pass.
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly StringBuilder Details = new StringBuilder();

        private static int Main(string[] args)
        {
            string reportPath = args.Length > 0 ? args[0] : null;

            Details.AppendLine("AudioSwitch 单元测试");
            Details.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Details.AppendLine();

            Run("DeviceVisibility: hide / case-insensitive / persistence / show all",
                TestVisibilityBasics);
            Run("DeviceVisibility: stale id pruning", TestVisibilityPruning);
            Run("DeviceVisibility: tolerant settings parsing", TestVisibilityParsing);
            Run("DeviceVisibility: empty/unknown enumeration never erases settings",
                TestVisibilityPruneFailSafe);
            Run("DeviceVisibility: Changed event fires", TestVisibilityEvents);
            Run("VolumeGlyphs: volume tiers and mute", TestVolumeGlyphs);
            Run("VolumeGlyphs: 0% hides auto-mute X unless user clicked mute",
                TestVolumeGlyphDisplay);
            Run("AudioVolumeState: percent clamping", TestVolumePercent);
            Run("FlyoutViewModel: filtering and restore", TestViewModelFiltering);
            Run("FlyoutViewModel: hide device via view model", TestViewModelHide);
            Run("AppVolumeViewModel: session list / volume / mute", TestAppVolumeViewModel);
            Run("SingleInstance: message-window signalling across threads", TestSingleInstanceSignalling);
            Run("SingleInstance: early show request is buffered before subscription",
                TestSingleInstancePendingRequest);

            Details.AppendLine();
            Details.AppendLine("通过: " + _passed + "，失败: " + _failed);
            Details.AppendLine("结果: " + (_failed == 0 ? "通过" : "失败"));

            string report = Details.ToString();
            Console.WriteLine(report);
            if (!string.IsNullOrEmpty(reportPath))
            {
                File.WriteAllText(reportPath, report, new UTF8Encoding(false));
            }

            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Details.AppendLine("[PASS] " + name);
            }
            catch (Exception exception)
            {
                _failed++;
                Details.AppendLine("[FAIL] " + name + "：" + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + "（期望 " + expected + "，实际 " + actual + "）");
            }
        }

        // ------------------------------------------------------------------ tests

        private static void TestVisibilityBasics()
        {
            InMemorySettings settings = new InMemorySettings();
            NullLogger logger = new NullLogger();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, logger);

            Assert(!visibility.HasHiddenDevices, "初始不应有隐藏设备");
            Assert(!visibility.IsHidden(null), "null id 不应算隐藏");

            Assert(visibility.Hide("demo-playback-1"), "第一次隐藏应返回 true");
            Assert(!visibility.Hide("demo-playback-1"), "重复隐藏应返回 false");
            Assert(visibility.IsHidden("DEMO-PLAYBACK-1"), "设备 id 比较应忽略大小写");
            Assert(visibility.HasHiddenDevices, "隐藏后 HasHiddenDevices 应为 true");

            DeviceVisibilityService reloaded = new DeviceVisibilityService(settings, logger);
            Assert(reloaded.IsHidden("demo-playback-1"), "隐藏选择应持久化，重启/重建后仍隐藏");

            Assert(reloaded.ShowAll(), "ShowAll 应返回 true");
            Assert(!reloaded.HasHiddenDevices, "ShowAll 后不应再有隐藏设备");
            DeviceVisibilityService reloaded2 = new DeviceVisibilityService(settings, logger);
            Assert(!reloaded2.IsHidden("demo-playback-1"), "ShowAll 结果应持久化");
            Assert(!reloaded2.ShowAll(), "没有隐藏设备时 ShowAll 应返回 false");
        }

        private static void TestVisibilityPruning()
        {
            InMemorySettings settings = new InMemorySettings();
            NullLogger logger = new NullLogger();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, logger);

            visibility.Hide("dev-a");
            visibility.Hide("dev-b");
            visibility.Hide("dev-c");

            int removed = visibility.Prune(new[] { "dev-a", "dev-c" });
            AssertEqual(1, removed, "应清理 1 个失效记录");
            Assert(visibility.IsHidden("dev-a") && visibility.IsHidden("dev-c"), "仍存在的设备应保持隐藏");
            Assert(!visibility.IsHidden("dev-b"), "失效设备应被清理");

            DeviceVisibilityService reloaded = new DeviceVisibilityService(settings, logger);
            Assert(!reloaded.IsHidden("dev-b"), "清理结果应持久化");
            AssertEqual(0, visibility.Prune(new[] { "dev-a", "dev-c" }), "没有失效记录时 Prune 不应删除");
        }

        private static void TestVisibilityPruneFailSafe()
        {
            InMemorySettings settings = new InMemorySettings();
            NullLogger logger = new NullLogger();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, logger);
            visibility.Hide("dev-keep");

            // Simulates an audio-service enumeration failure / empty device list: the
            // user's hide preference must survive untouched.
            AssertEqual(0, visibility.Prune(null), "null 列表不应删除隐藏设置");
            AssertEqual(0, visibility.Prune(new string[0]), "空列表不应删除隐藏设置");
            Assert(visibility.IsHidden("dev-keep"), "失败兜底不能丢失隐藏设置");

            // A non-empty known list that genuinely misses the id is a real removal.
            AssertEqual(1, visibility.Prune(new[] { "dev-other" }), "非空列表应清理失效 id");
            Assert(!visibility.IsHidden("dev-keep"), "失效 id 应被清理");
        }

        private static void TestVisibilityParsing()
        {
            InMemorySettings settings = new InMemorySettings();
            settings.SetString("HiddenDevices", " ;DEMO-PLAYBACK-1;; demo-recording-2 ; ");
            NullLogger logger = new NullLogger();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, logger);

            Assert(visibility.IsHidden("demo-playback-1"), "应解析出 demo-playback-1");
            Assert(visibility.IsHidden("DEMO-RECORDING-2"), "应解析出 demo-recording-2");
            AssertEqual(2, CountHidden(visibility), "应忽略空项并去重");
        }

        private static void TestVisibilityEvents()
        {
            InMemorySettings settings = new InMemorySettings();
            NullLogger logger = new NullLogger();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, logger);
            int changed = 0;
            visibility.Changed += delegate { changed++; };

            visibility.Hide("dev-a");
            AssertEqual(1, changed, "Hide 应触发 Changed");
            visibility.Hide("dev-a");
            AssertEqual(1, changed, "重复 Hide 不应触发 Changed");
            visibility.Prune(new[] { "dev-other" });
            AssertEqual(2, changed, "Prune 删除记录应触发 Changed");
            visibility.Hide("dev-b");
            AssertEqual(3, changed, "再次隐藏应触发 Changed");
            visibility.ShowAll();
            AssertEqual(4, changed, "ShowAll 应触发 Changed");
        }

        private static void TestVolumeGlyphs()
        {
            AssertEqual(VolumeGlyphs.Muted, VolumeGlyphs.ForVolume(50, true), "静音图标");
            AssertEqual(VolumeGlyphs.Zero, VolumeGlyphs.ForVolume(0, false), "0% 图标");
            AssertEqual(VolumeGlyphs.Low, VolumeGlyphs.ForVolume(1, false), "1% 应为低音量");
            AssertEqual(VolumeGlyphs.Low, VolumeGlyphs.ForVolume(33, false), "33% 应为低音量");
            AssertEqual(VolumeGlyphs.Medium, VolumeGlyphs.ForVolume(34, false), "34% 应为中音量");
            AssertEqual(VolumeGlyphs.Medium, VolumeGlyphs.ForVolume(66, false), "66% 应为中音量");
            AssertEqual(VolumeGlyphs.High, VolumeGlyphs.ForVolume(67, false), "67% 应为高音量");
            AssertEqual(VolumeGlyphs.High, VolumeGlyphs.ForVolume(100, false), "100% 应为高音量");
            AssertEqual(VolumeGlyphs.High, VolumeGlyphs.ForVolume(150, false), "超出范围按高音量处理");
        }

        private static void TestVolumeGlyphDisplay()
        {
            // Windows may report muted=true at 0%: a plain speaker is expected unless the
            // user explicitly clicked the mute button.
            AssertEqual(VolumeGlyphs.Zero,
                VolumeGlyphs.GetDisplayGlyph(0, true, false), "0% 自动静音应显示普通喇叭");
            AssertEqual(VolumeGlyphs.Muted,
                VolumeGlyphs.GetDisplayGlyph(0, true, true), "0% 手动静音应显示喇叭+×");
            AssertEqual(VolumeGlyphs.Zero,
                VolumeGlyphs.GetDisplayGlyph(0, false, false), "0% 未静音应显示普通喇叭");
            AssertEqual(VolumeGlyphs.Muted,
                VolumeGlyphs.GetDisplayGlyph(50, true, false), "非 0% 的静音应显示喇叭+×");
            AssertEqual(VolumeGlyphs.Medium,
                VolumeGlyphs.GetDisplayGlyph(50, false, false), "非 0% 未静音显示声波");
        }

        private static void TestVolumePercent()
        {
            AssertEqual(0, new AudioVolumeState { Volume = 0f }.Percent, "0% 音量");
            AssertEqual(50, new AudioVolumeState { Volume = 0.5f }.Percent, "50% 音量");
            AssertEqual(66, new AudioVolumeState { Volume = 0.656f }.Percent, "四舍五入到 66%");
            AssertEqual(100, new AudioVolumeState { Volume = 1f }.Percent, "100% 音量");
            AssertEqual(0, new AudioVolumeState { Volume = -0.5f }.Percent, "负数向下限幅");
            AssertEqual(100, new AudioVolumeState { Volume = 1.5f }.Percent, "超出 1 向上限幅");
        }

        private static void TestViewModelFiltering()
        {
            FakeDeviceService audio = new FakeDeviceService();
            FakeVolumeService volume = new FakeVolumeService();
            InMemorySettings settings = new InMemorySettings();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, new NullLogger());
            FlyoutViewModel viewModel = new FlyoutViewModel(audio, volume, visibility);

            viewModel.Refresh();
            AssertEqual(2, viewModel.Sections.Count, "应有输出/输入两个分组");
            AssertEqual(2, viewModel.Sections[0].Items.Count, "输出分组应有 2 个设备");
            AssertEqual(2, viewModel.Sections[1].Items.Count, "输入分组应有 2 个设备");

            visibility.Hide("playback-b");
            viewModel.Refresh();
            AssertEqual(1, viewModel.Sections[0].Items.Count, "隐藏一个输出设备后应只剩 1 个");
            AssertEqual("playback-a", viewModel.Sections[0].Items[0].Id, "应保留未隐藏设备");

            visibility.Hide("playback-a");
            viewModel.Refresh();
            AssertEqual(0, viewModel.Sections[0].Items.Count, "输出分组应被清空");
            Assert(viewModel.Sections[0].EmptyText.Contains("已隐藏全部设备"), "全部隐藏时提示应说明如何恢复");

            Assert(viewModel.RestoreAllDevices(), "RestoreAllDevices 应返回 true");
            AssertEqual(2, viewModel.Sections[0].Items.Count, "恢复后输出分组应恢复 2 个设备");
        }

        private static void TestAppVolumeViewModel()
        {
            DemoAudioSessionService sessions = new DemoAudioSessionService();
            AppVolumeViewModel viewModel = new AppVolumeViewModel(sessions);
            viewModel.Refresh();

            AssertEqual(4, viewModel.Items.Count, "演示数据应有 4 个应用会话");
            Assert(viewModel.Items[0].IsSystemSounds, "系统声音应排在最前面");
            AssertEqual("系统声音", viewModel.Items[0].Name, "系统声音名称");
            AssertEqual("浏览器", viewModel.Items[1].Name, "第二个应用应为浏览器");

            AppVolumeItemViewModel browser = viewModel.Items[1];
            Assert(viewModel.TrySetVolume(browser, 0.33f), "设置应用音量应成功");
            Assert(viewModel.TrySetMute(browser, true), "设置应用静音应成功");
            viewModel.Refresh();
            AppVolumeItemViewModel refreshed = viewModel.Items[1];
            AssertEqual(33, refreshed.Percent, "应用音量应更新为 33%");
            Assert(refreshed.Muted, "应用静音状态应更新");
            Assert(!viewModel.TrySetVolume(null, 0.5f), "null 应用不应成功");

            float peak;
            Assert(viewModel.TryGetPeakValue(refreshed, out peak), "应用电平应可读取");
            Assert(peak > 0.3f, "浏览器示例电平应大于 0.3");
        }

        private static void TestSingleInstanceSignalling()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string mutexName = @"Local\AudioSwitch.UnitTests.Mutex." + suffix;
            string eventName = @"Local\AudioSwitch.UnitTests.Event." + suffix;
            string windowTitle = "AudioSwitch.UnitTests." + suffix;

            Exception failure = null;
            bool primaryWasPrimary = false;
            bool secondaryWasSecondary = false;
            bool signalled = false;
            int received = 0;

            ManualResetEventSlim primaryReady = new ManualResetEventSlim(false);

            Thread primaryThread = new Thread(delegate()
            {
                try
                {
                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    using (SingleInstanceGuard guard = SingleInstanceGuard.TryAcquire(dispatcher,
                        mutexName, eventName, windowTitle))
                    {
                        primaryWasPrimary = guard.IsPrimary;
                        guard.ShowRequested += delegate { Interlocked.Increment(ref received); };
                        primaryReady.Set();

                        DispatcherFrame frame = new DispatcherFrame();
                        DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background,
                            dispatcher)
                        {
                            Interval = TimeSpan.FromMilliseconds(1200)
                        };
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            frame.Continue = false;
                        };
                        timer.Start();
                        Dispatcher.PushFrame(frame);
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                    primaryReady.Set();
                }
            });
            primaryThread.SetApartmentState(ApartmentState.STA);
            primaryThread.IsBackground = true;
            primaryThread.Start();

            if (!primaryReady.Wait(3000))
            {
                throw new Exception("主实例线程未在 3 秒内就绪");
            }

            Thread secondaryThread = new Thread(delegate()
            {
                try
                {
                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    using (SingleInstanceGuard guard = SingleInstanceGuard.TryAcquire(dispatcher,
                        mutexName, eventName, windowTitle))
                    {
                        secondaryWasSecondary = !guard.IsPrimary;
                        signalled = guard.SignalPrimary();
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            secondaryThread.SetApartmentState(ApartmentState.STA);
            secondaryThread.IsBackground = true;
            secondaryThread.Start();
            secondaryThread.Join(3000);
            primaryThread.Join(4000);

            if (failure != null)
            {
                throw new Exception("单实例测试异常：" + failure.Message);
            }

            Assert(primaryWasPrimary, "第一个实例应为主实例");
            Assert(secondaryWasSecondary, "第二个实例应识别出已有实例");
            Assert(signalled, "第二个实例应能向主实例发送显示请求");
            Assert(received > 0, "主实例应收到显示请求（message-only window）");
        }

        private static void TestSingleInstancePendingRequest()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string mutexName = @"Local\AudioSwitch.UnitTests.PendingMutex." + suffix;
            string eventName = @"Local\AudioSwitch.UnitTests.PendingEvent." + suffix;
            string windowTitle = "AudioSwitch.UnitTests.Pending." + suffix;

            Exception failure = null;
            bool pendingAfterEarlySignal = false;
            bool secondConsumeWasEmpty = false;

            ManualResetEventSlim primaryReady = new ManualResetEventSlim(false);

            Thread primaryThread = new Thread(delegate()
            {
                try
                {
                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    using (SingleInstanceGuard guard = SingleInstanceGuard.TryAcquire(dispatcher,
                        mutexName, eventName, windowTitle))
                    {
                        primaryReady.Set();

                        // Do NOT subscribe to ShowRequested here: this is exactly the
                        // startup race window the buffering is meant to cover.
                        DispatcherFrame frame = new DispatcherFrame();
                        DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background,
                            dispatcher)
                        {
                            Interval = TimeSpan.FromMilliseconds(900)
                        };
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            frame.Continue = false;
                        };
                        timer.Start();
                        Dispatcher.PushFrame(frame);

                        pendingAfterEarlySignal = guard.ConsumePendingShowRequest();
                        secondConsumeWasEmpty = !guard.ConsumePendingShowRequest();
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                    primaryReady.Set();
                }
            });
            primaryThread.SetApartmentState(ApartmentState.STA);
            primaryThread.IsBackground = true;
            primaryThread.Start();

            if (!primaryReady.Wait(3000))
            {
                throw new Exception("主实例线程未在 3 秒内就绪");
            }

            Thread.Sleep(100);
            Thread secondaryThread = new Thread(delegate()
            {
                try
                {
                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    using (SingleInstanceGuard guard = SingleInstanceGuard.TryAcquire(dispatcher,
                        mutexName, eventName, windowTitle))
                    {
                        guard.SignalPrimary();
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            secondaryThread.SetApartmentState(ApartmentState.STA);
            secondaryThread.IsBackground = true;
            secondaryThread.Start();
            secondaryThread.Join(3000);

            primaryThread.Join(4000);

            if (failure != null)
            {
                throw new Exception("单实例缓冲测试异常：" + failure.Message);
            }

            Assert(pendingAfterEarlySignal, "启动早到的显示请求应被缓冲并在订阅/启动后取回");
            Assert(secondConsumeWasEmpty, "缓冲请求应只能被消费一次");
        }

        private static void TestViewModelHide()
        {
            FakeDeviceService audio = new FakeDeviceService();
            FakeVolumeService volume = new FakeVolumeService();
            InMemorySettings settings = new InMemorySettings();
            DeviceVisibilityService visibility = new DeviceVisibilityService(settings, new NullLogger());
            FlyoutViewModel viewModel = new FlyoutViewModel(audio, volume, visibility);
            viewModel.Refresh();

            DeviceItemViewModel first = viewModel.Sections[1].Items[0];
            Assert(viewModel.HideDevice(first), "HideDevice 应成功");
            AssertEqual(1, viewModel.Sections[1].Items.Count, "输入分组应少一个设备");
            Assert(!viewModel.HideDevice(first), "重复隐藏同一设备应返回 false");
        }

        private static int CountHidden(DeviceVisibilityService visibility)
        {
            int count = 0;
            foreach (string id in new[] { "demo-playback-1", "demo-recording-2" })
            {
                if (visibility.IsHidden(id))
                {
                    count++;
                }
            }

            return count;
        }
    }

    internal sealed class InMemorySettings : IAppSettings
    {
        private readonly Dictionary<string, bool> _flags =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _strings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool GetFlag(string name, bool defaultValue)
        {
            bool value;
            return _flags.TryGetValue(name, out value) ? value : defaultValue;
        }

        public void SetFlag(string name, bool value)
        {
            _flags[name] = value;
        }

        public string GetString(string name, string defaultValue)
        {
            string value;
            return _strings.TryGetValue(name, out value) ? value : defaultValue;
        }

        public void SetString(string name, string value)
        {
            _strings[name] = value;
        }
    }

    internal sealed class NullLogger : ILogger
    {
        public string LogFilePath
        {
            get { return null; }
        }

        public void Info(string message)
        {
        }

        public void Error(string message, Exception exception = null)
        {
        }
    }

    internal sealed class FakeDeviceService : IAudioDeviceService
    {
        private readonly List<AudioEndpoint> _endpoints = new List<AudioEndpoint>
        {
            new AudioEndpoint { Id = "playback-a", Name = "扬声器 A", Flow = AudioDataFlow.Playback,
                IsDefaultConsole = true },
            new AudioEndpoint { Id = "playback-b", Name = "耳机 B", Flow = AudioDataFlow.Playback },
            new AudioEndpoint { Id = "recording-a", Name = "麦克风 A", Flow = AudioDataFlow.Recording,
                IsDefaultConsole = true },
            new AudioEndpoint { Id = "recording-b", Name = "麦克风 B", Flow = AudioDataFlow.Recording }
        };

        public event EventHandler DevicesChanged
        {
            add { }
            remove { }
        }

        public IList<AudioEndpoint> GetEndpoints()
        {
            return new List<AudioEndpoint>(_endpoints);
        }

        public IList<string> GetKnownDeviceIds()
        {
            List<string> ids = new List<string>();
            foreach (AudioEndpoint endpoint in _endpoints)
            {
                ids.Add(endpoint.Id);
            }

            return ids;
        }

        public string GetDefaultEndpointId(AudioDataFlow flow)
        {
            foreach (AudioEndpoint endpoint in _endpoints)
            {
                if (endpoint.Flow == flow && endpoint.IsDefault)
                {
                    return endpoint.Id;
                }
            }

            return null;
        }

        public void SetDefaultDevice(string deviceId)
        {
            foreach (AudioEndpoint endpoint in _endpoints)
            {
                endpoint.IsDefaultConsole = endpoint.Id == deviceId && endpoint.Flow == AudioDataFlow.Playback;
            }
        }

        public void Dispose()
        {
        }
    }

    internal sealed class FakeVolumeService : IAudioVolumeService
    {
        public event EventHandler<AudioVolumeChangedEventArgs> VolumeChanged
        {
            add { }
            remove { }
        }

        public bool TryGetState(string deviceId, out AudioVolumeState state)
        {
            state = new AudioVolumeState { Volume = 0.5f };
            return true;
        }

        public bool TrySetVolume(string deviceId, float volume)
        {
            return true;
        }

        public bool TrySetMute(string deviceId, bool muted)
        {
            return true;
        }

        public bool TryGetPeakValue(string deviceId, out float peak)
        {
            peak = 0f;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
