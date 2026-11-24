// Native tray UI: floating monitor (compact / expanded / rings), detail panel, tray menu,
// refresh lifecycle, notifications and desktop integration. Ported from tray.ps1 (v1.1).
// C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CodexUsage;

namespace CodexUsageDesktop {
    public sealed class ResetTag { public object Window; public string Detail = ""; public object Profile; public string Kind = ""; }
    public sealed class CardState { public Label Label; public object Profile; }
    public sealed class BallCell { public Label Name, Five, Long; }

    public sealed partial class TrayApp {
        // ---------------------------------------------------------------- state
        public readonly bool Smoke;
        public readonly string DataDir, LogDir, CachePath, UiSettingsPath, AlertStatePath, HistoryPath, ClientStatusPath, PayloadDir;
        public string ProfilesPath; // writable so the smoke test can redirect saves to a fixture
        public UiSettings Settings;
        public Dictionary<string, object> ProfileConfig;
        public int ProfileRevision, ReadRevision;
        public object LastData;
        public string RefreshError = "";
        public bool RefreshPending, Exiting, MonitorExpanded = true, AutoHidden, AlertBaselinePending = true, DesktopRecoveryPending, ResumeRefreshRequested;
        public DateTimeOffset RefreshStarted;
        public string ReadId = "";
        public int LastProgressRevision = -1;
        public Process Worker;
        public Dictionary<string, AlertEntry> AlertState = new Dictionary<string, AlertEntry>();
        public Dictionary<string, List<UsageSample>> UsageHistory = new Dictionary<string, List<UsageSample>>();
        readonly HashSet<string> forecastAlerted = new HashSet<string>();
        readonly HashSet<string> recoveryRequests = new HashSet<string>();
        public double UiScale = 1, MonitorLayoutScale = 1, AppliedMonitorScale = 1;
        public Point? RestLocation;
        public DateTimeOffset? PointerLeftAt;
        Point? ballMouseDown, ballOrigin;
        bool ballDragged, layingOutCards;
        DateTimeOffset lastStatusPaint = DateTimeOffset.MinValue;
        string lastClientStatus = "", clientUpdateNotified = "";
        public string ProfileCli, ClaudeCli;

        // ---------------------------------------------------------------- theme
        public static Color Html(string value) { return ColorTranslator.FromHtml(value); }
        // Apple dark-mode materials and system colours. Windows are translucent and the
        // desktop behind them is blurred (see Glass); these are the opaque base tones.
        public readonly Color PopupBack = Html("#1C1C1E"), CardBack = Html("#2C2C2E"), CardBorder = Html("#3A3A3C"), TextPrimary = Html("#F5F5F7"),
            TextMuted = Html("#98989F"), Danger = Html("#FF453A"), Warning = Html("#FF9F0A"), Good = Html("#30D158"), Track = Html("#3A3A3E"),
            ButtonBack = Html("#3A3A3C"), ButtonBorder = Html("#48484A"), ButtonHover = Html("#48484A"), BallBack = Html("#1E1E21"), BallBorder = Html("#4A4A50"),
            AccentCodex = Html("#409CFF"), AccentClaude = Html("#BF5AF2"), Caption = Html("#8E8E93"), MenuSelect = Html("#0A84FF");
        // Window opacity while the frosted-glass effect is on: the share of the base tone over the blurred desktop.
        public const double GlassMonitorOpacity = 0.86, GlassPanelOpacity = 0.92;
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        // ---------------------------------------------------------------- controls
        public DpiForm Popup;
        public FloatingForm Ball;
        public Panel ContentPanel, RingPanel;
        public VScrollBar ScrollBar;
        public Label StatusLabel, BallStatus;
        public Button RefreshButton;
        public TableLayoutPanel ExpandedGrid, CompactGrid;
        public NotifyIcon NotifyIcon;
        public ToolTip ToolTip;
        public ContextMenuStrip Menu;
        public Icon AppIcon;
        public readonly Dictionary<string, BallCell> BallCells = new Dictionary<string, BallCell>();
        public readonly Dictionary<string, SplitQuotaLabel> CompactCells = new Dictionary<string, SplitQuotaLabel>();
        public readonly Dictionary<string, Label> CompactRecovery = new Dictionary<string, Label>();
        public readonly Dictionary<string, QuotaRing> RingCells = new Dictionary<string, QuotaRing>();
        public readonly List<Label> ResetLabels = new List<Label>();
        public readonly List<CardState> CardStates = new List<CardState>();
        public readonly Dictionary<string, ToolStripMenuItem> SizeItems = new Dictionary<string, ToolStripMenuItem>();
        public readonly List<ToolStripMenuItem> ThresholdItems = new List<ToolStripMenuItem>();
        public ToolStripMenuItem ItemCompact, ItemRings, ItemClear, ItemGlass, ItemSnap, ItemNotifications, ItemBallVisible, ItemTopMost, ItemStartup, ItemFullscreen, ClientStatusItem, CheckUpdateItem, RestartUpdateItem, ItemRecovery, ItemFailure, ItemBeta;
        public TrackBar OpacitySlider;
        Label opacityLabel;
        public System.Windows.Forms.Timer HoverTimer, StatusTimer, PollTimer, PeriodicTimer, InitialTimer, ClientTimer;
        public const int RefreshIntervalMs = 60000;
        public RemoteWorker Remote;
        public RemoteSettings RemoteSettings;
        readonly HashSet<string> remoteNotified = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset remoteLastEnsure = DateTimeOffset.MinValue;
        Mutex mutex;
        public ApplicationContext Context;
        EventHandler cardLoginHandler;
        MouseEventHandler cardWheelHandler;

        public TrayApp(string dataDir, bool smoke) {
            Smoke = smoke;
            DataDir = dataDir;
            LogDir = Path.Combine(dataDir, "logs");
            CachePath = Path.Combine(LogDir, "usage-result.json");
            UiSettingsPath = Path.Combine(dataDir, "ui-settings.json");
            AlertStatePath = Path.Combine(LogDir, "notification-state.json");
            HistoryPath = Path.Combine(LogDir, "usage-history.json");
            ClientStatusPath = Path.Combine(dataDir, "client-status.json");
            ProfilesPath = Path.Combine(dataDir, "profiles.json");
            PayloadDir = Environment.GetEnvironmentVariable("CODEX_USAGE_PAYLOAD_DIR") ?? "";
            Directory.CreateDirectory(LogDir);
        }

        public void Log(string message) {
            try { File.AppendAllText(Path.Combine(LogDir, "tray.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", J.Inv) + "] " + message + Environment.NewLine, Encoding.UTF8); } catch (Exception) { }
        }

        // ---------------------------------------------------------------- scaling + factories
        public int U(double value) { double scale = Popup != null ? Popup.DisplayDpi / 96.0 : UiScale; return (int)Math.Round(value * scale); }
        public int B(double value) { double scale = Ball != null ? Ball.DisplayDpi / 96.0 : UiScale; return (int)Math.Round(value * scale * MonitorLayoutScale); }

        public Font UiFont(float size, bool bold, bool mono) {
            if (Popup != null) size = (float)(size * Popup.DisplayDpi / (UiScale * 96));
            string key = size.ToString(J.Inv) + "/" + bold + "/" + mono;
            Font font;
            if (!fonts.TryGetValue(key, out font)) {
                font = mono ? new Font(NumberFont.Family, size, NumberFont.Style(bold), GraphicsUnit.Point)
                    : new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                fonts[key] = font;
            }
            return font;
        }
        public Font UiFont(float size) { return UiFont(size, false, false); }

        public Label NewLabel(string text, float size, bool muted, bool bold, bool mono) {
            return new Label {
                Text = text ?? "", Dock = DockStyle.Fill, Margin = new Padding(0), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                Font = UiFont(size, bold, mono), ForeColor = muted ? TextMuted : TextPrimary
            };
        }
        public Label NewLabel(string text, float size, bool muted) { return NewLabel(text, size, muted, false, false); }

        public static TableLayoutPanel NewGrid(int columns) {
            return new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0), ColumnCount = columns, RowCount = 0 };
        }
        public void AddRow(TableLayoutPanel grid, double height) { grid.RowCount++; grid.RowStyles.Add(new RowStyle(SizeType.Absolute, U(height))); }
        public static void AddPercentColumns(TableLayoutPanel grid, params float[] widths) { foreach (float w in widths) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, w)); }

        public Button NewButton(string text) {
            var button = new PillButton { Text = text, Dock = DockStyle.Fill, Font = UiFont(9), BackColor = ButtonBack, FillColor = ButtonBack, HoverColor = ButtonHover, PressedColor = Html("#545458"), ForeColor = AccentCodex, Cursor = Cursors.Hand, Margin = new Padding(0, U(2), 0, U(2)) };
            return button;
        }

        public Color BarColor(double? remaining, Color? accent) {
            if (!remaining.HasValue) return Track;
            if (remaining.Value <= 15) return Danger;
            if (remaining.Value <= 35) return Warning;
            return accent ?? Good;
        }
        public Color ValueColor(double? remaining, bool stale) {
            if (stale || !remaining.HasValue) return TextMuted;
            if (remaining.Value <= 35) return BarColor(remaining, null);
            return TextPrimary;
        }
        public Color Accent(string id) { return id == "claude" ? AccentClaude : AccentCodex; }

        public static IEnumerable<Control> Tree(Control control) {
            yield return control;
            foreach (Control child in control.Controls) foreach (Control nested in Tree(child)) yield return nested;
        }

        // ---------------------------------------------------------------- profiles + settings
        public string[] ActiveIds() { return Profiles.ActiveIds(ProfileConfig); }
        public string AccountName(string id) { return Profiles.Name(ProfileConfig, id); }
        public int BallWidth() { return Model.MonitorWidth(ActiveIds().Length); }
        public int ExpandedHeight() { return Model.ExpandedHeight(ActiveIds().Length); }

        public void LoadProfiles() {
            if (Smoke) {
                ProfileConfig = new Dictionary<string, object> { { "profiles", new object[] {
                    new Dictionary<string, object> { { "id", "personal" }, { "label", "个人" }, { "codexHome", "~/.codex-personal" }, { "enabled", true } },
                    new Dictionary<string, object> { { "id", "work" }, { "label", "工作" }, { "codexHome", "~/.codex-work" }, { "enabled", true } },
                    new Dictionary<string, object> { { "id", "claude" }, { "label", "Claude" }, { "provider", "claude" }, { "claudeHome", "~/.claude" }, { "enabled", false } }
                } } };
                return;
            }
            ProfileConfig = Profiles.Load(ProfilesPath);
            // v1.0 adds a Claude slot to older configurations before the first read.
            if (Model.AddClaudeProfileIfMissing(ProfileConfig, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"))) {
                try { Profiles.Save(ProfileConfig, ProfilesPath); Log("Added Claude account slot to profiles.json."); }
                catch (Exception ex) { Log("Claude profile migration warning: " + ex.Message); }
            }
        }

        public void LoadSettings() {
            object raw = null;
            if (!Smoke) { try { raw = J.ReadFile(UiSettingsPath); } catch (Exception ex) { Log("UI settings load warning: " + ex.Message); } }
            Settings = UiSettings.FromObject(raw, U(440), U(840));
        }

        public void SaveSettings() {
            if (Smoke || Settings == null) return;
            try {
                if (Ball != null) {
                    Point point = RestLocation ?? Ball.Location;
                    Settings.BallX = point.X; Settings.BallY = point.Y;
                    // Preserve the user's preference while temporarily hidden for full-screen apps.
                    if (!AutoHidden) Settings.BallVisible = Ball.Visible;
                }
                if (Popup != null) {
                    Settings.PanelX = Popup.Left; Settings.PanelY = Popup.Top;
                    Settings.PanelWidth = Popup.Width; Settings.PanelHeight = Popup.Height;
                    Settings.TopMost = Popup.TopMost;
                }
                J.AtomicWrite(UiSettingsPath, J.Json(Settings.ToObject()), false);
            } catch (Exception ex) { Log("UI settings save warning: " + ex.Message); }
        }

        void LoadAlertState() {
            if (Smoke || !File.Exists(AlertStatePath)) return;
            try { AlertState = Behavior.AlertStateFromJson(File.ReadAllText(AlertStatePath, Encoding.UTF8)); }
            catch (Exception ex) { Log("Notification state load warning: " + ex.Message); }
        }

        void LoadUsageHistory() {
            if (Smoke || !File.Exists(HistoryPath)) return;
            try { UsageHistory = Forecasts.FromJson(File.ReadAllText(HistoryPath, Encoding.UTF8)); }
            catch (Exception ex) { UsageHistory = new Dictionary<string, List<UsageSample>>(); Log("Usage history load warning: " + ex.Message); }
        }

        // Fresh successful reads become forecast samples; the history stays local (percentages only).
        public void UpdateUsageHistory(object data) {
            try {
                Forecasts.AddSamples(UsageHistory, data, DateTimeOffset.Now);
                if (!Smoke) J.AtomicWrite(HistoryPath, Forecasts.ToJson(UsageHistory), false);
            } catch (Exception ex) { Log("Usage history warning: " + ex.Message); }
        }

        public Forecast ProfileForecast(object profile, string kind) {
            if (profile == null || !J.Bool(J.Get(profile, "ok"))) return null;
            return Forecasts.Burn(UsageHistory, J.Text(J.Get(profile, "id")), kind, J.Get(profile, kind), DateTimeOffset.Now);
        }

        // ---------------------------------------------------------------- geometry
        public Point ClampLocation(int x, int y, int width, int height) {
            Rectangle area = Screen.FromPoint(new Point(x, y)).WorkingArea;
            int maxX = Math.Max(area.Left, area.Right - width), maxY = Math.Max(area.Top, area.Bottom - height);
            return new Point(Math.Max(area.Left, Math.Min(maxX, x)), Math.Max(area.Top, Math.Min(maxY, y)));
        }
        public Point DefaultBallLocation() { Rectangle area = Screen.PrimaryScreen.WorkingArea; return new Point(area.Right - B(384), area.Bottom - U(180)); }
        public Point DefaultPanelLocation(int width, int height) {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            return new Point(area.Right - width - 24, Math.Max(area.Top + 24, area.Bottom - height - 24));
        }

        public double MonitorFitScale(double width, double height) {
            Point point = RestLocation ?? (Settings.BallX.HasValue && Settings.BallY.HasValue ? new Point(Settings.BallX.Value, Settings.BallY.Value) : Screen.PrimaryScreen.WorkingArea.Location);
            Rectangle area = Screen.FromPoint(point).WorkingArea;
            return Behavior.FittedScale(UiSettings.SizeScale(Settings.MonitorSize), Ball.DisplayDpi / 96.0, width, height, area.Width, area.Height);
        }

        public void UpdateMonitorLayoutScale(double width, double height) {
            double scale = MonitorFitScale(width, height);
            MonitorLayoutScale = scale;
            if (Math.Abs(scale - AppliedMonitorScale) > 0.000001) {
                Ball.ApplyZoom((float)(scale / AppliedMonitorScale));
                AppliedMonitorScale = scale;
            }
            UpdateBallChrome();
        }

        void UpdateBallChrome() { Ball.Radius = B(14); Ball.HudUnit = (float)(B(100) / 100.0); }

        public void RememberMonitorPosition() {
            int x = Ball.Left, y = Ball.Top;
            if (Settings.CompactMode && MonitorExpanded && Settings.DockVertical == "bottom") {
                double collapsedScale = MonitorFitScale(BallWidth(), 64);
                y = Ball.Bottom - (int)Math.Round(64 * collapsedScale * Ball.DisplayDpi / 96);
            }
            RestLocation = new Point(x, y);
        }

        // Baseline opacity of the floating monitor: translucent over the blurred desktop
        // with the glass effect, otherwise fully opaque.
        public double MonitorSolidOpacity { get { return Settings.GlassEffect ? GlassMonitorOpacity : 1.0; } }

        public void UpdateMonitorOpacity() {
            // Windows applies opacity to the whole compact window, including text.
            double solid = MonitorSolidOpacity;
            Ball.Opacity = MonitorExpanded || Settings.ClearMode ? solid : Math.Min(solid, Settings.CompactOpacity / 100.0);
            if (OpacitySlider != null) OpacitySlider.Enabled = !Settings.ClearMode;
        }

        public void ApplyGlassEffect() {
            bool glass = Settings.GlassEffect;
            Ball.GlassEnabled = glass;
            UpdateMonitorOpacity();
            if (Popup != null) {
                Popup.Opacity = glass ? GlassPanelOpacity : 1.0;
                if (Popup.IsHandleCreated) Glass.SetBlur(Popup.Handle, glass);
            }
        }

        Point DockedLocation(Point rest) {
            Rectangle area = Screen.FromPoint(rest).WorkingArea;
            int x = rest.X, y = rest.Y;
            if (Settings.DockHorizontal == "left") x = area.Left;
            if (Settings.DockHorizontal == "right") x = area.Right - Ball.Width;
            if (Settings.DockVertical == "top") y = area.Top;
            if (Settings.DockVertical == "bottom") y = area.Bottom - Ball.Height;
            return ClampLocation(x, y, Ball.Width, Ball.Height);
        }

        public void SetRingLayout() {
            string[] active = ActiveIds();
            int count = Math.Max(1, active.Length * 2), width = 60, height = 8 + 80 * count;
            UpdateMonitorLayoutScale(width, height);
            Ball.Padding = new Padding(B(4));
            Ball.ClientSize = new Size(B(width), B(height));
            int contentWidth = Ball.ClientSize.Width - Ball.Padding.Horizontal, contentHeight = Ball.ClientSize.Height - Ball.Padding.Vertical;
            int index = 0;
            foreach (string id in Model.AccountIds) {
                foreach (string window in new[] { "five", "long" }) {
                    QuotaRing ring = RingCells[id + "-" + window];
                    ring.Visible = active.Contains(id);
                    if (!ring.Visible) continue;
                    // Distribute final physical pixels so the last row never overflows by rounding.
                    int top = (int)Math.Round(contentHeight * (double)index / count), bottom = (int)Math.Round(contentHeight * (double)(index + 1) / count);
                    ring.SetBounds(0, top, contentWidth, bottom - top);
                    index++;
                }
            }
            Ball.Location = DockedLocation(RestLocation ?? Ball.Location);
            RememberMonitorPosition();
        }

        public void SetMonitorExpanded(bool expanded) {
            if (Settings.RingMode) {
                MonitorExpanded = false;
                ExpandedGrid.Visible = false; CompactGrid.Visible = false; RingPanel.Visible = true;
                UpdateMonitorOpacity();
                SetRingLayout();
                return;
            }
            RingPanel.Visible = false;
            if (!Settings.CompactMode) expanded = true;
            if (!RestLocation.HasValue) RememberMonitorPosition();
            Ball.SuspendLayout();
            try {
                MonitorExpanded = expanded;
                int width = BallWidth(), height = expanded ? ExpandedHeight() : 64;
                UpdateMonitorLayoutScale(width, height);
                UpdateMonitorOpacity();
                ExpandedGrid.Visible = expanded;
                CompactGrid.Visible = !expanded;
                Ball.Padding = expanded ? new Padding(B(14), B(10), B(14), B(10)) : new Padding(B(12), B(4), B(12), B(4));
                Ball.ClientSize = new Size(B(width), B(height));
                Ball.Location = DockedLocation(RestLocation.Value);
            } finally { Ball.ResumeLayout(true); }
        }

        public void SnapMonitor() {
            Rectangle area = Screen.FromRectangle(Ball.Bounds).WorkingArea;
            SnapResult snap = Behavior.Snap(Ball.Left, Ball.Top, Ball.Width, Ball.Height, area.Left, area.Top, area.Right, area.Bottom, B(20), Settings.EdgeSnap);
            Ball.Location = new Point(snap.X, snap.Y);
            Settings.DockHorizontal = snap.Horizontal;
            Settings.DockVertical = snap.Vertical;
            RememberMonitorPosition();
        }

        public void UpdateMonitorHover() {
            if (Settings.RingMode || !Settings.CompactMode || !Ball.Visible || ballMouseDown.HasValue || Menu.Visible) return;
            bool inside = Ball.Bounds.Contains(Cursor.Position);
            if (inside) {
                PointerLeftAt = null;
                if (!MonitorExpanded) SetMonitorExpanded(true);
            } else if (MonitorExpanded && !Popup.Visible) {
                if (!PointerLeftAt.HasValue) PointerLeftAt = DateTimeOffset.Now;
                if ((DateTimeOffset.Now - PointerLeftAt.Value).TotalMilliseconds >= 450) SetMonitorExpanded(false);
            }
        }

        public void SetMonitorSize(string size, bool initial) {
            RememberMonitorPosition();
            Settings.MonitorSize = size == "medium" || size == "large" ? size : "small";
            SetMonitorExpanded(MonitorExpanded);
            foreach (var pair in SizeItems) pair.Value.Checked = pair.Key == Settings.MonitorSize;
            RememberMonitorPosition();
            if (!initial) SaveSettings();
        }

        public void ApplyProfileLayout() {
            string[] active = ActiveIds();
            int count = Math.Max(1, active.Length);
            for (int i = 0; i < Model.AccountIds.Length; i++) {
                string id = Model.AccountIds[i];
                bool show = active.Contains(id);
                CompactGrid.ColumnStyles[i].Width = show ? 100f / count : 0;
                CompactCells[id].Visible = show;
                CompactRecovery[id].Visible = show;
                BallCell cells = BallCells[id];
                cells.Name.Visible = cells.Five.Visible = cells.Long.Visible = show;
                cells.Name.Text = AccountName(id);
                ExpandedGrid.RowStyles[i + 1].Height = show ? B(34) : 0;
            }
            SetMonitorExpanded(MonitorExpanded);
        }

        public void ResetUiPositions() {
            Ball.Location = DefaultBallLocation();
            Settings.DockHorizontal = "none"; Settings.DockVertical = "none";
            RememberMonitorPosition();
            if (Settings.RingMode) SetMonitorExpanded(false);
            Popup.Location = DefaultPanelLocation(Popup.Width, Popup.Height);
            SaveSettings();
        }
    }
}
