// Pure application rules shared by the native UI, the background worker and the tests.
// No WinForms dependency: PowerShell 7 can compile this file for offline tests.
// C# 5 only (Windows .NET Framework csc.exe).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexUsageDesktop {
    // ------------------------------------------------------------------ JSON helpers
    public static class J {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }; }
        public static object Parse(string text) { return Serializer().DeserializeObject(text ?? ""); }
        public static string Json(object value) { return Serializer().Serialize(value); }
        public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
        public static object Get(object value, params string[] path) {
            object current = value;
            foreach (string name in path) {
                var map = current as Dictionary<string, object>;
                if (map == null) return null;
                object next;
                if (!map.TryGetValue(name, out next)) return null;
                current = next;
            }
            return current;
        }
        public static bool Has(object value, string name) { var map = value as Dictionary<string, object>; return map != null && map.ContainsKey(name); }
        public static string Text(object value) { return value == null ? "" : Convert.ToString(value, Inv); }
        public static double? Num(object value) {
            if (value == null || value is bool) return null;
            if (value is string && ((string)value).Trim().Length == 0) return null;
            double result;
            if (!double.TryParse(Text(value), NumberStyles.Float, Inv, out result) || double.IsNaN(result) || double.IsInfinity(result)) return null;
            return result;
        }
        public static bool Bool(object value) { return value is bool && (bool)value; }
        public static object[] Arr(object value) {
            if (value == null) return new object[0];
            var array = value as object[];
            if (array != null) return array;
            if (value is string || value is Dictionary<string, object>) return new[] { value };
            var list = value as IEnumerable;
            if (list != null) return list.Cast<object>().ToArray();
            return new[] { value };
        }
        public static Dictionary<string, object> Copy(object value) {
            var map = value as Dictionary<string, object>;
            return map == null ? new Dictionary<string, object>() : new Dictionary<string, object>(map);
        }
        public static DateTimeOffset? Time(object value) {
            if (value == null) return null;
            if (value is DateTimeOffset) return (DateTimeOffset)value;
            if (value is DateTime) return new DateTimeOffset((DateTime)value);
            string text = Text(value);
            if (text.Length == 0) return null;
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(text, Inv, DateTimeStyles.AssumeUniversal, out parsed) ? (DateTimeOffset?)parsed : null;
        }
        public static void AtomicWrite(string path, string text, bool keepBackup) {
            string full = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(full);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(full)) File.Replace(temporary, full, keepBackup ? full + ".bak" : null);
                else File.Move(temporary, full);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static object ReadFile(string path) {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path, Encoding.UTF8);
            return text.Trim().Length == 0 ? null : Parse(text);
        }
    }

    // ------------------------------------------------------------------ quota presentation
    public sealed class QuotaPair {
        public double? FiveHour;
        public double? LongTerm;
        public string Source = "未提供";
        public object LongTermResetsAt;
    }

    public sealed class UsageTrend {
        public double[] Values;
        public string[] Labels;
        public int Highlight;
    }

    public static class Model {
        public static readonly string[] AccountIds = { "personal", "work", "claude" };

        public static bool MeaningfulLimit(object limit) {
            if (limit == null) return false;
            object value = J.Get(limit, "limit");
            if (value == null) return true;
            double? number = J.Num(value);
            return number.HasValue && number.Value > 0;
        }

        public static QuotaPair Pair(object profile) {
            var pair = new QuotaPair();
            if (profile == null || !J.Bool(J.Get(profile, "ok"))) return pair;
            pair.FiveHour = J.Num(J.Get(profile, "fiveHour", "remainingPercent"));
            double? weekly = J.Num(J.Get(profile, "weekly", "remainingPercent"));
            if (weekly.HasValue) { pair.LongTerm = weekly; pair.LongTermResetsAt = J.Get(profile, "weekly", "resetsAt"); pair.Source = "每周额度"; }
            object limit = J.Get(profile, "individualLimit");
            double? monthly = J.Num(J.Get(limit, "remainingPercent"));
            if (MeaningfulLimit(limit) && monthly.HasValue && (!pair.LongTerm.HasValue || monthly.Value < pair.LongTerm.Value)) {
                pair.LongTerm = monthly; pair.LongTermResetsAt = J.Get(limit, "resetsAt"); pair.Source = "工作空间 / 月度额度";
            }
            return pair;
        }

        public static object DisplayProfile(object data, string id) {
            foreach (object profile in J.Arr(J.Get(data, "profiles")))
                if (profile != null && J.Text(J.Get(profile, "id")) == id) return profile;
            return null;
        }

        public static bool Stale(object profile, DateTimeOffset now) {
            if (profile == null || !J.Bool(J.Get(profile, "ok")) || J.Bool(J.Get(profile, "stale"))) return true;
            DateTimeOffset? fetched = J.Time(J.Get(profile, "fetchedAt"));
            if (!fetched.HasValue) return true;
            return (now - fetched.Value).TotalMinutes >= 10;
        }
        public static bool Stale(object profile) { return Stale(profile, DateTimeOffset.Now); }

        // Match by account id, never list position; retain the last success per account.
        public static Dictionary<string, object> Merge(object previous, object incoming) {
            var profiles = new List<object>();
            foreach (object profile in J.Arr(J.Get(incoming, "profiles"))) {
                if (profile == null) continue;
                object old = DisplayProfile(previous, J.Text(J.Get(profile, "id")));
                object source = profile;
                bool ok = J.Bool(J.Get(profile, "ok")), pending = J.Bool(J.Get(profile, "pending"));
                bool oldOk = old != null && J.Bool(J.Get(old, "ok"));
                bool stale = !ok;
                if (!ok && oldOk) source = old;
                if (pending && oldOk) stale = Stale(old);
                var copy = J.Copy(source);
                copy["stale"] = stale;
                copy["pending"] = pending;
                copy["refreshError"] = stale ? J.Text(J.Get(profile, "error")) : "";
                profiles.Add(copy);
            }
            return new Dictionary<string, object> { { "profiles", profiles.ToArray() }, { "fetchedAt", J.Get(incoming, "fetchedAt") } };
        }

        public static string Percent(object value) {
            double? number = J.Num(value);
            if (!number.HasValue || number.Value < 0) return "—";
            if (number.Value > 0 && number.Value < 1) return "<1%";
            return Math.Round(Math.Min(100, number.Value), 0, MidpointRounding.AwayFromZero).ToString(J.Inv) + "%";
        }

        public static string ResetText(object iso, DateTimeOffset now) {
            DateTimeOffset? reset = J.Time(iso);
            if (!reset.HasValue) return "未提供重置时间";
            TimeSpan span = reset.Value - now;
            if (span.TotalSeconds <= 0) return "等待额度更新";
            if (span.TotalDays >= 1) return string.Format(J.Inv, "{0}天 {1}小时后重置", Math.Floor(span.TotalDays), span.Hours);
            if (span.TotalHours >= 1) return string.Format(J.Inv, "{0}小时 {1}分钟后重置", Math.Floor(span.TotalHours), span.Minutes);
            return string.Format(J.Inv, "{0}分钟后重置", Math.Max(1, Math.Ceiling(span.TotalMinutes)));
        }
        public static string ResetText(object iso) { return ResetText(iso, DateTimeOffset.Now); }

        public static string LocalTimestamp(object iso) {
            DateTimeOffset? value = J.Time(iso);
            return value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", J.Inv) : "";
        }

        public static string UpdateText(object profile, DateTimeOffset now) {
            DateTimeOffset? fetched = profile != null && J.Bool(J.Get(profile, "ok")) ? J.Time(J.Get(profile, "fetchedAt")) : null;
            string stamp = fetched.HasValue ? fetched.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", J.Inv) : "尚无成功更新";
            string suffix = profile != null && J.Bool(J.Get(profile, "pending")) ? " · 正在读取" : (Stale(profile, now) ? " · 数据未更新" : "");
            return "最近更新：" + stamp + suffix;
        }

        public static string CompactRecovery(object fiveHour, bool stale, DateTimeOffset now) {
            double? remaining = J.Num(J.Get(fiveHour, "remainingPercent"));
            if (!remaining.HasValue || remaining.Value != 0) return "5h / 长周期 · 剩余";
            if (stale) return "5h 恢复时间待确认";
            DateTimeOffset? reset = J.Time(J.Get(fiveHour, "resetsAt"));
            if (!reset.HasValue) return "5h 恢复时间未知";
            if (reset.Value <= now) return "5h 等待刷新确认";
            double minutes = Math.Max(1, Math.Ceiling((reset.Value - now).TotalMinutes));
            if (minutes < 60) return string.Format(J.Inv, "约 {0} 分钟后恢复", minutes);
            return string.Format(J.Inv, "约 {0}小时{1}分后恢复", Math.Floor(minutes / 60), minutes % 60);
        }

        public static bool Enabled(object profile) { return profile != null && (!J.Has(profile, "enabled") || J.Bool(J.Get(profile, "enabled"))); }

        public static string RecoveryKey(object profile, DateTimeOffset now) {
            if (Stale(profile, now)) return null;
            double? remaining = J.Num(J.Get(profile, "fiveHour", "remainingPercent"));
            if (!remaining.HasValue || remaining.Value != 0) return null;
            DateTimeOffset? reset = J.Time(J.Get(profile, "fiveHour", "resetsAt"));
            if (reset.HasValue && reset.Value <= now) return J.Text(J.Get(profile, "id")) + "|" + reset.Value.ToUniversalTime().ToString("o", J.Inv);
            return null;
        }

        public static string DefaultAccountName(string id) {
            if (id == "personal") return "个人";
            if (id == "claude") return "Claude";
            return "工作";
        }

        public static bool IsClaude(object profile) {
            return profile != null && (J.Text(J.Get(profile, "provider")) == "claude" || J.Text(J.Get(profile, "id")) == "claude");
        }

        // Adds the Claude slot to configurations created before v1.0. Returns true when changed.
        public static bool AddClaudeProfileIfMissing(Dictionary<string, object> config, string userProfile, string claudeConfigDir) {
            var profiles = J.Arr(J.Get(config, "profiles")).ToList();
            foreach (object profile in profiles) if (J.Text(J.Get(profile, "id")) == "claude") return false;
            string home = string.IsNullOrWhiteSpace(claudeConfigDir) ? Path.Combine(userProfile ?? "", ".claude") : claudeConfigDir;
            bool detected = File.Exists(Path.Combine(home, ".credentials.json")) || Directory.Exists(Path.Combine(home, "projects"));
            profiles.Add(new Dictionary<string, object> {
                { "id", "claude" }, { "label", "Claude" }, { "provider", "claude" },
                { "claudeHome", string.IsNullOrWhiteSpace(claudeConfigDir) ? "~/.claude" : claudeConfigDir }, { "enabled", detected }
            });
            config["profiles"] = profiles.ToArray();
            return true;
        }

        public static int MonitorWidth(int count) { return count <= 1 ? 220 : 360 + 150 * (count - 2); }
        public static int ExpandedHeight(int count) { return 74 + 34 * Math.Max(1, count); }

        public static string Tokens(object value) {
            double? n = J.Num(value);
            if (!n.HasValue || n.Value < 0) return "—";
            double v = n.Value;
            if (v >= 1e9) return (v / 1e9).ToString("0.0", J.Inv) + "B";
            if (v >= 1e6) return (v / 1e6).ToString("0.0", J.Inv) + "M";
            if (v >= 1e3) return (v / 1e3).ToString("0.0", J.Inv) + "K";
            return v.ToString("0", J.Inv);
        }

        public static string Usd(object value) {
            double? n = J.Num(value);
            if (!n.HasValue || n.Value < 0) return "—";
            if (n.Value > 0 && n.Value < 0.01) return "<$0.01";
            if (n.Value >= 1000) return "$" + n.Value.ToString("N0", J.Inv);
            return "$" + n.Value.ToString("0.00", J.Inv);
        }

        // No local session in the seven-day window. Claude sessions run in the Claude
        // app or on the web never reach ~/.claude/projects, so "0 tokens" would mislead.
        public static bool LocalUsageEmpty(object usage) {
            return usage == null || J.Get(usage, "today") == null || (J.Num(J.Get(usage, "week", "totalTokens")) ?? 0) <= 0;
        }
        public static string LocalUsageSummary(object usage, string provider) {
            if (LocalUsageEmpty(usage)) return provider == "claude" ? "无本机记录 · 云端会话不计入，以额度为准" : "近 7 天无本机会话记录";
            return LocalUsageSummary(usage);
        }
        public static string LocalUsageSummary(object usage) {
            if (usage == null || J.Get(usage, "today") == null) return "本机用量：暂无记录";
            return "今日 " + Tokens(J.Get(usage, "today", "totalTokens")) + " tokens · ≈" + Usd(J.Get(usage, "today", "costUsd")) +
                "　近 7 天 " + Tokens(J.Get(usage, "week", "totalTokens")) + " · ≈" + Usd(J.Get(usage, "week", "costUsd"));
        }

        public static string LocalUsageDetail(object usage, string provider) {
            string tool = provider == "codex" ? "Codex" : "Claude Code";
            if (LocalUsageEmpty(usage)) return "尚未在本机找到最近 7 天的 " + tool + " 会话记录。" + (provider == "claude" ? "\r\n在 Claude 应用或网页中运行的云端会话不会写入本机日志，实际用量以上方 5 小时 / 每周额度为准。" : "");
            object t = J.Get(usage, "today");
            var lines = new List<string> {
                "今日：输入 " + Tokens(J.Get(t, "inputTokens")) + " · 输出 " + Tokens(J.Get(t, "outputTokens")) + " · 缓存写入 " + Tokens(J.Get(t, "cacheWriteTokens")) + " · 缓存读取 " + Tokens(J.Get(t, "cacheReadTokens")),
                "今日请求 " + ((long)(J.Num(J.Get(t, "messages")) ?? 0)).ToString(J.Inv) + " 次" + (J.Text(J.Get(usage, "topModelToday")).Length > 0 ? " · 主要模型 " + J.Text(J.Get(usage, "topModelToday")) : "")
            };
            foreach (object day in J.Arr(J.Get(usage, "days"))) {
                if (day == null) continue;
                lines.Add(J.Text(J.Get(day, "date")) + "　" + Tokens(J.Get(day, "totalTokens")) + " tokens · ≈" + Usd(J.Get(day, "costUsd")));
            }
            lines.Add(provider == "codex" ? "费用按 OpenAI API 公开价格估算；ChatGPT 订阅不按 token 计费。" : "费用按 Anthropic API 公开价格估算；订阅用户不按 token 计费。");
            return string.Join("\r\n", lines);
        }

        public static UsageTrend Trend(object usage, DateTime today) {
            string[] names = { "日", "一", "二", "三", "四", "五", "六" };
            var byDate = new Dictionary<string, object>();
            foreach (object day in J.Arr(J.Get(usage, "days"))) if (day != null && J.Text(J.Get(day, "date")).Length > 0) byDate[J.Text(J.Get(day, "date"))] = day;
            var trend = new UsageTrend { Values = new double[7], Labels = new string[7], Highlight = 6 };
            for (int i = 6; i >= 0; i--) {
                DateTime date = today.AddDays(-i);
                string key = date.ToString("yyyy-MM-dd", J.Inv);
                object day;
                trend.Values[6 - i] = byDate.TryGetValue(key, out day) ? (J.Num(J.Get(day, "totalTokens")) ?? 0) : 0;
                trend.Labels[6 - i] = i == 0 ? "今" : names[(int)date.DayOfWeek];
            }
            return trend;
        }
    }

    // ------------------------------------------------------------------ burn-rate forecast
    public sealed class UsageSample { public double T; public double R; public double? Reset; }

    public sealed class Forecast {
        public string State = "none"; // none | idle | safe | exhaust | empty
        public DateTimeOffset? ExhaustAt;
        public double? RatePerHour;
        public double? MinutesLeft;
        public string Text = "";
    }

    public static class Forecasts {
        public static double? Unix(object value) {
            DateTimeOffset? time = J.Time(value);
            return time.HasValue ? (double?)time.Value.ToUnixTimeSeconds() : null;
        }

        // Samples come only from fresh successful reads; cached reads repeat their timestamp.
        public static void AddSamples(Dictionary<string, List<UsageSample>> history, object data, DateTimeOffset now) {
            if (history == null || data == null) return;
            double nowT = now.ToUnixTimeSeconds();
            foreach (object profile in J.Arr(J.Get(data, "profiles"))) {
                if (profile == null || !J.Bool(J.Get(profile, "ok")) || J.Bool(J.Get(profile, "pending"))) continue;
                double? t = Unix(J.Get(profile, "fetchedAt"));
                if (!t.HasValue || t.Value > nowT + 60) continue;
                foreach (string kind in new[] { "fiveHour", "weekly" }) {
                    object window = J.Get(profile, kind);
                    double? remaining = J.Num(J.Get(window, "remainingPercent"));
                    if (!remaining.HasValue || remaining.Value < 0 || remaining.Value > 100) continue;
                    string key = J.Text(J.Get(profile, "id")) + "|" + kind;
                    List<UsageSample> list;
                    if (!history.TryGetValue(key, out list)) { list = new List<UsageSample>(); history[key] = list; }
                    if (list.Count > 0 && t.Value < list[list.Count - 1].T + 50) continue;
                    list.Add(new UsageSample { T = t.Value, R = remaining.Value, Reset = Unix(J.Get(window, "resetsAt")) });
                    double horizon = nowT - 13 * 3600;
                    while (list.Count > 0 && (list[0].T < horizon || list.Count > 900)) list.RemoveAt(0);
                }
            }
        }

        public static string FormatTime(DateTimeOffset at, DateTimeOffset now) {
            DateTime local = at.ToLocalTime().DateTime, today = now.ToLocalTime().Date;
            if (local.Date == today) return local.ToString("HH:mm", J.Inv);
            if (local.Date == today.AddDays(1)) return "明天 " + local.ToString("HH:mm", J.Inv);
            return local.ToString("MM-dd HH:mm", J.Inv);
        }

        // Least-squares slope over recent samples in the current reset cycle.
        public static Forecast Burn(Dictionary<string, List<UsageSample>> history, string id, string kind, object window, DateTimeOffset now) {
            var none = new Forecast();
            double? current = J.Num(J.Get(window, "remainingPercent"));
            if (history == null || window == null || !current.HasValue) return none;
            if (current.Value <= 0) { none.State = "empty"; return none; }
            List<UsageSample> list;
            if (!history.TryGetValue(id + "|" + kind, out list) || list.Count < 2) return none;
            double nowT = now.ToUnixTimeSeconds();
            double lookback = kind == "fiveHour" ? 3600 : 12 * 3600, minSpan = kind == "fiveHour" ? 600 : 2 * 3600;
            double? reset = Unix(J.Get(window, "resetsAt"));
            var points = new List<UsageSample>();
            for (int i = list.Count - 1; i >= 0; i--) {
                UsageSample s = list[i];
                if (s.T < nowT - lookback) break;
                if (reset.HasValue && s.Reset.HasValue && Math.Abs(s.Reset.Value - reset.Value) > 600) break;
                if (points.Count > 0 && s.R < points[points.Count - 1].R - 0.5) break;
                points.Add(s);
            }
            if (points.Count < 2) return none;
            double first = points[points.Count - 1].T;
            if (points[0].T - first < minSpan) return none;
            double n = points.Count, sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach (UsageSample p in points) { double x = (p.T - first) / 3600.0; sx += x; sy += p.R; sxx += x * x; sxy += x * p.R; }
            double den = n * sxx - sx * sx;
            if (Math.Abs(den) < 1e-9) return none;
            double rate = -((n * sxy - sx * sy) / den);
            if (rate < 0.2) return new Forecast { State = "idle", RatePerHour = Math.Max(0, rate), Text = "近期消耗很少" };
            double minutes = current.Value / rate * 60;
            DateTimeOffset at = now.AddMinutes(minutes);
            if (reset.HasValue && at.ToUnixTimeSeconds() >= reset.Value)
                return new Forecast { State = "safe", ExhaustAt = at, RatePerHour = rate, MinutesLeft = minutes, Text = "按当前速度可用到重置" };
            return new Forecast { State = "exhaust", ExhaustAt = at, RatePerHour = rate, MinutesLeft = minutes, Text = "预计 " + FormatTime(at, now) + " 用完" };
        }

        public static string Detail(Forecast forecast) {
            if (forecast == null || forecast.State == "none" || forecast.State == "empty") return "消耗预测：数据不足（需要约 10 分钟的连续读数）";
            string rate = (forecast.RatePerHour ?? 0).ToString("0.#", J.Inv) + "%/小时";
            if (forecast.State == "idle") return "消耗预测：近期几乎未消耗（" + rate + "）";
            if (forecast.State == "safe") return "消耗预测：约 " + rate + "，按当前速度可用到重置";
            return "消耗预测：约 " + rate + "，" + forecast.Text;
        }

        public static string ToJson(Dictionary<string, List<UsageSample>> history) {
            var plain = new Dictionary<string, object>();
            foreach (var pair in history)
                plain[pair.Key] = pair.Value.Select(s => (object)new Dictionary<string, object> { { "t", s.T }, { "r", s.R }, { "reset", s.Reset } }).ToArray();
            return J.Json(plain);
        }

        public static Dictionary<string, List<UsageSample>> FromJson(string json) {
            var history = new Dictionary<string, List<UsageSample>>();
            if (string.IsNullOrWhiteSpace(json)) return history;
            var parsed = J.Map(J.Parse(json));
            if (parsed == null) return history;
            foreach (var pair in parsed) {
                var list = new List<UsageSample>();
                foreach (object s in J.Arr(pair.Value)) {
                    double? t = J.Num(J.Get(s, "t")), r = J.Num(J.Get(s, "r"));
                    if (t.HasValue && r.HasValue) list.Add(new UsageSample { T = t.Value, R = r.Value, Reset = J.Num(J.Get(s, "reset")) });
                }
                history[pair.Key] = list;
            }
            return history;
        }
    }

    // ------------------------------------------------------------------ interaction + alert rules
    public sealed class SnapResult { public int X, Y; public string Horizontal = "none", Vertical = "none"; }

    public sealed class AlertEntry { public double? Value; public string Cycle = ""; public List<int> Fired = new List<int>(); public string SampleAt = ""; }

    public sealed class QuotaAlert { public string Id, Label, Window, Title; public double Remaining; public int Threshold; }

    public static class Behavior {
        public static double FittedScale(double scale, double dpiScale, double width, double height, int areaWidth, int areaHeight) {
            if (areaWidth <= 0 || areaHeight <= 0) return scale;
            if (scale <= 0 || dpiScale <= 0 || width <= 0 || height <= 0) throw new ArgumentException("Invalid monitor geometry.");
            return Math.Min(scale, Math.Min(areaWidth / (width * dpiScale), areaHeight / (height * dpiScale)));
        }

        public static SnapResult Snap(int x, int y, int width, int height, int areaLeft, int areaTop, int areaRight, int areaBottom, int distance, bool enabled) {
            int right = Math.Max(areaLeft, areaRight - width), bottom = Math.Max(areaTop, areaBottom - height);
            var result = new SnapResult { X = Math.Max(areaLeft, Math.Min(right, x)), Y = Math.Max(areaTop, Math.Min(bottom, y)) };
            if (!enabled) return result;
            if (Math.Min(result.X - areaLeft, right - result.X) <= distance) {
                if (result.X - areaLeft <= right - result.X) { result.X = areaLeft; result.Horizontal = "left"; } else { result.X = right; result.Horizontal = "right"; }
            }
            if (Math.Min(result.Y - areaTop, bottom - result.Y) <= distance) {
                if (result.Y - areaTop <= bottom - result.Y) { result.Y = areaTop; result.Vertical = "top"; } else { result.Y = bottom; result.Vertical = "bottom"; }
            }
            return result;
        }

        // Only fresh successful reads that cross a threshold notify; first samples, new cycles
        // and gaps of ten minutes or more establish a baseline. Each threshold fires once per cycle.
        public static List<QuotaAlert> Alerts(object data, Dictionary<string, AlertEntry> state, int[] thresholds, bool enabled, bool baseline, DateTimeOffset now) {
            var alerts = new List<QuotaAlert>();
            int[] levels = (thresholds ?? new int[0]).Where(l => l > 0 && l < 100).Distinct().OrderByDescending(l => l).ToArray();
            foreach (object profile in J.Arr(J.Get(data, "profiles"))) {
                if (profile == null || Model.Stale(profile, now)) continue;
                DateTimeOffset? sampleAt = J.Time(J.Get(profile, "fetchedAt"));
                if (!sampleAt.HasValue || sampleAt.Value > now.AddMinutes(1)) continue;
                foreach (string kind in new[] { "fiveHour", "weekly", "individualLimit" }) {
                    object window = J.Get(profile, kind);
                    if (kind == "individualLimit" && !Model.MeaningfulLimit(window)) continue;
                    double? value = J.Num(J.Get(window, "remainingPercent"));
                    if (!value.HasValue || value.Value < 0 || value.Value > 100) continue;
                    string cycle = "";
                    if (J.Text(J.Get(window, "resetsAt")).Length > 0) {
                        DateTimeOffset? reset = J.Time(J.Get(window, "resetsAt"));
                        if (!reset.HasValue || reset.Value <= now) continue;
                        cycle = reset.Value.ToUniversalTime().ToString("o", J.Inv);
                    }
                    string key = J.Text(J.Get(profile, "id")) + "|" + J.Text(J.Get(profile, "accountId")) + "|" + kind;
                    AlertEntry old;
                    state.TryGetValue(key, out old);
                    DateTimeOffset? oldAt = old == null ? null : J.Time(old.SampleAt);
                    if (old != null && oldAt.HasValue && sampleAt.Value <= oldAt.Value) continue;
                    string oldCycle = "";
                    if (old != null && old.Cycle.Length > 0) { DateTimeOffset? c = J.Time(old.Cycle); if (c.HasValue) oldCycle = c.Value.ToUniversalTime().ToString("o", J.Inv); else old = null; }
                    bool newCycle = old == null || oldCycle != cycle;
                    var fired = newCycle ? new List<int>() : new List<int>(old.Fired);
                    if (cycle.Length == 0 && levels.Length > 0 && value.Value > levels[0]) fired.Clear();
                    var crossed = new List<int>();
                    bool recent = old != null && old.Value.HasValue && oldAt.HasValue && (sampleAt.Value - oldAt.Value).TotalMinutes < 10;
                    if (enabled && !baseline && !newCycle && recent) {
                        foreach (int level in levels) {
                            if (old.Value.Value > level && value.Value <= level && !fired.Contains(level)) { crossed.Add(level); fired.Add(level); }
                        }
                    }
                    state[key] = new AlertEntry { Value = value, Cycle = cycle, Fired = fired, SampleAt = sampleAt.Value.ToString("o", J.Inv) };
                    if (crossed.Count > 0) {
                        string title = kind == "fiveHour" ? "5 小时" : kind == "weekly" ? "每周" : "工作空间 / 月度";
                        alerts.Add(new QuotaAlert { Id = J.Text(J.Get(profile, "id")), Label = J.Text(J.Get(profile, "label")), Window = kind, Title = title, Remaining = value.Value, Threshold = crossed.Min() });
                    }
                }
            }
            return alerts;
        }

        public static string AlertStateToJson(Dictionary<string, AlertEntry> state) {
            var plain = new Dictionary<string, object>();
            foreach (var pair in state) plain[pair.Key] = new Dictionary<string, object> { { "value", pair.Value.Value }, { "cycle", pair.Value.Cycle }, { "fired", pair.Value.Fired.ToArray() }, { "sampleAt", pair.Value.SampleAt } };
            return J.Json(plain);
        }

        // Keep the fired-threshold ledger, but establish a fresh baseline after a restart.
        public static Dictionary<string, AlertEntry> AlertStateFromJson(string json) {
            var state = new Dictionary<string, AlertEntry>();
            var parsed = string.IsNullOrWhiteSpace(json) ? null : J.Map(J.Parse(json));
            if (parsed == null) return state;
            foreach (var pair in parsed) {
                var entry = new AlertEntry { Value = null, Cycle = J.Text(J.Get(pair.Value, "cycle")), SampleAt = J.Text(J.Get(pair.Value, "sampleAt")) };
                foreach (object level in J.Arr(J.Get(pair.Value, "fired"))) { double? l = J.Num(level); if (l.HasValue) entry.Fired.Add((int)l.Value); }
                state[pair.Key] = entry;
            }
            return state;
        }
    }

    // ------------------------------------------------------------------ recovery + read-failure notifications
    public sealed class HealthEntry { public double? Last; public DateTimeOffset? LastAt; public DateTimeOffset? FailingSince; public bool FailureNotified; }

    public sealed class HealthAlert { public string Id, Kind, Title, Text; }

    public static class AccountHealth {
        public const int FailureMinutes = 15;

        public static string FailureHint(string error) {
            string e = error ?? "";
            if (e.Contains("登录已过期") || e.Contains("/login")) return "打开 Claude Code 运行一次即可恢复。";
            if (e.Contains("Codex CLI")) return "请在“账号与首次使用”中安装或登录 Codex。";
            if (e.Contains("未检测到 Claude Code")) return "请安装 Claude Code 并登录订阅账号。";
            return "请检查网络或重新登录这个账号。";
        }

        // Raw (unmerged) reader data only. Recovery: a fresh reading after an exhausted one (0%)
        // shows the window has replenished. Failure: an account that keeps failing for 15 minutes.
        // The first sample after start-up only establishes a baseline.
        public static List<HealthAlert> Track(object data, Dictionary<string, HealthEntry> state, Func<string, string> name, bool recovery, bool failures, DateTimeOffset now) {
            var alerts = new List<HealthAlert>();
            foreach (object profile in J.Arr(J.Get(data, "profiles"))) {
                if (profile == null || J.Bool(J.Get(profile, "pending"))) continue;
                string id = J.Text(J.Get(profile, "id"));
                if (id.Length == 0) continue;
                string label = name != null ? name(id) : id;
                HealthEntry account;
                if (!state.TryGetValue(id, out account)) { account = new HealthEntry(); state[id] = account; }
                if (!J.Bool(J.Get(profile, "ok"))) {
                    if (!account.FailingSince.HasValue) account.FailingSince = now;
                    if (failures && !account.FailureNotified && (now - account.FailingSince.Value).TotalMinutes >= FailureMinutes) {
                        account.FailureNotified = true;
                        string error = J.Text(J.Get(profile, "error"));
                        alerts.Add(new HealthAlert { Id = id, Kind = "failure", Title = "额度读取异常", Text = label + " 已连续 " + FailureMinutes + " 分钟无法读取额度。" + FailureHint(error) });
                    }
                    continue;
                }
                bool wasFailing = account.FailureNotified;
                account.FailingSince = null;
                account.FailureNotified = false;
                if (wasFailing && failures) alerts.Add(new HealthAlert { Id = id, Kind = "restored", Title = "额度读取已恢复", Text = label + " 已重新连接，数据已更新。" });
                foreach (string kind in new[] { "fiveHour", "weekly" }) {
                    double? value = J.Num(J.Get(profile, kind, "remainingPercent"));
                    DateTimeOffset? at = J.Time(J.Get(profile, "fetchedAt"));
                    if (!value.HasValue || !at.HasValue) continue;
                    string key = id + "|" + kind;
                    HealthEntry window;
                    if (!state.TryGetValue(key, out window)) { window = new HealthEntry(); state[key] = window; }
                    if (window.LastAt.HasValue && at.Value <= window.LastAt.Value) continue;
                    if (recovery && window.Last.HasValue && window.Last.Value <= 0 && value.Value > 0)
                        alerts.Add(new HealthAlert { Id = id, Kind = "recovered", Title = "额度已恢复", Text = label + " · " + (kind == "fiveHour" ? "5 小时" : "每周") + "额度已恢复，剩余 " + Model.Percent(value) + "。" });
                    window.Last = value;
                    window.LastAt = at;
                }
            }
            return alerts;
        }
    }

    // ------------------------------------------------------------------ remote crypto (wire-compatible with v0.7+)
    public static class RemoteCrypto {
        public static byte[] Sha256(string text) { using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text)); }
        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public static string RemoteTopic(string pairKey) { return "codex-" + Hex(Sha256("codex-remote-topic-v1|" + pairKey)).Substring(0, 48); }
        public static string MobileTopic(string pairKey) { return "codex-usage-" + Hex(Sha256("codex-mobile-usage-topic-v1|" + pairKey)).Substring(0, 48); }

        static bool SameBytes(byte[] a, byte[] b) {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string Protect(string plain, string pairKey) { return Protect(plain, pairKey, null); }

        // AES-256-CBC + HMAC-SHA256 over IV||cipher, base64(IV||cipher||tag). iv is for fixtures only.
        public static string Protect(string plain, string pairKey, byte[] iv) {
            using (var aes = new AesManaged { KeySize = 256, BlockSize = 128, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 }) {
                aes.Key = Sha256("codex-remote-enc-v1|" + pairKey);
                if (iv == null) aes.GenerateIV(); else { if (iv.Length != 16) throw new ArgumentException("AES-CBC test IV must contain 16 bytes."); aes.IV = iv; }
                byte[] plainBytes = Encoding.UTF8.GetBytes(plain), cipher;
                using (var encryptor = aes.CreateEncryptor()) cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                byte[] body = new byte[16 + cipher.Length];
                Array.Copy(aes.IV, 0, body, 0, 16);
                Array.Copy(cipher, 0, body, 16, cipher.Length);
                byte[] tag;
                using (var hmac = new HMACSHA256(Sha256("codex-remote-mac-v1|" + pairKey))) tag = hmac.ComputeHash(body);
                byte[] package = new byte[body.Length + tag.Length];
                Array.Copy(body, 0, package, 0, body.Length);
                Array.Copy(tag, 0, package, body.Length, tag.Length);
                return Convert.ToBase64String(package);
            }
        }

        public static string Unprotect(string cipherText, string pairKey) {
            byte[] package = Convert.FromBase64String(cipherText);
            if (package.Length < 65) throw new CryptographicException("Remote payload is too short.");
            byte[] body = new byte[package.Length - 32], tag = new byte[32];
            Array.Copy(package, 0, body, 0, body.Length);
            Array.Copy(package, body.Length, tag, 0, 32);
            byte[] expected;
            using (var hmac = new HMACSHA256(Sha256("codex-remote-mac-v1|" + pairKey))) expected = hmac.ComputeHash(body);
            if (!SameBytes(tag, expected)) throw new CryptographicException("Remote payload authentication failed.");
            byte[] iv = new byte[16], cipher = new byte[body.Length - 16];
            Array.Copy(body, 0, iv, 0, 16);
            Array.Copy(body, 16, cipher, 0, cipher.Length);
            using (var aes = new AesManaged { KeySize = 256, BlockSize = 128, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 }) {
                aes.Key = Sha256("codex-remote-enc-v1|" + pairKey);
                aes.IV = iv;
                using (var decryptor = aes.CreateDecryptor()) return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(cipher, 0, cipher.Length));
            }
        }

        public static string NewPairKey() {
            byte[] bytes = new byte[18];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string RelayUrl(string configured) {
            string value = string.IsNullOrWhiteSpace(configured) ? "https://ntfy.sh" : configured.Trim();
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) throw new ArgumentException("中继地址无效。");
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new ArgumentException("中继地址必须使用 HTTPS；只有 localhost 可用 HTTP。");
            return value.TrimEnd('/');
        }
    }

    // ------------------------------------------------------------------ mobile snapshot (allowlisted)
    public static class Mobile {
        static object Percent(object value) { double? n = J.Num(value); if (!n.HasValue || n.Value < 0) return null; return Math.Min(100, n.Value); }

        static Dictionary<string, object> Window(object window, string kind, string label) {
            if (window == null) return null;
            object remaining = Percent(J.Get(window, "remainingPercent"));
            if (remaining == null) return null;
            string reset = J.Text(J.Get(window, "resetsAt"));
            return new Dictionary<string, object> { { "kind", kind }, { "label", label }, { "remainingPercent", remaining }, { "resetsAt", reset.Length == 0 ? null : reset } };
        }

        public static Dictionary<string, object> LongTerm(object profile) {
            var weekly = Window(J.Get(profile, "weekly"), "weekly", "Weekly");
            Dictionary<string, object> workspace = null;
            if (Model.MeaningfulLimit(J.Get(profile, "individualLimit"))) workspace = Window(J.Get(profile, "individualLimit"), "workspace", "Workspace / Monthly");
            if (weekly == null) return workspace;
            if (workspace == null) return weekly;
            return (double)workspace["remainingPercent"] < (double)weekly["remainingPercent"] ? workspace : weekly;
        }

        // Aggregate token counts and rounded cost only; never paths, models, prompts or ids.
        public static Dictionary<string, object> LocalUsage(object usage) {
            if (usage == null || J.Get(usage, "today") == null || J.Get(usage, "week") == null) return null;
            var result = new Dictionary<string, object>();
            foreach (string period in new[] { "today", "week" }) {
                double? tokens = J.Num(J.Get(usage, period, "totalTokens")), cost = J.Num(J.Get(usage, period, "costUsd"));
                if (!tokens.HasValue) return null;
                result[period] = new Dictionary<string, object> { { "totalTokens", Math.Floor(Math.Max(0, tokens.Value)) }, { "costUsd", cost.HasValue ? (object)Math.Round(Math.Max(0, cost.Value), 2) : null } };
            }
            return result;
        }

        public static Dictionary<string, object> Snapshot(object data, RemoteSettings settings, DateTimeOffset now) {
            var profiles = new List<object>();
            foreach (object profile in J.Arr(J.Get(data, "profiles"))) {
                if (profile == null || (J.Has(profile, "enabled") && !J.Bool(J.Get(profile, "enabled")))) continue;
                string id = J.Text(J.Get(profile, "id"));
                if (!Model.AccountIds.Contains(id)) continue;
                bool fresh = J.Bool(J.Get(profile, "ok")) && !J.Bool(J.Get(profile, "stale"));
                string fetched = J.Text(J.Get(profile, "fetchedAt"));
                profiles.Add(new Dictionary<string, object> {
                    { "id", id }, { "label", J.Text(J.Get(profile, "label")) }, { "ok", fresh }, { "status", fresh ? "ok" : "error" },
                    { "fetchedAt", fetched.Length == 0 ? null : fetched },
                    { "fiveHour", Window(J.Get(profile, "fiveHour"), "5h", "5 Hours") }, { "longTerm", LongTerm(profile) },
                    { "localUsage", LocalUsage(J.Get(profile, "localUsage")) }
                });
            }
            return new Dictionary<string, object> {
                { "version", 1 }, { "type", "usage_snapshot" }, { "deviceId", settings.DeviceId }, { "deviceName", settings.DeviceName },
                { "publishedAt", now.ToUniversalTime().ToString("o", J.Inv) }, { "profiles", profiles.ToArray() }
            };
        }

        // fetchedAt/publishedAt intentionally do not defeat duplicate suppression; the heartbeat refreshes them.
        public static string Hash(object snapshot) {
            var semantic = new List<object>();
            foreach (object p in J.Arr(J.Get(snapshot, "profiles")))
                semantic.Add(new object[] { J.Get(p, "id"), J.Get(p, "label"), J.Get(p, "ok"), J.Get(p, "status"), J.Get(p, "fiveHour"), J.Get(p, "longTerm"), J.Get(p, "localUsage") });
            string text = J.Json(new object[] { J.Get(snapshot, "version"), J.Get(snapshot, "type"), J.Get(snapshot, "deviceId"), J.Get(snapshot, "deviceName"), semantic.ToArray() });
            return RemoteCrypto.Hex(RemoteCrypto.Sha256(text));
        }

        public static bool PublishDue(string hash, object state, DateTimeOffset now, int heartbeatMinutes, string topic) {
            if (state == null || J.Text(J.Get(state, "lastPublishedHash")).Length == 0) return true;
            if (!string.IsNullOrEmpty(topic) && J.Text(J.Get(state, "lastPublishedTopic")) != topic) return true;
            if (J.Text(J.Get(state, "lastPublishedHash")) != hash) return true;
            DateTimeOffset? last = J.Time(J.Get(state, "lastPublishedAt"));
            return !last.HasValue || (now - last.Value).TotalMinutes >= heartbeatMinutes;
        }
    }

    // ------------------------------------------------------------------ Codex completion events (remote notifications)
    public static class Completions {
        static string TailText(string path, int maxBytes) {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                long start = Math.Max(0, stream.Length - maxBytes);
                stream.Seek(start, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, false), true, 4096)) {
                    string text = reader.ReadToEnd();
                    if (start > 0) { int newline = text.IndexOf('\n'); text = newline >= 0 ? text.Substring(newline + 1) : ""; }
                    return text;
                }
            }
        }

        public static string ProjectName(string path) {
            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, false), true, 4096)) {
                    for (int i = 0; i < 40 && !reader.EndOfStream; i++) {
                        string line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line) || line.IndexOf("session_meta", StringComparison.Ordinal) < 0) continue;
                        object record;
                        try { record = J.Parse(line); } catch (ArgumentException) { continue; } catch (InvalidOperationException) { continue; }
                        if (J.Text(J.Get(record, "type")) != "session_meta") continue;
                        string cwd = J.Text(J.Get(record, "payload", "cwd"));
                        if (cwd.Length == 0) cwd = J.Text(J.Get(record, "cwd"));
                        if (cwd.Length == 0) continue;
                        string trimmed = cwd.TrimEnd('\\', '/');
                        int slash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
                        string leaf = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
                        return leaf.Length == 0 ? trimmed : leaf;
                    }
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return Path.GetFileNameWithoutExtension(path);
        }

        // Persisted event_msg/task_complete records only; never guesses from process exit.
        public static List<Dictionary<string, object>> FromFile(string path, DateTimeOffset since, string deviceId, string deviceName, bool includeSummary) {
            var result = new List<Dictionary<string, object>>();
            string project = ProjectName(path);
            foreach (string line in TailText(path, 262144).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                if (line.IndexOf("task_complete", StringComparison.Ordinal) < 0) continue;
                object record;
                try { record = J.Parse(line); } catch (ArgumentException) { continue; } catch (InvalidOperationException) { continue; }
                if (J.Text(J.Get(record, "type")) != "event_msg" || J.Text(J.Get(record, "payload", "type")) != "task_complete") continue;
                string turnId = J.Text(J.Get(record, "payload", "turn_id"));
                if (turnId.Length == 0) continue;
                DateTimeOffset finished = J.Time(J.Get(record, "timestamp")) ?? DateTimeOffset.UtcNow;
                if (finished.ToUniversalTime() < since.ToUniversalTime()) continue;
                string summary = "";
                if (includeSummary) {
                    summary = J.Text(J.Get(record, "payload", "last_agent_message"));
                    if (summary.Length > 800) summary = summary.Substring(0, 800) + "…";
                }
                result.Add(new Dictionary<string, object> {
                    { "version", 1 }, { "id", deviceId + ":" + turnId }, { "deviceId", deviceId }, { "deviceName", deviceName }, { "turnId", turnId },
                    { "project", project }, { "status", J.Get(record, "payload", "error") != null ? "error" : "completed" },
                    { "finishedAt", finished.ToUniversalTime().ToString("o", J.Inv) }, { "summary", summary }
                });
            }
            return result;
        }
    }

    // ------------------------------------------------------------------ persisted settings
    public sealed class RemoteSettings {
        public bool Enabled, UsageSyncEnabled, IncludeSummary;
        public string DeviceId = Guid.NewGuid().ToString("N"), DeviceName = Environment.MachineName, PairKey = "", RelayUrl = "https://ntfy.sh";

        public bool Active { get { return (Enabled || UsageSyncEnabled) && !string.IsNullOrWhiteSpace(PairKey) && !string.IsNullOrWhiteSpace(DeviceId); } }

        public static RemoteSettings FromObject(object value) {
            var settings = new RemoteSettings();
            if (value == null) return settings;
            settings.Enabled = J.Bool(J.Get(value, "enabled"));
            settings.UsageSyncEnabled = J.Bool(J.Get(value, "usageSyncEnabled"));
            settings.IncludeSummary = J.Bool(J.Get(value, "includeSummary"));
            if (J.Text(J.Get(value, "deviceId")).Trim().Length > 0) settings.DeviceId = J.Text(J.Get(value, "deviceId")).Trim();
            if (J.Text(J.Get(value, "deviceName")).Trim().Length > 0) settings.DeviceName = J.Text(J.Get(value, "deviceName")).Trim();
            settings.PairKey = J.Text(J.Get(value, "pairKey")).Trim();
            if (J.Text(J.Get(value, "relayUrl")).Trim().Length > 0) settings.RelayUrl = J.Text(J.Get(value, "relayUrl")).Trim();
            return settings;
        }

        public static RemoteSettings Load(string path) {
            try { return FromObject(J.ReadFile(path)); } catch (Exception) { return new RemoteSettings(); }
        }

        public Dictionary<string, object> ToObject() {
            return new Dictionary<string, object> {
                { "enabled", Enabled }, { "usageSyncEnabled", UsageSyncEnabled }, { "deviceId", DeviceId }, { "deviceName", DeviceName },
                { "pairKey", PairKey }, { "includeSummary", IncludeSummary }, { "relayUrl", RelayUrl }
            };
        }

        public void Save(string path) { J.AtomicWrite(path, J.Json(ToObject()), true); }

        public RemoteSettings Clone() { return FromObject(ToObject()); }
    }

    public sealed class UiSettings {
        public int? BallX, BallY, PanelX, PanelY;
        public int PanelWidth = 440, PanelHeight = 840, CompactOpacity = 80;
        public bool TopMost = true, BallVisible = true, SetupSeen, HideFullscreen, RingMode, CompactMode = true, ClearMode = true, EdgeSnap = true, NotificationsEnabled;
        public bool RecoveryNotifications = true, FailureNotifications = true;
        public string MonitorSize = "small", DockHorizontal = "none", DockVertical = "none", UpdateChannel = "stable";
        public int[] NotificationThresholds = { 20, 10 };

        static int? Int(object value) { double? n = J.Num(value); return n.HasValue ? (int?)(int)Math.Round(n.Value) : null; }
        static bool BoolOr(object source, string name, bool fallback) { object v = J.Get(source, name); return v is bool ? (bool)v : fallback; }

        // Older settings files stay compatible; invalid values fall back to defaults.
        public static UiSettings FromObject(object value, int defaultPanelWidth, int defaultPanelHeight) {
            var s = new UiSettings { PanelWidth = defaultPanelWidth, PanelHeight = defaultPanelHeight };
            if (value == null) return s;
            s.BallX = Int(J.Get(value, "ballX")); s.BallY = Int(J.Get(value, "ballY"));
            s.PanelX = Int(J.Get(value, "panelX")); s.PanelY = Int(J.Get(value, "panelY"));
            s.PanelWidth = Int(J.Get(value, "panelWidth")) ?? defaultPanelWidth;
            s.PanelHeight = Int(J.Get(value, "panelHeight")) ?? defaultPanelHeight;
            s.TopMost = BoolOr(value, "topMost", true); s.BallVisible = BoolOr(value, "ballVisible", true);
            s.SetupSeen = BoolOr(value, "setupSeen", false); s.HideFullscreen = BoolOr(value, "hideFullscreen", false);
            s.RingMode = BoolOr(value, "ringMode", false); s.CompactMode = BoolOr(value, "compactMode", true);
            s.ClearMode = BoolOr(value, "clearMode", true); s.EdgeSnap = BoolOr(value, "edgeSnap", true);
            s.NotificationsEnabled = BoolOr(value, "notificationsEnabled", false);
            s.RecoveryNotifications = BoolOr(value, "recoveryNotifications", true);
            s.FailureNotifications = BoolOr(value, "failureNotifications", true);
            s.UpdateChannel = J.Text(J.Get(value, "updateChannel")) == "beta" ? "beta" : "stable";
            s.CompactOpacity = Math.Max(40, Math.Min(100, Int(J.Get(value, "compactOpacity")) ?? 80));
            string size = J.Text(J.Get(value, "monitorSize"));
            s.MonitorSize = size == "medium" || size == "large" ? size : "small";
            string h = J.Text(J.Get(value, "dockHorizontal")), v = J.Text(J.Get(value, "dockVertical"));
            s.DockHorizontal = h == "left" || h == "right" ? h : "none";
            s.DockVertical = v == "top" || v == "bottom" ? v : "none";
            var levels = new List<int>();
            foreach (object level in J.Arr(J.Get(value, "notificationThresholds"))) { int? l = Int(level); if (l.HasValue && l.Value > 0 && l.Value < 100 && !levels.Contains(l.Value)) levels.Add(l.Value); }
            s.NotificationThresholds = levels.Count > 0 ? levels.OrderByDescending(l => l).ToArray() : new[] { 20, 10 };
            return s;
        }

        public Dictionary<string, object> ToObject() {
            return new Dictionary<string, object> {
                { "ballX", BallX }, { "ballY", BallY }, { "panelX", PanelX }, { "panelY", PanelY }, { "panelWidth", PanelWidth }, { "panelHeight", PanelHeight },
                { "topMost", TopMost }, { "ballVisible", BallVisible }, { "setupSeen", SetupSeen }, { "hideFullscreen", HideFullscreen }, { "ringMode", RingMode },
                { "compactMode", CompactMode }, { "compactOpacity", CompactOpacity }, { "clearMode", ClearMode }, { "monitorSize", MonitorSize }, { "edgeSnap", EdgeSnap },
                { "dockHorizontal", DockHorizontal }, { "dockVertical", DockVertical }, { "notificationsEnabled", NotificationsEnabled }, { "notificationThresholds", NotificationThresholds },
                { "recoveryNotifications", RecoveryNotifications }, { "failureNotifications", FailureNotifications }, { "updateChannel", UpdateChannel }
            };
        }

        public static double SizeScale(string size) { return size == "medium" ? 1.2 : size == "large" ? 1.4 : 1.0; }
    }

    public static class Profiles {
        public static Dictionary<string, object> Load(string path) {
            var config = J.Map(J.ReadFile(path));
            if (config == null || !(J.Get(config, "profiles") is IEnumerable)) throw new InvalidDataException("账号配置无效。");
            var normalized = new List<object>();
            foreach (object profile in J.Arr(J.Get(config, "profiles"))) if (profile is Dictionary<string, object>) normalized.Add(profile);
            config["profiles"] = normalized.ToArray();
            return config;
        }

        public static void Save(Dictionary<string, object> config, string path) { J.AtomicWrite(path, J.Json(config), true); }

        public static Dictionary<string, object> Find(object config, string id) {
            foreach (object profile in J.Arr(J.Get(config, "profiles"))) if (J.Text(J.Get(profile, "id")) == id) return profile as Dictionary<string, object>;
            return null;
        }

        public static string Name(object config, string id) {
            string label = J.Text(J.Get(Find(config, id), "label"));
            return label.Trim().Length > 0 ? label : Model.DefaultAccountName(id);
        }

        public static string[] ActiveIds(object config) {
            return Model.AccountIds.Where(id => { var p = Find(config, id); return p != null && Model.Enabled(p); }).ToArray();
        }

        public static string ResolveHome(string path, string fallback) {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(path)) path = fallback;
            if (path == "~") return root;
            if (path.StartsWith("~/") || path.StartsWith("~\\")) return Path.Combine(root, path.Substring(2));
            try { return Path.GetFullPath(path); } catch (Exception) { return path; }
        }
    }
}
