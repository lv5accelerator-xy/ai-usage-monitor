// Native UI smoke test: real WinForms controls, fixture data, no account access, no network,
// no saved preferences. Run with: CodexUsage.exe --ui-smoke <dataDir>. C# 5 only.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CodexUsage;

namespace CodexUsageDesktop {
    public static class UiSmoke {
        static int checks;
        static TrayApp app;
        static string output;

        static void Check(bool condition, string message) { if (!condition) throw new Exception("UI smoke test: " + message); checks++; }
        static void Pump() { Application.DoEvents(); }
        static void Shot(Control control, string name) {
            try {
                using (var bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height))) {
                    control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(Path.Combine(output, name), ImageFormat.Png);
                }
            } catch (Exception ex) { app.Log("Screenshot " + name + ": " + ex.Message); }
        }
        static Dictionary<string, object> D(params object[] pairs) {
            var map = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) map[(string)pairs[i]] = pairs[i + 1];
            return map;
        }
        static object Clone(object value) { return J.Parse(J.Json(value)); }
        static Dictionary<string, object> P(object data, int index) { return (Dictionary<string, object>)J.Arr(J.Get(data, "profiles"))[index]; }
        static List<Label> Resets(string id, string kind) {
            return app.ResetLabels.Where(l => { var t = l.Tag as ResetTag; return t != null && t.Kind == kind && J.Text(J.Get(t.Profile, "id")) == id; }).ToList();
        }

        public static int Run(TrayApp tray) {
            app = tray;
            output = Environment.GetEnvironmentVariable("CODEX_USAGE_ARTIFACTS");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(app.DataDir, "artifacts");
            Directory.CreateDirectory(output);
            string report = Path.Combine(app.LogDir, "smoke-report.txt");
            try {
                Basics();
                Dialogs();
                Geometry();
                Compact();
                Failures();
                ClientStatus();
                Rings();
                Claude();
                Memory();
                app.SaveSettings();
                Check(Snapshot(Path.Combine(app.DataDir, "ui-settings.json")) == settingsBefore, "Smoke mode never writes user preferences.");
                string text = "Windows UI: " + checks + " checks passed.";
                File.WriteAllText(report, text + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine(text);
                return 0;
            } catch (Exception ex) {
                File.WriteAllText(report, ex.ToString(), Encoding.UTF8);
                Console.Error.WriteLine(ex);
                app.Log("SMOKE FAILURE: " + ex);
                return 1;
            }
        }

        static string fixtureTime, resetTime, settingsBefore, remoteBefore;
        static string Snapshot(string path) { return File.Exists(path) ? File.ReadAllText(path) : "<missing>"; }
        static Dictionary<string, object> sample;

        static Dictionary<string, object> Sample() {
            return D("fetchedAt", fixtureTime, "profiles", new object[] {
                D("id", "personal", "label", "个人账号", "ok", true, "planType", "Pro", "fetchedAt", fixtureTime,
                    "fiveHour", D("remainingPercent", 100, "resetsAt", resetTime), "weekly", D("remainingPercent", 95, "resetsAt", resetTime), "individualLimit", null),
                D("id", "work", "label", "工作账号", "ok", true, "planType", "Business", "fetchedAt", fixtureTime,
                    "fiveHour", D("remainingPercent", 49, "resetsAt", resetTime), "weekly", D("remainingPercent", 21, "resetsAt", resetTime),
                    "individualLimit", D("remainingPercent", 68, "limit", 100, "used", 32, "resetsAt", resetTime))
            });
        }

        static void Basics() {
            fixtureTime = DateTimeOffset.Now.ToString("o");
            resetTime = DateTimeOffset.Now.AddHours(2).AddSeconds(30).ToString("o");
            sample = Sample();
            settingsBefore = Snapshot(Path.Combine(app.DataDir, "ui-settings.json"));
            remoteBefore = Snapshot(app.RemoteSettingsPath);
            Check(app.Settings.ClearMode && app.Settings.CompactMode && !app.Settings.NotificationsEnabled && app.Settings.EdgeSnap, "First-run defaults: clear, compact, snapping on, notifications off.");
            Check(TrayApp.RefreshIntervalMs == 60000, "Automatic refresh runs every minute.");
            app.LastData = Model.Merge(null, sample);
            app.Popup.Show();
            app.Ball.Show();
            app.RenderData(app.LastData);
            Pump();
            Check(app.ContentPanel.Controls.Count == 2, "Both Codex account cards render.");
            Check(app.BallCells["personal"].Five.Text == "100%", "Personal 5-hour summary is visible.");
            Check(app.BallCells["work"].Long.Text == "21%", "Work summary uses the limiting long-term quota.");
            Check(app.ResetLabels.Count == 5, "All five meaningful quota windows render.");
            foreach (int width in new[] { 620, 440, 380, 620, 440 }) {
                app.Popup.Width = app.U(width);
                app.Popup.Height = app.U(840);
                Pump();
                Check(!app.ContentPanel.HorizontalScroll.Visible, "Resizing never introduces horizontal scrolling.");
                foreach (Control card in app.ContentPanel.Controls) {
                    Check(card.Width <= app.ContentPanel.ClientSize.Width, "Cards fit resized content.");
                    var grid = card.Controls[0];
                    foreach (Control child in grid.Controls) Check(child.Bottom <= grid.ClientSize.Height && child.Right <= grid.ClientSize.Width, "Rows and columns fit inside the account card.");
                }
            }
            app.ScrollBar.Value = Math.Max(0, app.ScrollBar.Maximum - app.ScrollBar.LargeChange + 1);
            Pump();
            Control last = app.ContentPanel.Controls[app.ContentPanel.Controls.Count - 1];
            Check(last.Bottom <= app.ContentPanel.ClientSize.Height, "Vertical scroll reveals the last account actions.");
            app.ScrollBar.Value = 0;
            app.Popup.Width = app.U(440);
            Pump();
            Shot(app.Popup, "detail.png");
            Shot(app.Ball, "floating.png");
            Check(app.Menu.Renderer is HudMenuRenderer, "Context menu uses the dark HUD renderer.");
            Check(app.ContentPanel.Controls[0] is Surface && ((Surface)app.ContentPanel.Controls[0]).AccentColor == app.AccentCodex, "Codex cards use the Codex accent.");
        }

        static void Dialogs() {
            DpiForm setup = app.ShowAccountSettings(true);
            setup.Show();
            Pump();
            Check(app.SetupRows.Count == 3, "Setup page exposes two Codex slots and one Claude slot.");
            Check(app.SetupRows[2].Enabled.Text == "Claude" && app.SetupRows[0].Name.MaxLength == 12, "Third slot is Claude; names are bounded.");
            Shot(setup, "account-setup.png");
            string originalData = app.ProfilesPath;
            var originalConfig = app.ProfileConfig;
            string fixture = Path.Combine(output, "账号保存 test " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            try {
                string path = Path.Combine(fixture, "profiles.json");
                Profiles.Save(originalConfig, path);
                app.ProfilesPath = path;
                app.SetupRows[0].Name.Text = "开发账号";
                app.SetupRows[1].Enabled.Checked = false;
                ((Button)setup.Controls["saveAccounts"]).PerformClick();
                var saved = Profiles.Load(path);
                Check(J.Text(J.Get(Profiles.Find(saved, "personal"), "label")) == "开发账号", "Real Save button persists custom names.");
                Check(!J.Bool(J.Get(Profiles.Find(saved, "work"), "enabled")), "Real Save button persists a disabled account.");
                Check(J.Text(J.Get(Profiles.Find(saved, "personal"), "codexHome")) == "~/.codex-personal", "Saving preserves the account home.");
                Check(J.Text(J.Get(Profiles.Find(Profiles.Load(path + ".bak"), "personal"), "label")) == "个人", "Atomic save keeps the previous settings as backup.");
                Profiles.Save(saved, path);
                Check(Directory.GetFiles(fixture, "*.tmp").Length == 0, "Successful saves leave no temporary files.");
                app.ProfilesPath = originalData;
            } finally {
                app.ProfileConfig = originalConfig;
                try { setup.Close(); setup.Dispose(); } catch (Exception) { }
                app.ApplyProfileLayout();
                app.RenderData(app.LastData);
                Directory.Delete(fixture, true);
            }
            DpiForm remote = app.ShowRemoteSettings(true);
            remote.Show();
            Pump();
            Shot(remote, "remote-settings.png");
            var boxes = remote.Controls.OfType<CheckBox>().ToList();
            var texts = remote.Controls.OfType<TextBox>().ToList();
            Check(boxes.Count == 3 && texts.Count == 3, "Remote dialog has completion, mobile and summary options plus name, key and relay.");
            boxes[1].Checked = true;
            texts[1].Text = "short";
            bool rejected = false;
            try { ((Button)remote.Controls["saveRemote"]).PerformClick(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && !app.RemoteSettings.UsageSyncEnabled, "A pairing key shorter than 12 characters is rejected.");
            texts[1].Text = "smoke-pair-key-123456";
            texts[2].Text = "http://example.com";
            rejected = false;
            try { ((Button)remote.Controls["saveRemote"]).PerformClick(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Non-local HTTP relay is rejected.");
            texts[2].Text = "https://ntfy.sh/";
            ((Button)remote.Controls["saveRemote"]).PerformClick();
            Check(app.RemoteSettings.UsageSyncEnabled && app.RemoteSettings.PairKey == "smoke-pair-key-123456" && app.RemoteSettings.RelayUrl == "https://ntfy.sh", "Valid remote settings apply.");
            Check(Snapshot(app.RemoteSettingsPath) == remoteBefore, "Smoke mode never writes remote settings.");
            app.RemoteSettings = new RemoteSettings { DeviceName = "测试电脑" };
            try { remote.Dispose(); } catch (Exception) { }
        }

        static void Geometry() {
            using (var dpi = new DpiForm { ClientSize = new Size(200, 100) }) {
                var child = new Label();
                child.SetBounds(10, 10, 100, 20);
                dpi.Controls.Add(child);
                dpi.ApplyDpi(192, new Rectangle(20, 20, 400, 200));
                Check(dpi.DisplayDpi == 192 && child.Width == 200, "DPI transition scales child geometry.");
                dpi.ApplyDpi(96, new Rectangle(20, 20, 200, 100));
                Check(child.Width == 100, "DPI roundtrip restores geometry.");
            }
            var monitor = new Rectangle(-1920, 0, 1920, 1080);
            Check(DesktopIntegration.IsFullScreen(new Rectangle(-1920, 0, 1920, 1080), monitor), "Negative-coordinate full-screen monitor is detected.");
            Check(!DesktopIntegration.IsFullScreen(new Rectangle(-1800, 20, 1000, 700), monitor), "Normal window is not treated as full screen.");
            Check(monitor.Contains(DesktopIntegration.Clamp(new Rectangle(3000, 2000, 320, 64), monitor)), "Disconnected-display window is moved into the working area.");
            Check(DesktopIntegration.StartupCommand(@"C:\Program Files\CodexUsage.exe") == "\"C:\\Program Files\\CodexUsage.exe\"", "Startup path with spaces is quoted.");
            foreach (string size in new[] { "small", "medium", "large" }) {
                app.SetMonitorSize(size, true);
                app.SetMonitorExpanded(false);
                Check(app.Ball.Width == app.B(360) && app.Ball.Height == app.B(64), "Compact geometry follows the selected size: " + size);
                app.SetMonitorExpanded(true);
                Check(app.Ball.Height == app.B(142), "Expanded geometry follows the selected size: " + size);
                Check(app.SizeItems[size].Checked && app.SizeItems.Values.Count(i => i.Checked) == 1, "Exactly one size is checked.");
            }
            app.SetMonitorSize("small", true);
        }

        static void Compact() {
            app.SetMonitorExpanded(false);
            Pump();
            Check(app.Ball.Height == app.B(64), "Compact mode is a narrow strip.");
            double solid = TrayApp.GlassMonitorOpacity;
            Check(app.Settings.GlassEffect && app.ItemGlass.Checked && app.Ball.GlassEnabled, "Frosted glass is on by default.");
            Check(Math.Abs(app.Ball.Opacity - solid) < 0.01, "Clear mode keeps the compact strip at the glass baseline.");
            Check(app.Popup.Opacity == 1, "Detail panel stays opaque for readability.");
            app.ItemClear.PerformClick();
            Check(!app.Settings.ClearMode && Math.Abs(app.Ball.Opacity - 0.8) < 0.01, "Disabling clear mode restores 20% transparency.");
            app.OpacitySlider.Value = 45;
            Check(app.Settings.CompactOpacity == 55 && Math.Abs(app.Ball.Opacity - 0.55) < 0.01, "Slider updates and applies opacity.");
            app.SetMonitorExpanded(true);
            Check(Math.Abs(app.Ball.Opacity - solid) < 0.01, "Expanded monitor returns to the readable glass baseline.");
            app.SetMonitorExpanded(false);
            Check(Math.Abs(app.Ball.Opacity - 0.55) < 0.01, "Collapsing restores the selected opacity.");
            app.OpacitySlider.Value = 60;
            Check(Math.Abs(app.Ball.Opacity - 0.4) < 0.01, "Maximum transparency keeps the compact window visible.");
            app.OpacitySlider.Value = 20;
            app.ItemClear.PerformClick();
            Check(app.Settings.ClearMode && Math.Abs(app.Ball.Opacity - solid) < 0.01 && !app.OpacitySlider.Enabled, "Clear mode disables the slider and restores the baseline.");
            app.ItemGlass.PerformClick();
            Check(!app.Settings.GlassEffect && !app.Ball.GlassEnabled && app.Ball.Opacity == 1 && app.Popup.Opacity == 1, "Turning glass off makes the monitor opaque.");
            app.SetMonitorExpanded(true);
            Check(app.Ball.Opacity == 1, "Expanded monitor is opaque without glass.");
            app.SetMonitorExpanded(false);
            app.ItemGlass.PerformClick();
            Check(app.Settings.GlassEffect && app.Ball.GlassEnabled && Math.Abs(app.Ball.Opacity - solid) < 0.01, "Turning glass back on restores translucency.");
            Check(app.CompactGrid.Visible && !app.ExpandedGrid.Visible, "Only the compact grid is visible.");
            Check(app.CompactCells["personal"].Text == "个人 100% / 95%", "Compact mode shows independent 5-hour and long-term percentages.");
            Check(app.CompactCells["work"].Text == "工作 49% / 21%", "Compact mode preserves independent account values.");
            Check(app.CompactCells["personal"].FiveText == "100%", "Native compact control exposes 5-hour text.");
            Shot(app.Ball, "compact.png");
            Profiles.Find(app.ProfileConfig, "work")["enabled"] = false;
            app.ApplyProfileLayout();
            Check(!app.CompactCells["work"].Visible && app.Ball.Width == app.B(220), "Disabled account is hidden and the strip shrinks.");
            app.SetMonitorExpanded(true);
            Check(app.Ball.Height == app.B(108), "Single-account expanded window has no empty row.");
            Profiles.Find(app.ProfileConfig, "personal")["label"] = "开发";
            app.UpdateBallSummary(app.LastData);
            Check(app.CompactCells["personal"].AccountName == "开发", "Custom name reaches the compact native control.");
            Profiles.Find(app.ProfileConfig, "personal")["label"] = "个人";
            Profiles.Find(app.ProfileConfig, "work")["enabled"] = true;
            app.ApplyProfileLayout();
            app.SetMonitorExpanded(false);
            var work = (Dictionary<string, object>)P(app.LastData, 1)["fiveHour"];
            object savedFive = work["remainingPercent"];
            work["remainingPercent"] = 0;
            app.UpdateBallSummary(app.LastData);
            Check(app.CompactCells["work"].Text == "工作 0% / 21%", "Zero 5-hour quota keeps the independent long-term value visible.");
            Check(app.CompactCells["work"].FiveColor != app.CompactCells["work"].LongColor, "Exhausted 5h and available long-term use different colors.");
            Check(app.CompactRecovery["work"].Text.Contains("恢复"), "Exhausted account shows recovery in the compact window.");
            work["remainingPercent"] = 0.4;
            app.UpdateBallSummary(app.LastData);
            Check(app.CompactCells["work"].FiveText == "<1%" && app.BallCells["work"].Five.Text == "<1%", "Positive quota below 1% shows <1%.");
            work["remainingPercent"] = savedFive;
            app.UpdateBallSummary(app.LastData);
            Rectangle area = Screen.FromRectangle(app.Ball.Bounds).WorkingArea;
            app.Ball.Location = new Point(area.Right - app.Ball.Width - app.U(8), area.Bottom - app.Ball.Height - app.U(8));
            app.SnapMonitor();
            Check(app.Ball.Right == area.Right && app.Ball.Bottom == area.Bottom, "Drop near a corner snaps to the working area.");
            Point rest = app.RestLocation.Value;
            app.SetMonitorExpanded(true);
            Check(app.Ball.Bottom == area.Bottom, "Bottom-docked hover expansion grows upward.");
            app.SetMonitorExpanded(false);
            Check(app.Ball.Location == rest, "Collapsing restores the exact resting position.");
            app.Popup.Hide();
            Point cursor = Cursor.Position;
            try {
                Cursor.Position = new Point(app.Ball.Left + app.U(20), app.Ball.Top + app.U(20));
                if (Cursor.Position == new Point(app.Ball.Left + app.U(20), app.Ball.Top + app.U(20))) {
                    app.UpdateMonitorHover();
                    Check(app.MonitorExpanded, "Hover expands the compact strip.");
                    Cursor.Position = new Point(area.Left + 2, area.Top + 2);
                    app.PointerLeftAt = DateTimeOffset.Now.AddSeconds(-1);
                    app.UpdateMonitorHover();
                    Check(!app.MonitorExpanded, "Leaving the strip collapses it after the delay.");
                }
            } finally { Cursor.Position = cursor; }
            app.ItemCompact.PerformClick();
            Check(app.MonitorExpanded && !app.Settings.CompactMode, "Disabling compact mode keeps the full panel open.");
            app.ItemCompact.PerformClick();
            Check(!app.MonitorExpanded && app.Settings.CompactMode, "Re-enabling compact mode restores the narrow strip.");
            app.ItemNotifications.PerformClick();
            Check(app.Settings.NotificationsEnabled && app.AlertBaselinePending, "Enabling alerts requires a new baseline.");
            app.ThresholdItems[1].PerformClick();
            Check(string.Join(",", app.Settings.NotificationThresholds) == "10" && app.ThresholdItems.Count(i => i.Checked) == 1, "Threshold presets update settings with one selection.");
            app.ItemNotifications.PerformClick();
            Check(app.ItemRecovery.Checked && app.ItemFailure.Checked && app.Settings.RecoveryNotifications && app.Settings.FailureNotifications, "Recovery and read-failure reminders are on by default.");
            app.ItemRecovery.PerformClick();
            Check(!app.Settings.RecoveryNotifications, "Recovery reminders can be turned off.");
            app.ItemRecovery.PerformClick();
            Check(!app.ItemBeta.Checked && app.Settings.UpdateChannel == "stable", "Clients start on the stable update channel.");
            app.ItemBeta.PerformClick();
            Check(app.Settings.UpdateChannel == "beta", "The beta toggle switches the update channel.");
            app.ItemBeta.PerformClick();
            Check(app.Settings.UpdateChannel == "stable", "The beta toggle switches back to stable.");
            // Recovery from 0% and a 15-minute reader outage produce one notification each.
            app.SmokeBalloons.Clear();
            string before = DateTimeOffset.Now.AddMinutes(-2).ToString("o"), after = DateTimeOffset.Now.AddMinutes(-1).ToString("o");
            app.ProcessHealthNotifications(D("profiles", new object[] { D("id", "work", "ok", true, "fetchedAt", before, "fiveHour", D("remainingPercent", 0)) }));
            app.ProcessHealthNotifications(D("profiles", new object[] { D("id", "work", "ok", true, "fetchedAt", after, "fiveHour", D("remainingPercent", 64)) }));
            Check(app.SmokeBalloons.Count == 1 && app.SmokeBalloons[0].StartsWith("额度已恢复|工作") && app.SmokeBalloons[0].Contains("64%"), "Quota recovery after exhaustion notifies with the account name.");
            app.RefreshError = "Fixture: reader crashed";
            app.refreshErrorSince = DateTimeOffset.Now.AddMinutes(-16);
            app.TrackRefreshError(DateTimeOffset.Now);
            app.TrackRefreshError(DateTimeOffset.Now);
            Check(app.SmokeBalloons.Count == 2 && app.SmokeBalloons[1].StartsWith("额度读取异常|"), "A 15-minute reader outage notifies once.");
            app.RefreshError = "";
            app.TrackRefreshError(DateTimeOffset.Now);
            Check(!app.refreshErrorSince.HasValue && !app.refreshErrorNotified, "A successful refresh rearms the outage reminder.");
            app.Popup.Show();
            app.SetMonitorExpanded(true);
        }

        static void Failures() {
            app.RenderError("Fixture: offline");
            Check(app.ContentPanel.Controls.Count == 2, "Failed refresh keeps the existing cards.");
            Check(app.BallCells["personal"].Five.Text == "100%", "Failed refresh keeps last successful data.");
            Check(app.BallStatus.Text.Contains("未更新"), "Floating panel marks stale data.");
            Check(app.CompactCells["personal"].Text.Contains("!"), "Compact mode marks retained data as stale.");
            app.RefreshError = "";
            var failed = (Dictionary<string, object>)Clone(sample);
            ((object[])failed["profiles"])[1] = D("id", "work", "label", "工作账号", "ok", false, "error", "Fixture: login expired");
            app.LastData = Model.Merge(app.LastData, failed);
            app.RenderData(app.LastData);
            Check(app.BallStatus.Text == "工作未更新", "One-account failure is identified.");
            Check(app.BallCells["work"].Long.Text == "21%", "One-account failure preserves its last quota.");
            app.LastData = null;
            app.RenderError("Fixture: first read failed");
            Check(app.ContentPanel.Controls.Count == 2, "First-read failure retains account login actions.");
            app.RefreshError = "";
            app.ShowLoading();
            Check(app.ContentPanel.Controls.Count == 2, "Initial loading retains stable account layout.");
            // Partial progress: a fast account appears while the other keeps its last values.
            app.LastData = Model.Merge(null, sample);
            var partial = (Dictionary<string, object>)Clone(sample);
            ((object[])partial["profiles"])[0] = D("id", "personal", "label", "个人账号", "ok", false, "pending", true, "error", "");
            ((Dictionary<string, object>)P(partial, 1)["fiveHour"])["remainingPercent"] = 7;
            app.AcceptData(partial, false);
            Check(app.BallCells["work"].Five.Text == "7%" && app.BallCells["personal"].Five.Text == "100%", "A fast account displays while the pending account keeps its values.");
            Check(J.Bool(J.Get(Model.DisplayProfile(app.LastData, "personal"), "pending")) && !Model.Stale(Model.DisplayProfile(app.LastData, "personal")), "Fresh retained values are not failures while reading.");
            app.LastData = Model.Merge(null, sample);
            app.RenderData(app.LastData);
        }

        static void ClientStatus() {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_USAGE_CLIENT_VERSION"))) return;
            Client.AtomicJson(app.ClientStatusPath, new { state = "ready", version = "99.0.0", message = "Fixture update ready" });
            app.UpdateClientStatus();
            Check(app.RestartUpdateItem.Available && !app.CheckUpdateItem.Enabled, "A downloaded update exposes restart and blocks overlapping checks.");
            Client.AtomicJson(app.ClientStatusPath, new { state = "error", version = "99.0.0", message = "Fixture offline" });
            app.UpdateClientStatus();
            Check(!app.RestartUpdateItem.Available && app.CheckUpdateItem.Enabled, "Failed checks hide installation and allow retry.");
            File.Delete(app.ClientStatusPath);
        }

        static void Rings() {
            var ringSample = Clone(sample);
            app.LastData = Model.Merge(null, ringSample);
            app.RenderData(app.LastData);
            app.ItemRings.PerformClick();
            Check(app.Settings.RingMode, "Menu enables ring mode.");
            foreach (string edge in new[] { "left", "right", "top", "bottom" }) {
                app.Settings.DockHorizontal = edge == "left" || edge == "right" ? edge : "none";
                app.Settings.DockVertical = edge == "top" || edge == "bottom" ? edge : "none";
                app.SetMonitorExpanded(false);
                Pump();
                Rectangle area = Screen.FromRectangle(app.Ball.Bounds).WorkingArea;
                Check(app.Ball.Width == app.B(60) && app.Ball.Height == app.B(328), "All edges use the same narrow vertical stack.");
                Check(app.RingCells["work-long"].Top > app.RingCells["personal-five"].Top, "Rings stay vertically ordered.");
                Check(area.Contains(app.Ball.Bounds), "Rings remain inside the working area.");
                if (edge == "left") Check(app.Ball.Left == area.Left, "Left dock stays flush.");
                if (edge == "right") Check(app.Ball.Right == area.Right, "Right dock stays flush.");
                if (edge == "top") Check(app.Ball.Top == area.Top, "Top dock stays flush.");
                if (edge == "bottom") Check(app.Ball.Bottom == area.Bottom, "Bottom dock stays flush.");
                Check(app.RingCells["personal-five"].Value == 100 && app.RingCells["work-long"].Value == 21, "Rings show five-hour and limiting long-term quota.");
                Check(app.RingCells["personal-five"].Caption == "个人\n5h" && app.RingCells["work-long"].Caption == "工作\n长周期", "Ring captions separate account and window.");
                foreach (QuotaRing ring in app.RingCells.Values.Where(r => r.Visible)) Check(app.RingPanel.ClientRectangle.Contains(ring.Bounds), "Every ring fits its panel.");
                Shot(app.Ball, "rings-" + edge + ".png");
            }
            int originalDpi = app.Ball.DisplayDpi;
            try {
                foreach (int dpi in new[] { 96, 120, 144, 192 }) {
                    app.Ball.ApplyDpi(dpi, new Rectangle(app.Ball.Left, app.Ball.Top, 60 * dpi / 96, 328 * dpi / 96));
                    Pump();
                    Check(app.Ball.Width == app.B(60) && app.Ball.Height == app.B(328), "Ring geometry stays narrow through DPI transitions.");
                    foreach (QuotaRing ring in app.RingCells.Values.Where(r => r.Visible)) Check(app.RingPanel.ClientRectangle.Contains(ring.Bounds), "DPI-scaled rings fit the panel.");
                    Shot(app.Ball, "rings-dpi-" + dpi + ".png");
                }
            } finally { app.Ball.ApplyDpi(originalDpi, new Rectangle(app.Ball.Left, app.Ball.Top, 60 * originalDpi / 96, 328 * originalDpi / 96)); }
            var tooltip = (Dictionary<string, object>)Clone(sample);
            // Half-hour offsets keep the countdown text stable however close in time both sides compute it.
            ((Dictionary<string, object>)P(tooltip, 1)["weekly"])["resetsAt"] = DateTimeOffset.Now.AddDays(3).AddHours(1).AddMinutes(30).ToString("o");
            ((Dictionary<string, object>)P(tooltip, 1)["individualLimit"])["resetsAt"] = DateTimeOffset.Now.AddDays(20).AddHours(1).AddMinutes(30).ToString("o");
            app.UpdateBallSummary(tooltip);
            string weeklyTip = app.ToolTip.GetToolTip(app.RingCells["work-long"]), weeklyExpected = Model.ResetText(J.Get(P(tooltip, 1), "weekly", "resetsAt"));
            Check(weeklyTip.EndsWith(weeklyExpected), "Weekly-limited ring tooltip uses the weekly reset. expected=" + weeklyExpected + " actual=" + weeklyTip.Replace("\r\n", " | "));
            ((Dictionary<string, object>)P(tooltip, 1)["individualLimit"])["remainingPercent"] = 10;
            app.UpdateBallSummary(tooltip);
            Check(app.ToolTip.GetToolTip(app.RingCells["work-long"]).EndsWith(Model.ResetText(J.Get(P(tooltip, 1), "individualLimit", "resetsAt"))), "Monthly-limited ring tooltip uses the monthly reset.");
            Check(app.ToolTip.GetToolTip(app.RingCells["work-five"]).EndsWith(Model.ResetText(J.Get(P(tooltip, 1), "fiveHour", "resetsAt"))), "Five-hour ring tooltip keeps its own reset.");
            app.UpdateBallSummary(app.LastData);
            Profiles.Find(app.ProfileConfig, "work")["enabled"] = false;
            app.ApplyProfileLayout();
            Check(!app.RingCells["work-five"].Visible && app.Ball.Width == app.B(60) && app.Ball.Height == app.B(168), "Single account uses two rings.");
            Profiles.Find(app.ProfileConfig, "work")["enabled"] = true;
            app.ApplyProfileLayout();
            app.RefreshError = "fixture offline";
            app.UpdateBallSummary(app.LastData);
            Check(app.RingCells["personal-five"].Stale, "Retained ring values are marked stale after errors.");
            app.RefreshError = "";
            app.UpdateBallSummary(null);
            Check(app.RingCells["personal-five"].Value == -1, "Missing quota never appears as zero.");
            app.UpdateBallSummary(app.LastData);
            app.ResetUiPositions();
            Check(app.Settings.DockHorizontal == "none" && app.Settings.DockVertical == "none", "Reset lets ring mode float.");
            app.ItemRings.PerformClick();
            Check(!app.Settings.RingMode && app.CompactGrid.Visible, "Menu restores compact mode.");
        }

        static void Claude() {
            var claude = D("id", "claude", "label", "Claude", "provider", "claude", "ok", true, "planType", "Max 5x", "fetchedAt", fixtureTime, "quotaError", null,
                "fiveHour", D("kind", "5h", "label", "5 小时", "remainingPercent", 80, "resetsAt", resetTime),
                "weekly", D("kind", "weekly", "label", "Weekly", "remainingPercent", 55, "resetsAt", resetTime),
                "windows", new object[] { D("kind", "weekly-sonnet", "label", "Sonnet 每周", "remainingPercent", 61, "resetsAt", resetTime) },
                "individualLimit", null,
                "localUsage", D("today", D("inputTokens", 1000, "outputTokens", 2000, "cacheWriteTokens", 3000, "cacheReadTokens", 1200000, "totalTokens", 1206000, "costUsd", 1.23, "messages", 42),
                    "week", D("totalTokens", 9800000, "costUsd", 15.5, "messages", 300),
                    "days", new object[] { D("date", DateTime.Today.ToString("yyyy-MM-dd"), "totalTokens", 1206000, "costUsd", 1.23) }, "topModelToday", "Opus 5.5"));
            var personal = (Dictionary<string, object>)Clone(P(sample, 0));
            personal["localUsage"] = D("today", D("inputTokens", 5000, "outputTokens", 900, "cacheWriteTokens", 0, "cacheReadTokens", 40000, "totalTokens", 45900, "costUsd", 0.08, "messages", 12),
                "week", D("totalTokens", 300000, "costUsd", 0.6, "messages", 80),
                "days", new object[] { D("date", DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"), "totalTokens", 90000, "costUsd", 0.2), D("date", DateTime.Today.ToString("yyyy-MM-dd"), "totalTokens", 45900, "costUsd", 0.08) },
                "topModelToday", "GPT-5.5");
            var data = D("fetchedAt", fixtureTime, "profiles", new object[] { personal, Clone(P(sample, 1)), claude });
            Profiles.Find(app.ProfileConfig, "claude")["enabled"] = true;
            app.ApplyProfileLayout();
            app.SetMonitorExpanded(false);
            Check(app.Ball.Width == app.B(510), "Three accounts widen the compact strip.");
            app.SetMonitorExpanded(true);
            Check(app.Ball.Height == app.B(176), "Three accounts add one expanded row.");
            app.LastData = Model.Merge(null, data);
            app.RenderData(app.LastData);
            Pump();
            Check(app.ContentPanel.Controls.Count == 3, "Codex and Claude cards render together.");
            Check(app.BallCells["claude"].Five.Text == "80%" && app.BallCells["claude"].Long.Text == "55%", "Claude summary shows 5-hour and weekly remaining.");
            string tip = app.ToolTip.GetToolTip(app.BallCells["claude"].Long);
            Check(tip.Contains("Sonnet 每周") && tip.Contains("tokens"), "Claude tooltip lists model weekly quota and local usage.");
            Control claudeCard = app.ContentPanel.Controls[2];
            var claudeTexts = TrayApp.Tree(claudeCard).Select(c => c.Text ?? "").ToList();
            Check(claudeTexts.Count(t => t.Contains("本机 Claude Code 用量") && t.Contains("1.2M")) == 1, "Claude card shows local token usage.");
            Check(claudeTexts.Contains("Claude · Max 5x") && claudeTexts.Contains("Sonnet 每周 · 剩余"), "Claude card shows plan and model weekly row.");
            Check(((Surface)claudeCard).AccentColor == app.AccentClaude, "Claude card uses the Claude accent.");
            var codexTree = TrayApp.Tree(app.ContentPanel.Controls[0]).ToList();
            Check(codexTree.Count(c => (c.Text ?? "").Contains("本机 Codex 用量") && (c.Text ?? "").Contains("45.9K")) == 1, "Codex card shows local Codex usage.");
            var chart = codexTree.OfType<UsageSparkline>().ToList();
            Check(chart.Count == 1 && chart[0].Values.Length == 7 && chart[0].HighlightIndex == 6 && chart[0].Values[6] == 45900 && chart[0].Labels[6] == "今", "Codex card has a seven-day trend ending today.");
            Check(TrayApp.Tree(claudeCard).OfType<UsageSparkline>().Count() == 1 && !TrayApp.Tree(app.ContentPanel.Controls[1]).OfType<UsageSparkline>().Any(), "Charts appear only where local logs exist.");
            foreach (int width in new[] { 620, 380 }) {
                app.Popup.Width = app.U(width);
                Pump();
                foreach (Control card in app.ContentPanel.Controls) {
                    var grid = card.Controls[0];
                    foreach (Control child in grid.Controls) Check(child.Bottom <= grid.ClientSize.Height && child.Right <= grid.ClientSize.Width, "Claude and local-usage card rows fit.");
                }
            }
            app.Popup.Width = app.U(440);
            Pump();
            Shot(app.Popup, "detail-claude.png");
            Shot(app.Ball, "floating-claude.png");
            // Burn-rate forecast: personal 5h falls 30%/h with 49% left and reset in 2 h -> runs out first.
            ((Dictionary<string, object>)personal["fiveHour"])["remainingPercent"] = 49;
            app.UsageHistory = new Dictionary<string, List<UsageSample>>();
            for (int m = 20; m >= 0; m--) {
                Forecasts.AddSamples(app.UsageHistory, D("profiles", new object[] { D("id", "personal", "ok", true, "fetchedAt", DateTimeOffset.Now.AddMinutes(-m).ToString("o"), "fiveHour", D("remainingPercent", 49 + m * 0.5, "resetsAt", resetTime)) }), DateTimeOffset.Now);
            }
            app.UpdateStatus(false);
            var five = Resets("personal", "fiveHour");
            Check(five.Count == 1 && five[0].Text.Contains("用完") && five[0].ForeColor.ToArgb() == app.Warning.ToArgb(), "Card reset line shows the amber exhaustion forecast: " + (five.Count > 0 ? five[0].Text : "none"));
            Check(app.ToolTip.GetToolTip(five[0]).Contains("%/小时"), "Forecast tooltip shows the burn rate.");
            Check(!Resets("claude", "fiveHour")[0].Text.Contains("用完"), "Accounts without history show no forecast.");
            app.SetMonitorExpanded(false);
            app.UpdateBallSummary(app.LastData);
            Check(app.CompactRecovery["personal"].Text.StartsWith("5h 预计") && app.CompactRecovery["personal"].Text.EndsWith("用完"), "Compact strip warns before the 5h quota runs out: " + app.CompactRecovery["personal"].Text);
            Shot(app.Ball, "compact-claude.png");
            ((Dictionary<string, object>)personal["fiveHour"])["remainingPercent"] = 100;
            app.UsageHistory = new Dictionary<string, List<UsageSample>>();
            foreach (var variant in new[] {
                D("id", "claude", "label", "Claude", "provider", "claude", "ok", true, "planType", "本地统计", "fetchedAt", fixtureTime, "fiveHour", null, "weekly", null, "windows", new object[0], "individualLimit", null, "quotaError", "未检测到 Claude 订阅登录，只显示本机 Claude Code 用量。", "localUsage", claude["localUsage"]),
                D("id", "claude", "label", "Claude", "provider", "claude", "ok", false, "fetchedAt", fixtureTime, "error", "Claude 登录已过期：打开 Claude Code 运行一次。", "localUsage", claude["localUsage"]),
                D("id", "claude", "label", "Claude", "provider", "claude", "ok", false, "fetchedAt", fixtureTime, "error", "未检测到 Claude Code 登录。") }) {
                app.RenderData(D("fetchedAt", fixtureTime, "profiles", new object[] { Clone(P(sample, 0)), Clone(P(sample, 1)), variant }));
                Pump();
                var grid = app.ContentPanel.Controls[2].Controls[0];
                foreach (Control child in grid.Controls) Check(child.Bottom <= grid.ClientSize.Height, "Local-only and error Claude cards fit.");
            }
            app.UpdateBallSummary(D("profiles", new object[] { D("id", "claude", "ok", true, "provider", "claude", "fetchedAt", fixtureTime, "fiveHour", null, "weekly", null, "quotaError", "x") }));
            Check(app.BallCells["claude"].Five.Text == "—", "Missing Claude quota is unknown, never 0% or 100%.");
            app.Settings.RingMode = true;
            app.SetMonitorExpanded(false);
            Check(app.RingCells["claude-five"].Visible && app.RingCells["claude-long"].Visible && app.Ball.Height == app.B(8 + 80 * 6), "Claude rings are visible in a six-ring stack.");
            app.Settings.RingMode = false;
            Profiles.Find(app.ProfileConfig, "claude")["enabled"] = false;
            app.ApplyProfileLayout();
            Check(!app.CompactCells["claude"].Visible && !app.RingCells["claude-five"].Visible && app.Ball.Width == app.B(360), "Disabled Claude slot is hidden everywhere.");
            app.LastData = Model.Merge(null, sample);
            app.RenderData(app.LastData);
        }

        // Accelerated lifetime regression: repeated renders and status ticks keep memory bounded.
        static void Memory() {
            Func<long> managed = delegate { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(false); };
            long idleStart = managed();
            for (int tick = 0; tick < 2000; tick++) { app.UpdateStatus(false); app.UpdateMonitorHover(); }
            Check(managed() - idleStart < 16L * 1024 * 1024, "Idle status updates keep retained memory bounded.");
            var measurements = new List<long>();
            int handles0 = 0, handles3 = 0;
            for (int cycle = 0; cycle < 4; cycle++) {
                var oldCard = new WeakReference(app.ContentPanel.Controls[0]);
                for (int refresh = 0; refresh < 60; refresh++) {
                    app.RenderData(Model.Merge(null, sample));
                    for (int tick = 0; tick < 10; tick++) { app.UpdateStatus(false); app.UpdateMonitorHover(); }
                    Pump();
                }
                measurements.Add(managed());
                int handles = System.Diagnostics.Process.GetCurrentProcess().HandleCount;
                if (cycle == 0) handles0 = handles;
                if (cycle == 3) handles3 = handles;
                Check(!oldCard.IsAlive, "Disposed account cards are collectible.");
                Console.WriteLine("MEMORY batch=" + cycle + " managedMB=" + (measurements[cycle] / 1048576.0).ToString("0.0") + " handles=" + handles);
            }
            Check(measurements[3] - measurements[0] < 32L * 1024 * 1024, "Retained managed heap stays bounded after warmup.");
            Check(handles3 - handles0 < 100, "Repeated refreshes do not leak process handles.");
        }
    }
}
