using System;
using System.Drawing;
using System.Windows.Forms;
using AudioSwitch.Platform;

namespace AudioSwitch.Tray
{
    /// <summary>Right click menu of the notification area icon.</summary>
    public sealed class TrayContextMenu : IDisposable
    {
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _soundItem;
        private readonly ToolStripMenuItem _restoreHiddenItem;
        private readonly IDeviceVisibilityService _visibility;
        private readonly Action<string> _showInfo;
        private readonly IStartupRegistration _startup;
        private readonly IAppSettings _settings;
        private readonly ILogger _logger;
        private readonly Action<string> _reportError;
        private bool _disposed;

        public TrayContextMenu(IStartupRegistration startup, IAppSettings settings,
            IDeviceVisibilityService visibility, ILogger logger,
            Action<string> reportError, Action<string> showInfo, Action openPanel, Action refresh,
            Action openSoundSettings, Action openSoundPanel, Action exit)
        {
            _startup = startup;
            _settings = settings;
            _visibility = visibility;
            _logger = logger;
            _reportError = reportError;
            _showInfo = showInfo;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9f);

            ToolStripMenuItem openItem = new ToolStripMenuItem("打开切换面板");
            openItem.Click += delegate { openPanel(); };

            ToolStripMenuItem refreshItem = new ToolStripMenuItem("刷新设备");
            refreshItem.Click += delegate { refresh(); };

            ToolStripMenuItem soundSettingsItem = new ToolStripMenuItem("声音设置");
            soundSettingsItem.Click += delegate { openSoundSettings(); };

            ToolStripMenuItem soundPanelItem = new ToolStripMenuItem("声音控制面板");
            soundPanelItem.Click += delegate { openSoundPanel(); };

            _startupItem = new ToolStripMenuItem("开机自动启动");
            _startupItem.Click += OnStartupItemClick;

            _soundItem = new ToolStripMenuItem("音量调节提示音");
            _soundItem.Click += OnSoundItemClick;

            _restoreHiddenItem = new ToolStripMenuItem("取消所有隐藏");
            _restoreHiddenItem.Click += OnRestoreHiddenItemClick;

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { exit(); };

            menu.Items.Add(openItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(soundSettingsItem);
            menu.Items.Add(soundPanelItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startupItem);
            menu.Items.Add(_soundItem);
            menu.Items.Add(_restoreHiddenItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            menu.Opening += OnMenuOpening;

            _menu = menu;
        }

        public ContextMenuStrip Menu
        {
            get { return _menu; }
        }

        /// <summary>Test hook: invokes the 「取消所有隐藏」 item as if the user clicked it.</summary>
        internal void ClickRestoreHiddenItemForTest()
        {
            _restoreHiddenItem.PerformClick();
        }

        public void Show(int x, int y)
        {
            try
            {
                _menu.Show(new Point(x, y));
            }
            catch (Exception ex)
            {
                _logger.Error("显示托盘右键菜单失败。", ex);
            }
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _startupItem.Checked = _startup.IsEnabled;
                _soundItem.Checked = _settings.GetFlag("VolumeFeedbackSound", true);
            }
            catch (Exception ex)
            {
                _logger.Error("读取菜单状态失败。", ex);
            }
        }

        private void OnStartupItemClick(object sender, EventArgs e)
        {
            try
            {
                bool enable = !_startup.IsEnabled;
                _startup.SetEnabled(enable);
                _startupItem.Checked = enable;
            }
            catch (Exception ex)
            {
                _logger.Error("设置开机自动启动失败。", ex);
                Action<string> report = _reportError;
                if (report != null)
                {
                    report("设置开机自动启动失败。\n\n" + ex.Message);
                }
            }
        }

        private void OnRestoreHiddenItemClick(object sender, EventArgs e)
        {
            try
            {
                bool changed = _visibility != null && _visibility.ShowAll();
                Action<string> info = _showInfo;
                if (info != null)
                {
                    info(changed
                        ? "已取消所有隐藏，所有设备将重新显示。"
                        : "当前没有被隐藏的设备。");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("取消隐藏设备失败。", ex);
                Action<string> report = _reportError;
                if (report != null)
                {
                    report("取消隐藏设备失败。\n\n" + ex.Message);
                }
            }
        }

        private void OnSoundItemClick(object sender, EventArgs e)
        {
            try
            {
                bool enable = !_settings.GetFlag("VolumeFeedbackSound", true);
                _settings.SetFlag("VolumeFeedbackSound", enable);
                _soundItem.Checked = enable;
            }
            catch (Exception ex)
            {
                _logger.Error("保存音量提示音设置失败。", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _menu.Opening -= OnMenuOpening;
                _menu.Dispose();
            }
            catch
            {
                // Ignore teardown failures.
            }
        }
    }
}
