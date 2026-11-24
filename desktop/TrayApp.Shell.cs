// Window construction, tray menu, timers, desktop integration and lifecycle. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CodexUsage;

namespace CodexUsageDesktop {
    public sealed partial class TrayApp {
        public const string MobileUrl = "https://lv5accelerator-xy.github.io/ai-usage-monitor/";

        // Entry point for the UI process. Returns the process exit code.
        public static int Run(string dataDir, bool smoke) {
            NativeDisplay.EnableDpi();
            Application.EnableVisualStyles();
            var app = new TrayApp(dataDir, smoke);
            try {
                bool created;
                app.mutex = new Mutex(true, smoke ? "CodexDualUsageTraySmokeTest" : "CodexDualUsageTray", out created);
                if (!created) {
                    MessageBox.Show("Codex + Claude 额度已经在运行。请检查桌面悬浮面板或任务栏右下角图标。", "Codex + Claude 额度", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                app.Log("===== Codex Usage native UI starting =====");
                app.Build();
                if (smoke) {
                    int code = UiSmoke.Run(app);
                    app.Exit();
                    return code;
                }
                app.StartTimers();
                string launchId = Environment.GetEnvironmentVariable("CODEX_USAGE_LAUNCH_ID");
                if (!string.IsNullOrWhiteSpace(launchId)) File.WriteAllText(Path.Combine(dataDir, "client-ui-ready.txt"), launchId);
                app.Context = new ApplicationContext();
                app.Log("Floating monitor and tray icon are visible. Entering message loop.");
                Application.Run(app.Context);
                app.Log("Message loop ended.");
                return 0;
            } catch (Exception ex) {
                app.Log("FATAL: " + ex);
                try { if (app.NotifyIcon != null) { app.NotifyIcon.Visible = false; app.NotifyIcon.Dispose(); } } catch (Exception) { }
                if (smoke) { Console.Error.WriteLine(ex); File.WriteAllText(Path.Combine(app.LogDir, "smoke-failure.txt"), ex.ToString()); return 1; }
                MessageBox.Show(ex.Message + "\r\n\r\n日志：" + Path.Combine(app.LogDir, "tray.log"), "Codex + Claude 额度 - 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            } finally {
                try { if (app.mutex != null) { app.mutex.ReleaseMutex(); app.mutex.Dispose(); } } catch (Exception) { }
            }
        }

        Icon LoadIcon() {
            try {
                string ico = Path.Combine(PayloadDir, "assets", "app.ico");
                if (PayloadDir.Length > 0 && File.Exists(ico)) return new Icon(ico);
                Icon embedded = Icon.ExtractAssociatedIcon(Client.Self);
                if (embedded != null) return embedded;
            } catch (Exception) { }
            return SystemIcons.Information;
        }

        public void Build() {
            using (Graphics desktop = Graphics.FromHwnd(IntPtr.Zero)) UiScale = desktop.DpiX / 96.0;
            ToolTip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400 };
            cardLoginHandler = delegate(object sender, EventArgs e) { StartLogin(J.Text(((Control)sender).Tag)); };
            cardWheelHandler = delegate(object sender, MouseEventArgs e) { MoveVerticalScroll(e.Delta); var h = e as HandledMouseEventArgs; if (h != null) h.Handled = true; };
            LoadSettings();
            LoadProfiles();
            LoadAlertState();
            LoadUsageHistory();
            InitializeRemote();
            AppIcon = LoadIcon();
            BuildPopup();
            BuildBall();
            BuildMenu();
            NotifyIcon = new NotifyIcon { Icon = AppIcon, Text = "Codex + Claude 额度", ContextMenuStrip = Menu, Visible = !Smoke };
            NotifyIcon.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePopup(); };
            NotifyIcon.BalloonTipClicked += delegate { Popup.Show(); Popup.Activate(); };
            EnsurePollTimer();
            ApplyProfileLayout();
            Ball.ScaleChanged += delegate { UpdateBallChrome(); RememberMonitorPosition(); if (Settings.RingMode) SetMonitorExpanded(false); };
            Popup.ScaleChanged += delegate { UpdateCardWidths(); };
            EventHandler desktopChanged = delegate(object sender, EventArgs e) { DesktopRecoveryPending = true; if (((DpiForm)sender).LastChangeWasResume) ResumeRefreshRequested = true; };
            Ball.DisplayEnvironmentChanged += desktopChanged;
            Popup.DisplayEnvironmentChanged += desktopChanged;
            SetMonitorSize(Settings.MonitorSize, true);
            SetMonitorExpanded(!Settings.CompactMode);
            ApplyGlassEffect();
            if (Settings.RingMode) SnapMonitor();
            if (Settings.BallVisible && !Smoke) Ball.Show();
            if (Smoke) SetMonitorExpanded(true);
            else ShowLoading();
        }

        void BuildPopup() {
            Popup = new DpiForm { DisplayDpi = (int)(UiScale * 96), Text = "Codex + Claude 额度", Icon = AppIcon, AutoScaleMode = AutoScaleMode.None, FormBorderStyle = FormBorderStyle.SizableToolWindow, ShowInTaskbar = false, TopMost = Settings.TopMost, BackColor = PopupBack, StartPosition = FormStartPosition.Manual };
            Popup.Font = UiFont(10);
            Popup.MinimumSize = new Size(U(380), U(360));
            int width = Math.Max(U(380), Settings.PanelWidth), height = Math.Max(U(360), Settings.PanelHeight);
            Point saved = new Point(Settings.PanelX ?? 0, Settings.PanelY ?? 0);
            Rectangle area = Screen.FromPoint(saved).WorkingArea;
            width = Math.Min(width, area.Width); height = Math.Min(height, area.Height);
            Popup.Size = new Size(width, height);
            Popup.Location = Settings.PanelX.HasValue && Settings.PanelY.HasValue ? ClampLocation(Settings.PanelX.Value, Settings.PanelY.Value, width, height) : DefaultPanelLocation(width, height);
            var root = NewGrid(1);
            AddRow(root, 76);
            root.RowCount++;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            AddRow(root, 36);
            Popup.Controls.Add(root);
            var header = NewGrid(2);
            header.Padding = new Padding(U(20), U(12), U(20), U(12));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, U(72)));
            AddRow(header, 30);
            AddRow(header, 22);
            Label title = NewLabel("Codex + Claude 额度", 15, false, true, false);
            Label subtitle = NewLabel("所有数值均为剩余额度", 9, true);
            subtitle.ForeColor = Caption;
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(subtitle, 0, 1);
            RefreshButton = NewButton("刷新");
            RefreshButton.Click += delegate { StartRefresh(); };
            header.Controls.Add(RefreshButton, 1, 0);
            root.Controls.Add(header, 0, 0);
            ContentPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = false, Margin = new Padding(0), Padding = new Padding(U(16), 0, U(16), 0) };
            ContentPanel.SizeChanged += delegate { UpdateCardWidths(); };
            var viewport = NewGrid(2);
            viewport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            viewport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SystemInformation.VerticalScrollBarWidth));
            viewport.RowCount = 1;
            viewport.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ScrollBar = new VScrollBar { Dock = DockStyle.Fill, Margin = new Padding(0), AccessibleName = "额度面板垂直滚动" };
            ScrollBar.ValueChanged += delegate { UpdateCardWidths(); };
            ScrollBar.HandleCreated += delegate { Glass.DarkScrollBar(ScrollBar); };
            ContentPanel.MouseWheel += delegate(object s, MouseEventArgs e) { MoveVerticalScroll(e.Delta); };
            viewport.Controls.Add(ContentPanel, 0, 0);
            viewport.Controls.Add(ScrollBar, 1, 0);
            root.Controls.Add(viewport, 0, 1);
            StatusLabel = NewLabel("", 9, true);
            StatusLabel.Padding = new Padding(U(20), 0, U(20), 0);
            root.Controls.Add(StatusLabel, 0, 2);
            Popup.ResizeEnd += delegate { SaveSettings(); };
            Popup.FormClosing += delegate(object s, FormClosingEventArgs e) {
                if (!Exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Popup.Hide(); SaveSettings(); }
            };
        }

        void BuildBall() {
            Ball = new FloatingForm { DisplayDpi = (int)(UiScale * 96), Text = "Codex + Claude 额度悬浮面板", AutoScaleMode = AutoScaleMode.None, FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = Settings.TopMost, StartPosition = FormStartPosition.Manual, BackColor = BallBack, BorderColor = BallBorder, AccentColor = AccentCodex, GlassEnabled = Settings.GlassEffect, GlassTintAlpha = GlassTintAlpha };
            Ball.Radius = U(14);
            Ball.ClientSize = new Size(U(320), U(142));
            Ball.Padding = new Padding(U(14), U(10), U(14), U(10));
            int count = ActiveIds().Length;
            int initialWidth = Settings.RingMode ? 60 : BallWidth();
            int initialHeight = Settings.RingMode ? 8 + 80 * Math.Max(1, count * 2) : Settings.CompactMode ? 64 : ExpandedHeight();
            MonitorLayoutScale = MonitorFitScale(initialWidth, initialHeight);
            Point location = Settings.BallX.HasValue && Settings.BallY.HasValue ? ClampLocation(Settings.BallX.Value, Settings.BallY.Value, B(initialWidth), B(initialHeight)) : DefaultBallLocation();
            Ball.Location = location;
            RestLocation = location;
            ExpandedGrid = NewGrid(3);
            ExpandedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, U(56)));
            AddPercentColumns(ExpandedGrid, 50, 50);
            AddRow(ExpandedGrid, 24);
            foreach (string unused in Model.AccountIds) AddRow(ExpandedGrid, 34);
            AddRow(ExpandedGrid, 30);
            string[] captions = { "剩余", "5小时", "长周期" };
            for (int c = 0; c < captions.Length; c++) {
                Label caption = NewLabel(captions[c], 9, true);
                caption.ForeColor = Caption;
                if (c > 0) caption.TextAlign = ContentAlignment.MiddleRight;
                ExpandedGrid.Controls.Add(caption, c, 0);
            }
            int row = 1;
            foreach (string id in Model.AccountIds) {
                var cell = new BallCell { Name = NewLabel(Model.DefaultAccountName(id), 10, true), Five = NewLabel("—", 14, false, true, true), Long = NewLabel("—", 14, false, true, true) };
                cell.Five.TextAlign = cell.Long.TextAlign = ContentAlignment.MiddleRight;
                ExpandedGrid.Controls.Add(cell.Name, 0, row);
                ExpandedGrid.Controls.Add(cell.Five, 1, row);
                ExpandedGrid.Controls.Add(cell.Long, 2, row);
                BallCells[id] = cell;
                row++;
            }
            BallStatus = NewLabel("等待首次读取", 9, true);
            ExpandedGrid.Controls.Add(BallStatus, 0, row);
            ExpandedGrid.SetColumnSpan(BallStatus, 3);
            Ball.Controls.Add(ExpandedGrid);
            CompactGrid = NewGrid(Model.AccountIds.Length);
            foreach (string unused in Model.AccountIds) CompactGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Model.AccountIds.Length));
            AddRow(CompactGrid, 30);
            AddRow(CompactGrid, 22);
            int index = 0;
            foreach (string id in Model.AccountIds) {
                var label = new SplitQuotaLabel { Dock = DockStyle.Fill, Margin = new Padding(0), Font = UiFont(9, true, false), SeparatorColor = Caption };
                CompactCells[id] = label;
                CompactGrid.Controls.Add(label, index, 0);
                Label recovery = NewLabel("5h / 长周期 · 剩余", 8, true);
                recovery.TextAlign = ContentAlignment.MiddleCenter;
                CompactRecovery[id] = recovery;
                CompactGrid.Controls.Add(recovery, index, 1);
                index++;
            }
            CompactGrid.Visible = false;
            Ball.Controls.Add(CompactGrid);
            RingPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            foreach (string id in Model.AccountIds) {
                foreach (string window in new[] { "five", "long" }) {
                    var ring = new QuotaRing { BackColor = BallBack, ForeColor = TextMuted, TrackColor = Track };
                    RingCells[id + "-" + window] = ring;
                    RingPanel.Controls.Add(ring);
                }
            }
            Ball.Controls.Add(RingPanel);
            MouseEventHandler down = delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                ((Control)sender).Capture = true;
                ballMouseDown = Cursor.Position; ballOrigin = Ball.Location; ballDragged = false;
            };
            MouseEventHandler move = delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left || !ballMouseDown.HasValue) return;
                Point cursor = Cursor.Position;
                int dx = cursor.X - ballMouseDown.Value.X, dy = cursor.Y - ballMouseDown.Value.Y;
                if (Math.Abs(dx) + Math.Abs(dy) > U(4)) ballDragged = true;
                if (ballDragged) Ball.Location = new Point(ballOrigin.Value.X + dx, ballOrigin.Value.Y + dy);
            };
            MouseEventHandler up = delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                if (ballDragged) { SnapMonitor(); SaveSettings(); } else TogglePopup();
                ballMouseDown = null; ballOrigin = null; ballDragged = false;
                ((Control)sender).Capture = false;
            };
            foreach (Control control in Tree(Ball).ToList()) { control.MouseDown += down; control.MouseMove += move; control.MouseUp += up; }
        }

        ToolStripMenuItem Item(ToolStripItemCollection items, string text, EventHandler click) {
            var item = new ToolStripMenuItem(text);
            if (click != null) item.Click += click;
            items.Add(item);
            return item;
        }
        ToolStripMenuItem Check(ToolStripItemCollection items, string text, bool value, EventHandler click) {
            var item = Item(items, text, click);
            item.CheckOnClick = true;
            item.Checked = value;
            return item;
        }

        void BuildMenu() {
            Menu = new ContextMenuStrip { Renderer = new HudMenuRenderer(CardBack, CardBorder, MenuSelect, TextPrimary, TextMuted, AccentCodex), BackColor = CardBack, ForeColor = TextPrimary };
            ToolStripItemCollection items = Menu.Items;
            ItemCompact = Check(items, "紧凑模式（悬停展开）", Settings.CompactMode, delegate {
                Settings.RingMode = false; ItemRings.Checked = false;
                Settings.CompactMode = ItemCompact.Checked;
                RememberMonitorPosition();
                SetMonitorExpanded(!ItemCompact.Checked);
                RememberMonitorPosition();
                SaveSettings();
            });
            ItemRings = Check(items, "贴边圆环（点击查看详情）", Settings.RingMode, delegate {
                RememberMonitorPosition();
                Settings.RingMode = ItemRings.Checked;
                Settings.CompactMode = true;
                ItemCompact.Checked = !ItemRings.Checked;
                SetMonitorExpanded(false);
                if (ItemRings.Checked) SnapMonitor();
                SaveSettings();
            });
            if (ItemRings.Checked) ItemCompact.Checked = false;
            ItemGlass = Check(items, "磨砂玻璃效果", Settings.GlassEffect, delegate { Settings.GlassEffect = ItemGlass.Checked; ApplyGlassEffect(); SaveSettings(); });
            ItemClear = Check(items, "清晰模式（紧凑条不额外透明）", Settings.ClearMode, delegate { Settings.ClearMode = ItemClear.Checked; UpdateMonitorOpacity(); SaveSettings(); });
            var sizeMenu = Item(items, "悬浮窗大小", null);
            foreach (var option in new[] { new[] { "small", "小" }, new[] { "medium", "中" }, new[] { "large", "大" } }) {
                string id = option[0];
                var sizeItem = Item(sizeMenu.DropDownItems, option[1], delegate { SetMonitorSize(id, false); });
                sizeItem.Checked = Settings.MonitorSize == id;
                SizeItems[id] = sizeItem;
            }
            var opacityMenu = Item(items, "紧凑小窗透明度", null);
            var opacityPanel = new Panel { Size = new Size(U(232), U(82)), BackColor = CardBack };
            var opacityLayout = NewGrid(1);
            opacityLayout.Padding = new Padding(U(10), U(4), U(10), U(4));
            AddRow(opacityLayout, 28);
            AddRow(opacityLayout, 42);
            opacityLabel = NewLabel("透明度：" + (100 - Settings.CompactOpacity) + "%", 9, false);
            opacityLayout.Controls.Add(opacityLabel, 0, 0);
            OpacitySlider = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 60, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Value = Math.Max(0, Math.Min(60, 100 - Settings.CompactOpacity)), AccessibleName = "紧凑小窗透明度，0% 到 60%", BackColor = CardBack };
            OpacitySlider.ValueChanged += delegate {
                Settings.CompactOpacity = 100 - OpacitySlider.Value;
                opacityLabel.Text = "透明度：" + OpacitySlider.Value + "%";
                UpdateMonitorOpacity();
                SaveSettings();
            };
            opacityLayout.Controls.Add(OpacitySlider, 0, 1);
            opacityPanel.Controls.Add(opacityLayout);
            opacityMenu.DropDownItems.Add(new ToolStripControlHost(opacityPanel) { AutoSize = false, Size = opacityPanel.Size });
            opacityMenu.DropDownOpening += delegate { if (Settings.CompactMode) SetMonitorExpanded(false); };
            ItemSnap = Check(items, "贴边吸附", Settings.EdgeSnap, delegate { Settings.EdgeSnap = ItemSnap.Checked; SnapMonitor(); SaveSettings(); });
            var notifications = Item(items, "通知", null);
            ItemNotifications = Check(notifications.DropDownItems, "低额度提醒", Settings.NotificationsEnabled, delegate {
                Settings.NotificationsEnabled = ItemNotifications.Checked;
                AlertBaselinePending = true;
                foreach (AlertEntry entry in AlertState.Values) entry.Value = null;
                SaveSettings();
            });
            notifications.DropDownItems.Add(new ToolStripSeparator());
            foreach (int[] levels in new[] { new[] { 20, 10 }, new[] { 10 }, new[] { 30, 15 } }) {
                int[] preset = levels;
                string text = preset.Length == 1 ? "仅剩余 " + preset[0] + "%" : "剩余 " + string.Join("% / ", preset) + "%";
                ToolStripMenuItem item = null;
                item = Item(notifications.DropDownItems, text, delegate {
                    Settings.NotificationThresholds = preset;
                    foreach (var choice in ThresholdItems) choice.Checked = choice == item;
                    AlertBaselinePending = true;
                    foreach (AlertEntry entry in AlertState.Values) entry.Value = null;
                    SaveSettings();
                });
                item.Checked = string.Join(",", Settings.NotificationThresholds) == string.Join(",", preset);
                ThresholdItems.Add(item);
            }
            notifications.DropDownItems.Add(new ToolStripSeparator());
            ItemRecovery = Check(notifications.DropDownItems, "额度恢复提醒（用完后恢复时）", Settings.RecoveryNotifications, delegate { Settings.RecoveryNotifications = ItemRecovery.Checked; SaveSettings(); });
            ItemFailure = Check(notifications.DropDownItems, "读取异常提醒（连续 15 分钟失败）", Settings.FailureNotifications, delegate { Settings.FailureNotifications = ItemFailure.Checked; SaveSettings(); });
            items.Add(new ToolStripSeparator());
            Item(items, "展开 / 收起额度面板", delegate { TogglePopup(); });
            Item(items, "立即刷新", delegate { StartRefresh(); });
            ItemBallVisible = Check(items, "显示悬浮面板", Settings.BallVisible, delegate {
                AutoHidden = false;
                Settings.BallVisible = ItemBallVisible.Checked;
                if (ItemBallVisible.Checked) Ball.Show(); else Ball.Hide();
                SaveSettings();
            });
            ItemTopMost = Check(items, "始终置顶", Settings.TopMost, delegate { Settings.TopMost = Popup.TopMost = Ball.TopMost = ItemTopMost.Checked; SaveSettings(); });
            Item(items, "账号与首次使用", delegate { ShowAccountSettings(); });
            Item(items, "远程通知与手机额度", delegate { ShowRemoteSettings(); });
            string clientPath = Environment.GetEnvironmentVariable("CODEX_USAGE_CLIENT_PATH") ?? "";
            ItemStartup = Check(items, "开机启动", false, delegate {
                try { DesktopIntegration.SetStartup(clientPath, ItemStartup.Checked); }
                catch (Exception) { ItemStartup.Checked = !ItemStartup.Checked; MessageBox.Show("无法修改开机启动设置。", "Codex + Claude 额度"); }
            });
            ItemStartup.Enabled = clientPath.Length > 0;
            if (ItemStartup.Enabled && !Smoke) { try { ItemStartup.Checked = DesktopIntegration.IsStartupEnabled(clientPath); } catch (Exception) { } }
            ItemFullscreen = Check(items, "全屏时隐藏悬浮窗", Settings.HideFullscreen, delegate { Settings.HideFullscreen = ItemFullscreen.Checked; UpdateDesktopExperience(); SaveSettings(); });
            Item(items, "重置界面位置", delegate { ResetUiPositions(); });
            items.Add(new ToolStripSeparator());
            var login = Item(items, "账号登录 / 切换", null);
            Item(login.DropDownItems, "个人账号", delegate { StartLogin("personal"); });
            Item(login.DropDownItems, "工作账号", delegate { StartLogin("work"); });
            Item(login.DropDownItems, "Claude 账号", delegate { StartLogin("claude"); });
            items.Add(new ToolStripSeparator());
            var client = Item(items, "客户端更新", null);
            string version = Environment.GetEnvironmentVariable("CODEX_USAGE_CLIENT_VERSION") ?? "";
            ClientStatusItem = Item(client.DropDownItems, version.Length > 0 ? "版本 " + version + " · 等待检查" : "开发模式 · 请使用安装版获取自动更新", null);
            ClientStatusItem.Enabled = false;
            CheckUpdateItem = Item(client.DropDownItems, "立即检查更新", delegate {
                File.WriteAllText(Path.Combine(DataDir, "check-update.request"), "check");
                ClientStatusItem.Text = "正在检查更新…";
            });
            CheckUpdateItem.Enabled = version.Length > 0;
            RestartUpdateItem = Item(client.DropDownItems, "重启并更新", delegate {
                File.WriteAllText(Path.Combine(DataDir, "restart.request"), "restart");
                Exit();
            });
            RestartUpdateItem.Visible = false;
            ItemBeta = Check(client.DropDownItems, "接收测试版（先于朋友更新）", Settings.UpdateChannel == "beta", delegate {
                Settings.UpdateChannel = ItemBeta.Checked ? "beta" : "stable";
                SaveSettings();
                if (!Smoke && version.Length > 0) {
                    File.WriteAllText(Path.Combine(DataDir, "check-update.request"), "check");
                    ClientStatusItem.Text = ItemBeta.Checked ? "已切换到测试版通道，正在检查…" : "已切换到正式版通道，正在检查…";
                }
            });
            Item(client.DropDownItems, "版本说明 / 下载客户端", delegate { OpenUrl("https://github.com/" + Client.Repository + "/releases/latest"); });
            Item(items, "使用指南 / 分享给朋友", delegate { OpenUrl("https://github.com/" + Client.Repository + "/blob/main/docs/FRIENDS.md"); });
            items.Add(new ToolStripSeparator());
            Item(items, "打开日志文件夹", delegate { try { Process.Start("explorer.exe", Client.Quote(LogDir)); } catch (Exception) { } });
            Item(items, "退出", delegate { Exit(); });
            foreach (Control control in Tree(Ball).ToList()) control.ContextMenuStrip = Menu;
            RoundMenuCorners(Menu);
        }

        // Windows 11 draws rounded, shadowed menus when asked; submenus are separate windows.
        static void RoundMenuCorners(ToolStripDropDown dropDown) {
            dropDown.Opened += delegate { Glass.RoundCorners(dropDown.Handle, true); };
            foreach (ToolStripItem item in dropDown.Items) {
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems) RoundMenuCorners(menuItem.DropDown);
            }
        }

        static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { } }

        void StartTimers() {
            ClientTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            ClientTimer.Tick += delegate { UpdateClientStatus(); };
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_USAGE_CLIENT_VERSION"))) ClientTimer.Start();
            HoverTimer = new System.Windows.Forms.Timer { Interval = 150 };
            HoverTimer.Tick += delegate { UpdateMonitorHover(); };
            HoverTimer.Start();
            StatusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            StatusTimer.Tick += delegate { UpdateStatus(true); UpdateDesktopExperience(); UpdateRemoteNotifications(); };
            StatusTimer.Start();
            PeriodicTimer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
            PeriodicTimer.Tick += delegate { StartRefresh(); };
            PeriodicTimer.Start();
            InitialTimer = new System.Windows.Forms.Timer { Interval = 700 };
            InitialTimer.Tick += delegate {
                InitialTimer.Stop();
                StartRefresh();
                if (!Settings.SetupSeen) ShowAccountSettings();
            };
            InitialTimer.Start();
        }

        // Created even in smoke mode so refresh and recovery paths can be exercised without timers firing.
        void EnsurePollTimer() {
            if (PollTimer != null) return;
            PollTimer = new System.Windows.Forms.Timer { Interval = 350 };
            PollTimer.Tick += delegate { FinishRefreshIfReady(); };
        }

        public void TogglePopup() {
            if (Popup.Visible) { Popup.Hide(); SaveSettings(); }
            else { Popup.Show(); Popup.Activate(); }
        }

        public void StartLogin(string account) {
            if (!Model.AccountIds.Contains(account)) return;
            AlertBaselinePending = true;
            string file = account == "personal" ? "login-personal.bat" : account == "claude" ? "login-claude.bat" : "login-work.bat";
            string path = Path.Combine(PayloadDir, file);
            if (PayloadDir.Length == 0 || !File.Exists(path)) { MessageBox.Show("未找到登录文件：" + path, "Codex + Claude 额度"); return; }
            if (Smoke) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = PayloadDir }); }
            catch (Exception ex) { MessageBox.Show("无法打开登录窗口：" + ex.Message, "Codex + Claude 额度"); }
        }

        public void UpdateClientStatus() {
            string version = Environment.GetEnvironmentVariable("CODEX_USAGE_CLIENT_VERSION") ?? "";
            if (version.Length == 0) return;
            string showRequest = Path.Combine(DataDir, "show.request");
            if (File.Exists(showRequest)) { Client.TryDelete(showRequest); Popup.Show(); Popup.Activate(); }
            try {
                if (!File.Exists(ClientStatusPath)) return;
                string json = File.ReadAllText(ClientStatusPath);
                if (json == lastClientStatus) return;
                object status = J.Parse(json);
                lastClientStatus = json;
                string state = J.Text(J.Get(status, "state")), next = J.Text(J.Get(status, "version"));
                ClientStatusItem.Text = J.Text(J.Get(status, "message"));
                RestartUpdateItem.Visible = state == "ready";
                CheckUpdateItem.Enabled = state != "checking" && state != "downloading" && state != "ready";
                if (state == "ready" && clientUpdateNotified != next) {
                    clientUpdateNotified = next;
                    Balloon("客户端更新", "v" + next + " 已下载。右键选择“重启并更新”，或退出后自动安装。", ToolTipIcon.Info);
                }
            } catch (Exception ex) { Log("Client status: " + ex.Message); }
        }

        public void UpdateDesktopExperience() {
            if (Smoke || Exiting) return;
            if (DesktopRecoveryPending) RestoreDesktopLayout();
            if (!ballMouseDown.HasValue) {
                Point before = Ball.Location;
                DesktopIntegration.KeepVisible(Ball);
                DesktopIntegration.KeepVisible(Popup);
                if (Ball.Location != before) RememberMonitorPosition();
            }
            bool hide = Settings.HideFullscreen && DesktopIntegration.FullScreenOn(Ball);
            if (hide && Ball.Visible) { AutoHidden = true; Ball.Hide(); }
            else if (!hide && AutoHidden) { AutoHidden = false; if (Settings.BallVisible) Ball.Show(); }
            if (!RefreshPending) {
                foreach (string id in ActiveIds()) {
                    string key = Model.RecoveryKey(Model.DisplayProfile(LastData, id), DateTimeOffset.Now);
                    if (key != null && recoveryRequests.Add(key)) { StartRefresh(); break; }
                }
            }
        }

        public void RestoreDesktopLayout() {
            if (ballMouseDown.HasValue) return;
            DesktopIntegration.KeepVisible(Ball);
            DesktopIntegration.KeepVisible(Popup);
            RememberMonitorPosition();
            SetMonitorExpanded(MonitorExpanded);
            DesktopRecoveryPending = false;
            if (ResumeRefreshRequested) {
                if (Worker != null) {
                    try { if (!Worker.HasExited) { Worker.Kill(); Worker.WaitForExit(1000); } } catch (Exception) { }
                    Worker.Dispose(); Worker = null;
                    if (PollTimer != null) PollTimer.Stop();
                }
                RefreshPending = false;
                ResumeRefreshRequested = false;
                if (!Smoke) StartRefresh();
            }
            SaveSettings();
        }

        public void Exit() {
            if (Exiting) return;
            Exiting = true;
            Log("Exiting tray.");
            SaveSettings();
            foreach (var timer in new[] { PollTimer, PeriodicTimer, InitialTimer, StatusTimer, HoverTimer, ClientTimer }) { try { if (timer != null) timer.Stop(); } catch (Exception) { } }
            try { if (Remote != null) Remote.Stop(); } catch (Exception) { }
            try { if (Worker != null && !Worker.HasExited) Worker.Kill(); } catch (Exception) { }
            try { if (NotifyIcon != null) { NotifyIcon.Visible = false; NotifyIcon.Dispose(); } } catch (Exception) { }
            try { if (Popup != null) { Popup.Hide(); Popup.Dispose(); } } catch (Exception) { }
            try { if (Ball != null) { Ball.Hide(); Ball.Dispose(); } } catch (Exception) { }
            try { ToolTip.Dispose(); } catch (Exception) { }
            foreach (Font font in fonts.Values) { try { font.Dispose(); } catch (Exception) { } }
            try { if (Context != null) Context.ExitThread(); } catch (Exception) { }
        }
    }
}
