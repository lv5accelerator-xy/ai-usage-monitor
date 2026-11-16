// Detail cards, summaries, refresh lifecycle and notifications. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CodexUsage;

namespace CodexUsageDesktop {
    public sealed partial class TrayApp {
        public const int LocalUsageBlockHeight = 104;

        // ---------------------------------------------------------------- cards
        void ClearContent() {
            ResetLabels.Clear();
            CardStates.Clear();
            while (ContentPanel.Controls.Count > 0) {
                Control control = ContentPanel.Controls[0];
                ContentPanel.Controls.RemoveAt(0);
                foreach (Control child in Tree(control).ToList()) {
                    ToolTip.SetToolTip(child, null);
                    child.MouseWheel -= cardWheelHandler;
                    child.Click -= cardLoginHandler;
                    child.Tag = null;
                }
                control.Dispose();
            }
        }

        void AddQuotaRow(TableLayoutPanel parent, string title, object window, int row, bool primary, string detail, Color? accent, object profile, string kind) {
            var grid = NewGrid(2);
            AddPercentColumns(grid, 60, 40);
            AddRow(grid, primary ? 48 : 30);
            AddRow(grid, 6);
            AddRow(grid, 28);
            grid.Controls.Add(NewLabel(title, 10, false), 0, 0);
            double? remaining = J.Num(J.Get(window, "remainingPercent"));
            Label pct = NewLabel(Model.Percent(remaining), primary ? 22 : 14, false, true, true);
            pct.TextAlign = ContentAlignment.MiddleRight;
            pct.ForeColor = remaining.HasValue && remaining.Value > 35 && accent.HasValue ? accent.Value : ValueColor(remaining, false);
            grid.Controls.Add(pct, 1, 0);
            var bar = new QuotaBar { Dock = DockStyle.Fill, Margin = new Padding(0), Value = remaining ?? 0, FillColor = BarColor(remaining, accent), TrackColor = Track };
            grid.Controls.Add(bar, 0, 1);
            grid.SetColumnSpan(bar, 2);
            Label reset = NewLabel("", 9, true);
            reset.Tag = new ResetTag { Window = window, Detail = detail ?? "", Profile = profile, Kind = kind ?? "" };
            ResetLabels.Add(reset);
            grid.Controls.Add(reset, 0, 2);
            grid.SetColumnSpan(reset, 2);
            parent.Controls.Add(grid, 0, row);
            parent.SetColumnSpan(grid, 2);
        }

        // Local usage block: one summary line plus a seven-day trend chart (58 + 46 logical px).
        void AddLocalUsageRows(TableLayoutPanel parent, object profile, int row, Color accent) {
            string provider = Model.IsClaude(profile) ? "claude" : "codex";
            object usage = J.Get(profile, "localUsage");
            string detail = Model.LocalUsageDetail(usage, provider);
            string title = provider == "codex" ? "本机 Codex 用量" : "本机 Claude Code 用量";
            AddRow(parent, 58);
            Label local = NewLabel(title + "\r\n" + Model.LocalUsageSummary(usage), 9, true);
            parent.Controls.Add(local, 0, row);
            parent.SetColumnSpan(local, 2);
            ToolTip.SetToolTip(local, detail);
            AddRow(parent, 46);
            UsageTrend trend = Model.Trend(usage, DateTime.Today);
            var chart = new UsageSparkline { Dock = DockStyle.Fill, Margin = new Padding(0, U(4), 0, U(2)), BarColor = accent, TrackColor = Track, CaptionColor = Caption, HighlightIndex = trend.Highlight, AccessibleName = title + " 近 7 天趋势" };
            chart.SetData(trend.Values, trend.Labels);
            parent.Controls.Add(chart, 0, row + 1);
            parent.SetColumnSpan(chart, 2);
            ToolTip.SetToolTip(chart, detail);
        }

        void AddAccountCard(Dictionary<string, object> profile) {
            string id = J.Text(J.Get(profile, "id"));
            bool isClaude = Model.IsClaude(profile), ok = J.Bool(J.Get(profile, "ok"));
            bool hasMonthly = Model.MeaningfulLimit(J.Get(profile, "individualLimit"));
            var extraWindows = isClaude ? J.Arr(J.Get(profile, "windows")).Where(w => w != null && J.Text(J.Get(w, "kind")).StartsWith("weekly-")).ToList() : new List<object>();
            bool localOnly = isClaude && ok && J.Text(J.Get(profile, "quotaError")).Trim().Length > 0;
            bool hasLocalKey = profile.ContainsKey("localUsage");
            object usage = J.Get(profile, "localUsage");
            bool showLocal = isClaude ? hasLocalKey && (ok || usage != null) : ok && usage != null;
            Color accent = Accent(isClaude ? "claude" : id);
            var card = new Surface { Radius = U(6), HudUnit = (float)(Popup.DisplayDpi / 96.0), BackColor = CardBack, BorderColor = CardBorder, AccentColor = accent, Padding = new Padding(U(16)), Margin = new Padding(0, 0, 0, U(12)) };
            int height;
            if (isClaude) {
                int body = !ok ? 104 : localOnly ? 60 : 100 + 82 + 82 * extraWindows.Count;
                if (showLocal) body += LocalUsageBlockHeight;
                height = 106 + body;
            } else {
                height = !ok ? 206 : hasMonthly ? 366 : 284;
                if (showLocal) height += LocalUsageBlockHeight;
            }
            card.Height = U(height);
            var grid = NewGrid(2);
            AddPercentColumns(grid, 60, 40);
            AddRow(grid, 36);
            Label name = NewLabel(J.Text(J.Get(profile, "label")), 11, false, true, false);
            name.ForeColor = accent;
            grid.Controls.Add(name, 0, 0);
            string planText = J.Text(J.Get(profile, "planType"));
            if (planText.Trim().Length == 0 || planText == "unknown") planText = "账号";
            Label plan = NewLabel((isClaude ? "Claude · " : "Codex · ") + planText, 9, true);
            plan.TextAlign = ContentAlignment.MiddleRight;
            grid.Controls.Add(plan, 1, 0);
            card.Controls.Add(grid);
            int row;
            if (localOnly) {
                AddRow(grid, 60);
                Label note = NewLabel(J.Text(J.Get(profile, "quotaError")), 10, true);
                grid.Controls.Add(note, 0, 1);
                grid.SetColumnSpan(note, 2);
                row = 2;
            } else if (ok) {
                AddRow(grid, 100);
                AddQuotaRow(grid, "5 小时 · 剩余", J.Get(profile, "fiveHour"), 1, true, "", accent, profile, "fiveHour");
                row = 2;
                if (hasMonthly) {
                    AddRow(grid, 82);
                    object limit = J.Get(profile, "individualLimit");
                    string detail = J.Get(limit, "used") != null && J.Get(limit, "limit") != null ? "已用 " + J.Text(J.Get(limit, "used")) + " / " + J.Text(J.Get(limit, "limit")) : "";
                    AddQuotaRow(grid, "工作空间 / 月度", limit, row, false, detail, accent, null, "");
                    row++;
                }
                AddRow(grid, 82);
                AddQuotaRow(grid, isClaude ? "每周 · 全部模型 · 剩余" : "每周 · 剩余", J.Get(profile, "weekly"), row, false, "", accent, profile, "weekly");
                row++;
                foreach (object extra in extraWindows) {
                    AddRow(grid, 82);
                    AddQuotaRow(grid, J.Text(J.Get(extra, "label")) + " · 剩余", extra, row, false, "", accent, null, "");
                    row++;
                }
            } else {
                AddRow(grid, 104);
                string error = J.Text(J.Get(profile, "error"));
                Label errorLabel = NewLabel(isClaude ? "暂时无法读取 Claude 额度。\r\n" + error : "暂时无法读取额度。\r\n请重试，或重新登录这个账号。", 10, true);
                grid.Controls.Add(errorLabel, 0, 1);
                grid.SetColumnSpan(errorLabel, 2);
                ToolTip.SetToolTip(errorLabel, error);
                row = 2;
            }
            if (showLocal) { AddLocalUsageRows(grid, profile, row, accent); row += 2; }
            AddRow(grid, 32);
            Label state = NewLabel("", 9, true);
            grid.Controls.Add(state, 0, row);
            CardStates.Add(new CardState { Label = state, Profile = profile });
            Button login = NewButton("重新登录");
            login.Tag = id;
            login.AccessibleName = J.Text(J.Get(profile, "label")) + "：重新登录";
            login.Click += cardLoginHandler;
            grid.Controls.Add(login, 1, row);
            foreach (Control control in Tree(card).ToList()) control.MouseWheel += cardWheelHandler;
            ContentPanel.Controls.Add(card);
        }

        public void UpdateCardWidths() {
            if (ContentPanel == null || ScrollBar == null || layingOutCards) return;
            layingOutCards = true;
            try {
                int width = Math.Max(1, ContentPanel.ClientSize.Width - ContentPanel.Padding.Horizontal);
                int total = ContentPanel.Padding.Vertical;
                foreach (Control card in ContentPanel.Controls) total += card.Height + card.Margin.Bottom;
                int maxScroll = Math.Max(0, total - ContentPanel.ClientSize.Height);
                // Own the vertical range explicitly; AutoScroll can retain a horizontal range on resize.
                ScrollBar.Value = Math.Min(ScrollBar.Value, maxScroll);
                ScrollBar.LargeChange = Math.Max(1, ContentPanel.ClientSize.Height);
                ScrollBar.Maximum = Math.Max(0, total - 1);
                ScrollBar.SmallChange = U(36);
                ScrollBar.Visible = maxScroll > 0;
                int top = ContentPanel.Padding.Top - ScrollBar.Value;
                foreach (Control card in ContentPanel.Controls) {
                    card.Width = width;
                    card.Location = new Point(ContentPanel.Padding.Left, top);
                    top += card.Height + card.Margin.Bottom;
                }
            } finally { layingOutCards = false; }
        }

        public void MoveVerticalScroll(int delta) {
            int max = Math.Max(0, ScrollBar.Maximum - ScrollBar.LargeChange + 1);
            ScrollBar.Value = Math.Max(0, Math.Min(max, ScrollBar.Value - delta / 120 * U(72)));
        }

        // ---------------------------------------------------------------- summary (floating monitor)
        public void UpdateBallSummary(object data) {
            DateTimeOffset now = DateTimeOffset.Now;
            bool errorShown = data != null || !string.IsNullOrEmpty(RefreshError);
            foreach (string id in Model.AccountIds) {
                object profile = Model.DisplayProfile(data, id);
                BallCell cells;
                if (!BallCells.TryGetValue(id, out cells)) continue;
                QuotaPair pair = Model.Pair(profile);
                bool stale = Model.Stale(profile, now) || !string.IsNullOrEmpty(RefreshError);
                string account = AccountName(id);
                cells.Name.Text = account;
                cells.Five.Text = Model.Percent(pair.FiveHour);
                cells.Long.Text = Model.Percent(pair.LongTerm);
                cells.Five.ForeColor = ValueColor(pair.FiveHour, stale);
                cells.Long.ForeColor = ValueColor(pair.LongTerm, stale);
                cells.Name.ForeColor = stale && errorShown ? Warning : Accent(id);
                bool isClaude = id == "claude" || Model.IsClaude(profile);
                string tip = isClaude ? "长周期显示 Claude 每周（全部模型）剩余额度。" : "长周期显示每周与有效月度额度中，剩余比例较低的一项。";
                tip += "\r\n当前来源：" + pair.Source;
                string updateText = Model.UpdateText(profile, now);
                tip += "\r\n" + updateText;
                if (J.Get(profile, "weekly") != null) tip += "\r\n每周剩余：" + Model.Percent(J.Get(profile, "weekly", "remainingPercent"));
                if (Model.MeaningfulLimit(J.Get(profile, "individualLimit"))) tip += "\r\n工作空间 / 月度剩余：" + Model.Percent(J.Get(profile, "individualLimit", "remainingPercent"));
                foreach (object extra in J.Arr(J.Get(profile, "windows")))
                    if (extra != null && J.Text(J.Get(extra, "kind")).StartsWith("weekly-")) tip += "\r\n" + J.Text(J.Get(extra, "label")) + "剩余：" + Model.Percent(J.Get(extra, "remainingPercent"));
                if (isClaude && J.Text(J.Get(profile, "quotaError")).Trim().Length > 0) tip += "\r\n" + J.Text(J.Get(profile, "quotaError"));
                if (J.Get(profile, "localUsage") != null) tip += "\r\n" + Model.LocalUsageSummary(J.Get(profile, "localUsage"));
                if (stale && errorShown) tip += "\r\n数据未更新，请刷新后确认。";
                Forecast fiveForecast = ProfileForecast(profile, "fiveHour"), longForecast = ProfileForecast(profile, "weekly");
                tip += "\r\n" + Forecasts.Detail(longForecast).Replace("消耗预测", "每周消耗预测");
                object fiveReset = J.Get(profile, "fiveHour", "resetsAt");
                ToolTip.SetToolTip(cells.Long, tip);
                ToolTip.SetToolTip(cells.Five, "5 小时窗口剩余额度。\r\n" + updateText + "\r\n" + Model.ResetText(fiveReset, now) + "\r\n" + Forecasts.Detail(fiveForecast));
                ToolTip.SetToolTip(cells.Name, updateText + "\r\n" + J.Text(J.Get(profile, "refreshError")));
                foreach (string window in new[] { "five", "long" }) {
                    QuotaRing ring;
                    if (!RingCells.TryGetValue(id + "-" + window, out ring)) continue;
                    double? value = window == "five" ? pair.FiveHour : pair.LongTerm;
                    ring.Value = value ?? -1;
                    ring.Stale = stale;
                    ring.Caption = account + "\n" + (window == "five" ? "5h" : "长周期");
                    ring.ForeColor = ValueColor(value, stale);
                    ring.FillColor = stale || !value.HasValue ? TextMuted : BarColor(value, Accent(id));
                    ring.CaptionColor = Accent(id);
                    ring.AccessibleName = ring.Caption.Replace("\n", " · ") + " 剩余 " + Model.Percent(value) + (stale ? " 数据未更新" : "");
                    object ringReset = window == "five" ? fiveReset : pair.LongTermResetsAt;
                    ToolTip.SetToolTip(ring, ring.AccessibleName + "\r\n" + tip + "\r\n" + Model.ResetText(ringReset, now));
                    ring.Invalidate();
                }
                SplitQuotaLabel label;
                if (CompactCells.TryGetValue(id, out label)) {
                    string mark = stale && errorShown ? " ! " : " ";
                    label.Text = account + mark + Model.Percent(pair.FiveHour) + " / " + Model.Percent(pair.LongTerm);
                    Label recovery = CompactRecovery[id];
                    recovery.Text = Model.CompactRecovery(J.Get(profile, "fiveHour"), stale, now);
                    recovery.ForeColor = pair.FiveHour.HasValue && pair.FiveHour.Value == 0 && !stale ? Warning : TextMuted;
                    // Not exhausted yet: show the forecast when either window runs out before its reset.
                    if (!stale && pair.FiveHour.HasValue && pair.FiveHour.Value > 0) {
                        if (fiveForecast != null && fiveForecast.State == "exhaust") { recovery.Text = "5h " + fiveForecast.Text; recovery.ForeColor = Warning; }
                        else if (longForecast != null && longForecast.State == "exhaust") { recovery.Text = "长周期 " + longForecast.Text; recovery.ForeColor = Warning; }
                    }
                    label.AccountName = account + (stale && data != null ? " !" : "");
                    label.FiveText = Model.Percent(pair.FiveHour);
                    label.LongText = Model.Percent(pair.LongTerm);
                    label.ForeColor = stale ? Warning : TextPrimary;
                    label.FiveColor = ValueColor(pair.FiveHour, stale);
                    label.LongColor = ValueColor(pair.LongTerm, stale);
                    label.Invalidate();
                    ToolTip.SetToolTip(label, account + " · 5h / 长周期剩余（长周期指长周期额度）\r\n5 小时：" + Model.Percent(pair.FiveHour) + "\r\n" + tip);
                    string stamp = Model.LocalTimestamp(fiveReset);
                    ToolTip.SetToolTip(recovery, "5 小时额度用尽时显示本机时间；以实际刷新结果为准。\r\n" + Model.ResetText(fiveReset, now) + (stamp.Length > 0 ? "\r\n" + stamp : ""));
                }
            }
        }

        // ---------------------------------------------------------------- status
        public void UpdateStatus(bool scheduled) {
            DateTimeOffset utc = DateTimeOffset.UtcNow;
            // Labels show minutes; keep the one-second desktop checks cheap.
            if (scheduled && (utc - lastStatusPaint).TotalSeconds < 15) return;
            lastStatusPaint = utc;
            var problems = new List<string>();
            foreach (object profile in J.Arr(J.Get(LastData, "profiles"))) if (profile != null && Model.Stale(profile)) problems.Add(J.Text(J.Get(profile, "label")));
            bool warning = problems.Count > 0 || !string.IsNullOrEmpty(RefreshError);
            string text, compact;
            if (RefreshPending) { text = "正在更新 · 保留上次数据"; compact = "正在更新…"; }
            else if (warning) {
                text = problems.Count > 0 ? string.Join("、", problems) + "未更新 · 请重试" : "刷新失败 · 显示上次数据";
                compact = problems.Count == 1 ? problems[0].Replace("账号", "") + "未更新" : "数据未更新 · 请重试";
            } else if (LastData == null) { text = "等待首次读取 · 每 1 分钟自动更新"; compact = "等待首次读取"; }
            else {
                DateTimeOffset? fetched = J.Time(J.Get(LastData, "fetchedAt"));
                string stamp = fetched.HasValue ? fetched.Value.ToLocalTime().ToString("HH:mm", J.Inv) : "--:--";
                text = "更新于 " + stamp + " · 每 1 分钟自动刷新"; compact = "更新于 " + stamp;
            }
            StatusLabel.Text = text;
            BallStatus.Text = compact;
            Color color = warning ? Warning : TextMuted;
            StatusLabel.ForeColor = color; BallStatus.ForeColor = color;
            ToolTip.SetToolTip(StatusLabel, text + "\r\n" + RefreshError);
            ToolTip.SetToolTip(BallStatus, text + "\r\n" + RefreshError);
            bool loading = LastData == null && string.IsNullOrEmpty(RefreshError);
            foreach (CardState entry in CardStates) {
                bool stale = (Model.Stale(entry.Profile) || !string.IsNullOrEmpty(RefreshError)) && !loading;
                entry.Label.Text = J.Bool(J.Get(entry.Profile, "pending")) ? "正在读取 · 保留上次数据" : loading ? "等待读取…" : stale ? "数据未更新" : "已连接 · 数据已更新";
                entry.Label.ForeColor = stale ? Warning : TextMuted;
                ToolTip.SetToolTip(entry.Label, Model.UpdateText(entry.Profile, DateTimeOffset.Now) + "\r\n" + J.Text(J.Get(entry.Profile, "refreshError")));
            }
            foreach (Label label in ResetLabels) {
                var tag = label.Tag as ResetTag;
                if (tag == null) continue;
                object window = tag.Window;
                if (loading) label.Text = "等待读取…";
                else if (window == null || J.Num(J.Get(window, "remainingPercent")) == null) label.Text = "未提供此额度";
                else label.Text = Model.ResetText(J.Get(window, "resetsAt"));
                label.ForeColor = TextMuted;
                Forecast forecast = tag.Kind.Length > 0 ? ProfileForecast(tag.Profile, tag.Kind) : null;
                if (forecast != null && (forecast.State == "exhaust" || forecast.State == "safe")) {
                    label.Text += " · " + forecast.Text;
                    if (forecast.State == "exhaust") label.ForeColor = Warning;
                }
                string tip = label.Text;
                if (tag.Kind.Length > 0) tip += "\r\n" + Forecasts.Detail(forecast);
                if (tag.Detail.Length > 0) tip = tag.Detail + "\r\n" + tip;
                string stamp = Model.LocalTimestamp(J.Get(window, "resetsAt"));
                if (stamp.Length > 0) tip += "\r\n" + stamp;
                ToolTip.SetToolTip(label, tip);
            }
            UpdateBallSummary(LastData);
            if (NotifyIcon != null) {
                string tray = "Codex + Claude 额度 · " + compact;
                NotifyIcon.Text = tray.Length > 63 ? tray.Substring(0, 63) : tray;
            }
        }

        public void RenderData(object data) {
            int scroll = ScrollBar.Value;
            ContentPanel.SuspendLayout();
            try {
                ClearContent();
                string[] active = ActiveIds();
                foreach (object item in J.Arr(J.Get(data, "profiles"))) {
                    var profile = item as Dictionary<string, object>;
                    if (profile == null) continue;
                    string id = J.Text(J.Get(profile, "id"));
                    if (!active.Contains(id)) continue;
                    profile["label"] = AccountName(id);
                    AddAccountCard(profile);
                }
                UpdateCardWidths();
            } finally { ContentPanel.ResumeLayout(true); }
            ScrollBar.Value = Math.Min(scroll, Math.Max(0, ScrollBar.Maximum - ScrollBar.LargeChange + 1));
            UpdateCardWidths();
            UpdateStatus(false);
        }

        object PlaceholderData(bool ok, string error) {
            var profiles = new List<object>();
            foreach (object profile in J.Arr(J.Get(ProfileConfig, "profiles"))) {
                if (!Model.Enabled(profile)) continue;
                var entry = new Dictionary<string, object> { { "id", J.Text(J.Get(profile, "id")) }, { "label", J.Text(J.Get(profile, "label")) }, { "ok", ok } };
                if (ok) entry["planType"] = "等待读取"; else entry["error"] = error;
                profiles.Add(entry);
            }
            return new Dictionary<string, object> { { "profiles", profiles.ToArray() } };
        }

        public void ShowLoading() { RenderData(PlaceholderData(true, null)); }

        public void RenderError(string message) {
            RefreshError = message ?? "";
            if (LastData == null) RenderData(PlaceholderData(false, message));
            UpdateStatus(false);
        }

        // ---------------------------------------------------------------- refresh lifecycle
        public void StartRefresh() {
            if (RefreshPending || Smoke) return;
            ReadRevision = ProfileRevision;
            ReadId = Guid.NewGuid().ToString("N");
            LastProgressRevision = -1;
            RefreshPending = true;
            RefreshStarted = DateTimeOffset.Now;
            RefreshButton.Enabled = false;
            RefreshButton.Text = "更新中";
            if (LastData == null) ShowLoading();
            UpdateStatus(false);
            try {
                if (File.Exists(CachePath)) File.Delete(CachePath);
                var info = new ProcessStartInfo(Client.Self, "--read-usage " + Client.Quote(CachePath)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = DataDir };
                info.EnvironmentVariables["CODEX_USAGE_READ_ID"] = ReadId;
                info.EnvironmentVariables["CODEX_USAGE_DATA_DIR"] = DataDir;
                Worker = Process.Start(info);
                if (Worker == null) throw new IOException("无法启动后台读取进程。");
                PollTimer.Start();
                Log("Refresh worker PID: " + Worker.Id);
            } catch (Exception ex) {
                if (Worker != null) { Worker.Dispose(); Worker = null; }
                RefreshPending = false;
                RefreshButton.Enabled = true;
                RefreshButton.Text = "刷新";
                Log("Refresh start error: " + ex.Message);
                RenderError(ex.Message);
            }
        }

        void EndWorker() {
            if (Worker != null) { Worker.Dispose(); Worker = null; }
            PollTimer.Stop();
            RefreshPending = false;
            RefreshButton.Enabled = true;
            RefreshButton.Text = "刷新";
        }

        object ReadPayload() { return J.Parse(File.ReadAllText(CachePath, Encoding.UTF8)); }

        public void FinishRefreshIfReady() {
            if (!RefreshPending || Worker == null) return;
            if (!Worker.HasExited) {
                ReadRefreshProgress();
                if ((DateTimeOffset.Now - RefreshStarted).TotalSeconds < 150) return;
                try { Worker.Kill(); } catch (Exception) { }
                EndWorker();
                RenderError("读取超时，请重试。");
                return;
            }
            EndWorker();
            try {
                if (!File.Exists(CachePath)) throw new IOException("后台未返回数据，请重试或查看日志。");
                object payload = ReadPayload();
                if (J.Text(J.Get(payload, "readId")) != ReadId) throw new InvalidDataException("读取结果已过期，请重试。");
                if (!J.Bool(J.Get(payload, "ok"))) throw new InvalidOperationException(J.Text(J.Get(payload, "error")));
                object data = J.Get(payload, "data");
                if (J.Arr(J.Get(data, "profiles")).Length == 0) throw new InvalidDataException("未配置账号，请检查 profiles.json。");
                if (ReadRevision != ProfileRevision) { StartRefresh(); return; }
                AcceptData(data, true);
                Log("Refresh result rendered.");
            } catch (Exception ex) {
                Log("Refresh error: " + ex.Message);
                RenderError(ex.Message);
            }
        }

        public void AcceptData(object data, bool final) {
            LastData = Model.Merge(LastData, data);
            RefreshError = "";
            UpdateUsageHistory(data);
            if (final) { ProcessQuotaNotifications(data); ProcessForecastNotifications(data); }
            try { RemoteWorker.WritePending(LastData, RemoteSettings, Remote == null ? Path.Combine(DataDir, "mobile-usage-pending.json") : Remote.MobilePendingPath); }
            catch (Exception ex) { Log("Mobile snapshot queue warning: " + ex.Message); }
            RenderData(LastData);
        }

        public void ReadRefreshProgress() {
            if (ReadRevision != ProfileRevision || !File.Exists(CachePath)) return;
            try {
                object payload = ReadPayload();
                if (ReadId.Length > 0 && J.Text(J.Get(payload, "readId")) != ReadId) return;
                double? revision = J.Num(J.Get(payload, "revision"));
                if (!J.Bool(J.Get(payload, "ok")) || !revision.HasValue || revision.Value <= LastProgressRevision) return;
                object data = J.Get(payload, "data");
                if (J.Arr(J.Get(data, "profiles")).Length == 0) return;
                LastProgressRevision = (int)revision.Value;
                AcceptData(data, false);
            } catch (Exception ex) { Log("Partial refresh warning: " + ex.Message); }
        }

        // ---------------------------------------------------------------- notifications
        void Balloon(string title, string text, ToolTipIcon icon) {
            if (Smoke || NotifyIcon == null) return;
            NotifyIcon.ShowBalloonTip(8000, title, text, icon);
        }

        public void ProcessQuotaNotifications(object data) {
            try {
                var alerts = Behavior.Alerts(data, AlertState, Settings.NotificationThresholds, Settings.NotificationsEnabled, AlertBaselinePending, DateTimeOffset.Now);
                AlertBaselinePending = false;
                if (Smoke) return;
                // Persist the deduplication ledger before requesting a Windows notification.
                J.AtomicWrite(AlertStatePath, Behavior.AlertStateToJson(AlertState), false);
                if (alerts.Count > 0) Balloon("低额度提醒", string.Join("\r\n", alerts.Select(a => a.Label + " · " + a.Title + "剩余 " + Model.Percent(a.Remaining))), ToolTipIcon.Warning);
            } catch (Exception ex) { Log("Notification warning: " + ex.Message); }
        }

        // Early warning: 5h quota about to run out within 30 minutes, weekly within 24 hours.
        public void ProcessForecastNotifications(object data) {
            if (!Settings.NotificationsEnabled) return;
            var lines = new List<string>();
            foreach (object profile in J.Arr(J.Get(data, "profiles"))) {
                if (profile == null || Model.Stale(profile)) continue;
                foreach (string kind in new[] { "fiveHour", "weekly" }) {
                    Forecast forecast = ProfileForecast(profile, kind);
                    if (forecast == null || forecast.State != "exhaust" || forecast.MinutesLeft > (kind == "fiveHour" ? 30 : 24 * 60)) continue;
                    string key = J.Text(J.Get(profile, "id")) + "|" + kind + "|" + J.Text(J.Get(profile, kind, "resetsAt"));
                    if (!forecastAlerted.Add(key)) continue;
                    lines.Add(AccountName(J.Text(J.Get(profile, "id"))) + " · " + (kind == "fiveHour" ? "5 小时额度" : "每周额度") + "按当前速度" + forecast.Text);
                }
            }
            if (lines.Count > 0) Balloon("额度消耗预警", string.Join("\r\n", lines), ToolTipIcon.Warning);
        }

        // ---------------------------------------------------------------- remote notifications (UI side)
        public string RemoteSettingsPath { get { return Path.Combine(DataDir, "remote-notifications.json"); } }
        string RemoteNotifiedPath { get { return Path.Combine(DataDir, "remote-notified.json"); } }

        public void InitializeRemote() {
            if (Smoke) { RemoteSettings = new RemoteSettings { DeviceName = "测试电脑" }; return; }
            object raw = null;
            try { raw = J.ReadFile(RemoteSettingsPath); } catch (Exception ex) { Log("Remote settings load warning: " + ex.Message); }
            RemoteSettings = RemoteSettings.FromObject(raw);
            try { RemoteSettings.Save(RemoteSettingsPath); } catch (Exception ex) { Log("Remote settings save warning: " + ex.Message); }
            try { foreach (object id in J.Arr(J.ReadFile(RemoteNotifiedPath))) if (J.Text(id).Trim().Length > 0) remoteNotified.Add(J.Text(id)); }
            catch (Exception ex) { Log("Remote notification ledger warning: " + ex.Message); }
            Remote = new RemoteWorker(DataDir);
            EnsureRemoteWorker();
        }

        public void EnsureRemoteWorker() {
            if (Smoke || Exiting || Remote == null || RemoteSettings == null || !RemoteSettings.Active) return;
            if (!Remote.Running) { Remote.Start(); Log("Remote worker thread started."); }
        }

        public void RestartRemoteWorker() { if (Remote != null) { Remote.Stop(); EnsureRemoteWorker(); } }

        public string RemoteStatusText(bool mobile) {
            if (RemoteSettings == null || !(mobile ? RemoteSettings.UsageSyncEnabled : RemoteSettings.Enabled)) return "未启用";
            if (string.IsNullOrWhiteSpace(RemoteSettings.PairKey)) return "需要配对密钥";
            try {
                string path = mobile ? Path.Combine(DataDir, "mobile-usage-status.json") : Path.Combine(DataDir, "remote-worker-status.json");
                object status = J.ReadFile(path);
                if (status != null) return J.Text(J.Get(status, "message"));
            } catch (Exception) { }
            return mobile ? "正在启动同步…" : "正在启动监听…";
        }

        void ProcessRemoteInbox() {
            string inbox = Path.Combine(DataDir, "remote-inbox.jsonl");
            if (Smoke || !File.Exists(inbox)) return;
            try {
                bool changed = false;
                foreach (string line in File.ReadAllLines(inbox, Encoding.UTF8).Reverse().Take(200).Reverse()) {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    object evt;
                    try { evt = J.Parse(line); } catch (ArgumentException) { continue; } catch (InvalidOperationException) { continue; }
                    string id = J.Text(J.Get(evt, "id"));
                    if (id.Length == 0 || !remoteNotified.Add(id)) continue;
                    changed = true;
                    string device = J.Text(J.Get(evt, "deviceName")).Trim().Length > 0 ? J.Text(J.Get(evt, "deviceName")) : "另一台电脑";
                    string project = J.Text(J.Get(evt, "project")).Trim().Length > 0 ? J.Text(J.Get(evt, "project")) : "Codex 会话";
                    bool failed = J.Text(J.Get(evt, "status")) == "error";
                    string body = project + (J.Text(J.Get(evt, "summary")).Trim().Length > 0 ? Environment.NewLine + J.Text(J.Get(evt, "summary")) : "");
                    NotifyIcon.ShowBalloonTip(10000, (failed ? "Codex 施工需要检查 · " : "Codex 施工完成 · ") + device, body, failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
                    Log("Remote completion shown: " + id);
                }
                if (changed) {
                    var ids = remoteNotified.ToList();
                    if (ids.Count > 300) ids = ids.Skip(ids.Count - 300).ToList();
                    File.WriteAllText(RemoteNotifiedPath, J.Json(ids.ToArray()), new UTF8Encoding(false));
                }
            } catch (Exception ex) { Log("Remote inbox warning: " + ex.Message); }
        }

        public void UpdateRemoteNotifications() {
            if (Smoke || Exiting) return;
            ProcessRemoteInbox();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if ((now - remoteLastEnsure).TotalSeconds < 5) return;
            remoteLastEnsure = now;
            EnsureRemoteWorker();
        }
    }
}
