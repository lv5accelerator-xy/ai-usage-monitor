// Claude subscription quota + local Claude Code usage statistics.
// Shared by the packaged EXE (compiled in) and script mode (Add-Type).
// C# 5 only: Windows .NET Framework csc.exe and Windows PowerShell 5.1 compile this file.
//
// Privacy: the OAuth access token is read from Claude Code's own credentials
// file for the duration of one HTTPS request and is never written, logged or
// refreshed here. Refreshing would rotate Claude Code's refresh token and can
// log the user out of running sessions, so an expired login is reported instead.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexUsageDesktop {
    public sealed class ClaudeHttpException : Exception {
        public int Status { get; private set; }
        public ClaudeHttpException(int status, string message) : base(message) { Status = status; }
    }

    public static class ClaudeReader {
        public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        // The usage endpoint rate-limits aggressively; the UI polls every minute.
        public static int MinApiIntervalSeconds = 120;
        public static int CacheFallbackMinutes = 15;
        public const int LocalDays = 7;
        public static Func<string, string, string> HttpGet = DefaultHttpGet;

        static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        static object Get(object value, string name) { object result; return Map(value).TryGetValue(name, out result) ? result : null; }
        static string Text(object value) { return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture); }
        static double? Number(object value) {
            if (value == null || value is bool) return null;
            double result;
            return double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && !double.IsNaN(result) && !double.IsInfinity(result) ? (double?)result : null;
        }
        static long Count(object value) { double? n = Number(value); return n.HasValue && n.Value > 0 ? (long)n.Value : 0; }
        static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }; }

        public static string ResolveHome(string path) {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(path)) {
                string configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                path = string.IsNullOrWhiteSpace(configured) ? "~/.claude" : configured;
            }
            if (path == "~") return root;
            if (path.StartsWith("~/") || path.StartsWith("~\\")) return Path.Combine(root, path.Substring(2));
            return Path.GetFullPath(path);
        }

        // ---------------------------------------------------------------- quota

        static string DefaultHttpGet(string url, string token) {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (NotSupportedException) { }
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.Accept = "application/json";
            request.UserAgent = "codex-dual-usage-dashboard";
            request.Headers["Authorization"] = "Bearer " + token;
            request.Headers["anthropic-beta"] = "oauth-2025-04-20";
            try {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) {
                    var buffer = new char[8192];
                    var text = new StringBuilder();
                    int read;
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) {
                        text.Append(buffer, 0, read);
                        if (text.Length > 1024 * 1024) throw new InvalidDataException("Claude 响应过大。");
                    }
                    return text.ToString();
                }
            } catch (WebException ex) {
                var failed = ex.Response as HttpWebResponse;
                if (failed != null) {
                    int code = (int)failed.StatusCode;
                    failed.Close();
                    throw new ClaudeHttpException(code, StatusMessage(code));
                }
                throw new ClaudeHttpException(0, "无法连接 Claude 服务器，请检查网络或代理。");
            }
        }

        public static string StatusMessage(int status) {
            if (status == 401 || status == 403) return "Claude 登录已过期：打开 Claude Code 运行一次（必要时执行 /login），恢复后会自动读取。";
            if (status == 429) return "Claude 用量接口暂时限流，稍后会自动重试。";
            if (status >= 500) return "Claude 服务器暂时不可用，稍后会自动重试。";
            if (status == 0) return "无法连接 Claude 服务器，请检查网络或代理。";
            return "Claude 用量读取失败（HTTP " + status + "）。";
        }

        public static Dictionary<string, object> ReadCredentials(string home) {
            string path = Path.Combine(home, ".credentials.json");
            if (!File.Exists(path)) return null;
            object parsed;
            try { parsed = Json().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)); }
            catch (ArgumentException) { throw new InvalidDataException("Claude 登录文件无法解析，请在 Claude Code 中重新登录。"); }
            object oauth = Get(parsed, "claudeAiOauth");
            string token = Text(Get(oauth, "accessToken"));
            if (string.IsNullOrWhiteSpace(token)) return null;
            return new Dictionary<string, object> {
                { "token", token },
                { "plan", PlanLabel(Text(Get(oauth, "subscriptionType")), Text(Get(oauth, "rateLimitTier"))) }
            };
        }

        public static string PlanLabel(string subscription, string tier) {
            string plan = string.IsNullOrWhiteSpace(subscription) ? "Claude" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(subscription.Trim().ToLowerInvariant());
            Match multiplier = Regex.Match(tier ?? "", @"(\d+)x", RegexOptions.IgnoreCase);
            if (multiplier.Success) plan += " " + multiplier.Groups[1].Value + "x";
            return plan;
        }

        static string Iso(object value) {
            DateTimeOffset parsed;
            string text = Text(value);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed) ? parsed.ToUniversalTime().ToString("o") : null;
        }

        static Dictionary<string, object> Window(object raw, string kind, string label, int minutes) {
            if (raw == null) return null;
            double? used = Number(Get(raw, "utilization"));
            if (!used.HasValue) return null;
            // The endpoint reports 0-100. Clamp defensively; remaining is never negative.
            double clamped = Math.Max(0, Math.Min(100, used.Value));
            return new Dictionary<string, object> {
                { "kind", kind }, { "label", label }, { "usedPercent", clamped }, { "remainingPercent", 100 - clamped },
                { "windowMinutes", minutes }, { "resetsAt", Iso(Get(raw, "resets_at")) }
            };
        }

        public static Dictionary<string, object> ParseUsage(string body) {
            object parsed;
            try { parsed = Json().DeserializeObject(body ?? ""); } catch (ArgumentException) { throw new InvalidDataException("Claude 用量响应格式无效。"); }
            if (!(parsed is Dictionary<string, object>)) throw new InvalidDataException("Claude 用量响应格式无效。");
            var five = Window(Get(parsed, "five_hour"), "5h", "5 小时", 300);
            var weekly = Window(Get(parsed, "seven_day"), "weekly", "Weekly", 10080);
            var windows = new List<object>();
            if (five != null) windows.Add(five);
            if (weekly != null) windows.Add(weekly);
            foreach (var extra in new[] { new[] { "seven_day_opus", "weekly-opus", "Opus 每周" }, new[] { "seven_day_sonnet", "weekly-sonnet", "Sonnet 每周" } }) {
                var window = Window(Get(parsed, extra[0]), extra[1], extra[2], 10080);
                if (window != null) windows.Add(window);
            }
            if (five == null && weekly == null) throw new InvalidDataException("Claude 未返回 5 小时或每周额度（API Key 或企业账号可能没有订阅额度）。");
            return new Dictionary<string, object> { { "fiveHour", five }, { "weekly", weekly }, { "windows", windows.ToArray() } };
        }

        // Returns { quota, fetchedAt, cached }. Cache holds percentages only, never credentials.
        public static Dictionary<string, object> ReadQuota(string home, string token, string cachePath, DateTimeOffset now) {
            object cache = null;
            DateTimeOffset cachedAt = DateTimeOffset.MinValue;
            if (!string.IsNullOrEmpty(cachePath) && File.Exists(cachePath)) {
                try {
                    cache = Json().DeserializeObject(File.ReadAllText(cachePath, Encoding.UTF8));
                    if (!string.Equals(Text(Get(cache, "home")), home, StringComparison.OrdinalIgnoreCase) ||
                        !DateTimeOffset.TryParse(Text(Get(cache, "fetchedAt")), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out cachedAt)) cache = null;
                } catch (Exception) { cache = null; }
            }
            Func<Dictionary<string, object>> fromCache = delegate {
                return new Dictionary<string, object> { { "quota", Get(cache, "quota") }, { "fetchedAt", cachedAt.ToUniversalTime().ToString("o") }, { "cached", true } };
            };
            double age = cache == null ? double.MaxValue : (now - cachedAt).TotalSeconds;
            if (cache != null && age >= 0 && age < MinApiIntervalSeconds) return fromCache();
            Dictionary<string, object> quota;
            try {
                quota = ParseUsage(HttpGet(UsageUrl, token));
            } catch (Exception ex) {
                // Injected readers (tests, script hosts) may wrap the HTTP failure.
                ClaudeHttpException http = null;
                for (Exception current = ex; current != null && http == null; current = current.InnerException) http = current as ClaudeHttpException;
                if (http == null) throw;
                bool transient = http.Status == 0 || http.Status == 429 || http.Status >= 500;
                if (transient && cache != null && age >= 0 && age < CacheFallbackMinutes * 60) return fromCache();
                throw http;
            }
            if (!string.IsNullOrEmpty(cachePath)) {
                try { AtomicWrite(cachePath, Json().Serialize(new Dictionary<string, object> { { "home", home }, { "fetchedAt", now.ToUniversalTime().ToString("o") }, { "quota", quota } })); }
                catch (Exception) { }
            }
            return new Dictionary<string, object> { { "quota", quota }, { "fetchedAt", now.ToUniversalTime().ToString("o") }, { "cached", false } };
        }

        // ---------------------------------------------------------------- pricing

        // USD per million tokens: input, output, cache write (5 min), cache read.
        // Estimates at public API list prices; subscription users are not billed per token.
        public static bool IsOpenAiModel(string model) {
            string m = (model ?? "").ToLowerInvariant();
            return m.StartsWith("gpt") || m.Contains("codex") || Regex.IsMatch(m, @"^o\d");
        }

        // OpenAI list prices (input, output, n/a cache write, cached input) for Codex sessions.
        static double[] OpenAiPrice(string m) {
            if (Regex.IsMatch(m, @"5[.-]5")) return m.Contains("pro") ? new double[] { 30, 180, 30, 30 } : new double[] { 5, 30, 5, 0.5 };
            if (Regex.IsMatch(m, @"5[.-]4")) {
                if (m.Contains("nano")) return new double[] { 0.2, 1.25, 0.2, 0.02 };
                if (m.Contains("mini")) return new double[] { 0.75, 4.5, 0.75, 0.075 };
                return new double[] { 2.5, 15, 2.5, 0.25 };
            }
            if (Regex.IsMatch(m, @"5[.-][23]")) return new double[] { 1.75, 14, 1.75, 0.175 };
            if (m.StartsWith("o3")) return new double[] { 2, 8, 2, 0.5 };
            if (m.StartsWith("o4")) return new double[] { 1.1, 4.4, 1.1, 0.275 };
            if (m.Contains("nano")) return new double[] { 0.05, 0.4, 0.05, 0.005 };
            if (m.Contains("mini")) return new double[] { 0.25, 2, 0.25, 0.025 };
            return new double[] { 1.25, 10, 1.25, 0.125 };
        }

        public static double[] Price(string model) {
            string m = (model ?? "").ToLowerInvariant();
            if (m.Length == 0 || m.Contains("synthetic")) return new double[] { 0, 0, 0, 0 };
            if (IsOpenAiModel(m)) return OpenAiPrice(m);
            int major = 0, minor = 0;
            Match version = Regex.Match(m, @"(opus|sonnet|haiku|fable|mythos)-(\d{1,2})(?:-(\d))?(?=-|$)");
            if (version.Success) {
                major = int.Parse(version.Groups[2].Value, CultureInfo.InvariantCulture);
                if (version.Groups[3].Success) minor = int.Parse(version.Groups[3].Value, CultureInfo.InvariantCulture);
            } else {
                Match legacy = Regex.Match(m, @"claude-(\d)(?:-(\d))?-(opus|sonnet|haiku)");
                if (legacy.Success) {
                    major = int.Parse(legacy.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (legacy.Groups[2].Success) minor = int.Parse(legacy.Groups[2].Value, CultureInfo.InvariantCulture);
                }
            }
            double v = major + minor / 10.0;
            if (m.Contains("fable") || m.Contains("mythos")) return Rates(10, 50, 1.0);
            if (m.Contains("opus")) {
                if (v >= 5.5) return Rates(4, 20, 0.2);
                if (v >= 4.5) return Rates(5, 25, 0.5);
                return Rates(15, 75, 1.5);
            }
            if (m.Contains("sonnet")) {
                if (v >= 5.5) return Rates(2, 10, 0.1);
                if (v >= 5) return Rates(2, 10, 0.2);
                return Rates(3, 15, 0.3);
            }
            if (m.Contains("haiku")) {
                if (v >= 5.5) return Rates(0.1, 0.5, 0.01);
                if (v >= 4) return Rates(1, 5, 0.1);
                if (v >= 3.5) return Rates(0.8, 4, 0.08);
                return Rates(0.25, 1.25, 0.03);
            }
            return Rates(3, 15, 0.3);
        }
        static double[] Rates(double input, double output, double cacheRead) { return new[] { input, output, input * 1.25, cacheRead }; }

        public static double Cost(string model, long input, long output, long cacheWrite, long cacheRead) {
            double[] p = Price(model);
            return (input * p[0] + output * p[1] + cacheWrite * p[2] + cacheRead * p[3]) / 1000000.0;
        }

        public static string ShortModel(string model) {
            if (IsOpenAiModel(model)) { string t = model.Trim(); return t.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) ? "GPT" + t.Substring(3) : t; }
            Match match = Regex.Match((model ?? "").ToLowerInvariant(), @"(opus|sonnet|haiku|fable|mythos)(?:-(\d{1,2})(?:-(\d))?(?=-|$))?");
            if (!match.Success) return string.IsNullOrWhiteSpace(model) ? "" : model;
            string name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Groups[1].Value);
            if (match.Groups[2].Success) name += " " + match.Groups[2].Value + (match.Groups[3].Success ? "." + match.Groups[3].Value : "");
            return name;
        }

        // ---------------------------------------------------------------- local logs

        sealed class Entry { public string Key, Day, Model; public long Input, Output, CacheWrite, CacheRead; }
        // Per-file parser state that must survive incremental reads (Codex only).
        sealed class FileState { public string Model = "", Account = ""; public long LastTotal = -1; }

        static string LocalDay(string timestamp) {
            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)) return null;
            return parsed.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        static Entry ParseLine(string line, string fallbackKey, FileState state, bool codex) {
            return codex ? ParseCodexLine(line, fallbackKey, state) : ParseClaudeLine(line, fallbackKey);
        }

        // Codex rollout: event_msg/token_count carries per-turn last_token_usage plus a
        // cumulative total. Repeated events with an unchanged total are skipped; the
        // model comes from the latest turn_context. cached_input is a subset of input
        // and reasoning output is a subset of output.
        static Entry ParseCodexLine(string line, string fallbackKey, FileState state) {
            bool context = line.IndexOf("\"turn_context\"", StringComparison.Ordinal) >= 0;
            bool meta = !context && line.IndexOf("\"session_meta\"", StringComparison.Ordinal) >= 0;
            if (!context && !meta && line.IndexOf("\"token_count\"", StringComparison.Ordinal) < 0) return null;
            object record;
            try { record = Json().DeserializeObject(line); } catch (ArgumentException) { return null; } catch (InvalidOperationException) { return null; }
            object payload = Get(record, "payload");
            string type = Text(Get(record, "type"));
            // The Codex desktop app writes every account's sessions into the shared
            // ~/.codex/sessions; session_meta names the account that created the session.
            if (type == "session_meta") {
                string account = Text(Get(payload, "creator_account_id"));
                if (account.Length > 0) state.Account = account;
                return null;
            }
            if (type == "turn_context") {
                string model = Text(Get(payload, "model"));
                if (model.Length > 0) state.Model = model;
                return null;
            }
            if (type != "event_msg" || Text(Get(payload, "type")) != "token_count") return null;
            object info = Get(payload, "info");
            object last = Get(info, "last_token_usage");
            if (last == null) return null;
            long total = Count(Get(Get(info, "total_token_usage"), "total_tokens"));
            if (total > 0 && total == state.LastTotal) return null;
            if (total > 0) state.LastTotal = total;
            long input = Count(Get(last, "input_tokens")), cached = Math.Min(input, Count(Get(last, "cached_input_tokens"))), output = Count(Get(last, "output_tokens"));
            string timestamp = Text(Get(record, "timestamp"));
            var entry = new Entry { Model = state.Model.Length > 0 ? state.Model : "codex", Input = input - cached, CacheRead = cached, Output = output, Day = LocalDay(timestamp) };
            if (entry.Day == null || input + output == 0) return null;
            // Forked sessions can copy earlier events into another file; the same
            // timestamp and counters identify the same model call across files.
            entry.Key = timestamp.Length > 0 ? "codex:" + timestamp + ":" + total + ":" + Count(Get(last, "total_tokens")) : fallbackKey;
            return entry;
        }

        static Entry ParseClaudeLine(string line, string fallbackKey) {
            if (line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0 || line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) return null;
            object record;
            try { record = Json().DeserializeObject(line); } catch (ArgumentException) { return null; } catch (InvalidOperationException) { return null; }
            if (Text(Get(record, "type")) != "assistant") return null;
            object message = Get(record, "message"), usage = Get(message, "usage");
            if (usage == null) return null;
            var entry = new Entry {
                Model = Text(Get(message, "model")),
                Input = Count(Get(usage, "input_tokens")),
                Output = Count(Get(usage, "output_tokens")),
                CacheWrite = Count(Get(usage, "cache_creation_input_tokens")),
                CacheRead = Count(Get(usage, "cache_read_input_tokens")),
                Day = LocalDay(Text(Get(record, "timestamp")))
            };
            if (entry.Day == null || entry.Input + entry.Output + entry.CacheWrite + entry.CacheRead == 0) return null;
            string id = Text(Get(message, "id")), requestId = Text(Get(record, "requestId"));
            // Streaming writes one line per content block with the same message/request id;
            // resumed sessions can copy earlier messages into a new file. Dedupe globally.
            entry.Key = id.Length > 0 ? id + ":" + requestId : (Text(Get(record, "uuid")).Length > 0 ? Text(Get(record, "uuid")) : fallbackKey);
            return entry;
        }

        static object[] Pack(Entry e) { return new object[] { e.Key, e.Day, e.Model, e.Input, e.Output, e.CacheWrite, e.CacheRead }; }
        static Entry Unpack(object raw) {
            var a = raw as object[];
            if (a == null) { var list = raw as ArrayList; if (list != null) a = list.ToArray(); }
            if (a == null || a.Length < 7) return null;
            return new Entry { Key = Text(a[0]), Day = Text(a[1]), Model = Text(a[2]), Input = Count(a[3]), Output = Count(a[4]), CacheWrite = Count(a[5]), CacheRead = Count(a[6]) };
        }

        // Reads complete lines appended after offset. Returns the new offset.
        static long ReadAppended(string path, long offset, string cutoffDay, List<Entry> output, FileState state, bool codex) {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                if (offset > stream.Length) offset = 0;
                stream.Seek(offset, SeekOrigin.Begin);
                var line = new MemoryStream();
                var buffer = new byte[64 * 1024];
                long position = offset, consumed = offset;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                    int start = 0;
                    for (int i = 0; i < read; i++) {
                        if (buffer[i] != (byte)'\n') continue;
                        line.Write(buffer, start, i - start);
                        long lineStart = consumed;
                        consumed = position + i + 1;
                        start = i + 1;
                        if (line.Length > 0 && line.Length < 32 * 1024 * 1024) {
                            string text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
                            Entry entry = ParseLine(text, path + "#" + lineStart, state, codex);
                            if (entry != null && string.CompareOrdinal(entry.Day, cutoffDay) >= 0) output.Add(entry);
                        }
                        line.SetLength(0);
                    }
                    if (start < read) line.Write(buffer, start, read - start);
                    position += read;
                }
                // A trailing partial line is still being written; re-read it next time.
                return consumed;
            }
        }

        static Dictionary<string, object> Totals() {
            return new Dictionary<string, object> { { "inputTokens", 0L }, { "outputTokens", 0L }, { "cacheWriteTokens", 0L }, { "cacheReadTokens", 0L }, { "totalTokens", 0L }, { "costUsd", 0.0 }, { "messages", 0L } };
        }
        static void Add(Dictionary<string, object> totals, Entry e, double cost) {
            totals["inputTokens"] = (long)totals["inputTokens"] + e.Input;
            totals["outputTokens"] = (long)totals["outputTokens"] + e.Output;
            totals["cacheWriteTokens"] = (long)totals["cacheWriteTokens"] + e.CacheWrite;
            totals["cacheReadTokens"] = (long)totals["cacheReadTokens"] + e.CacheRead;
            totals["totalTokens"] = (long)totals["totalTokens"] + e.Input + e.Output + e.CacheWrite + e.CacheRead;
            totals["costUsd"] = (double)totals["costUsd"] + cost;
            totals["messages"] = (long)totals["messages"] + 1;
        }

        public static Dictionary<string, object> ScanLocal(string projectsDir, string cachePath, DateTime today) {
            return ScanFiles(new[] { projectsDir }, cachePath, today, false, null);
        }

        // Codex rollout logs live under <CODEX_HOME>/sessions/YYYY/MM/DD/rollout-*.jsonl.
        public static Dictionary<string, object> ScanCodex(string sessionsDir, string cachePath, DateTime today) {
            return ScanFiles(new[] { sessionsDir }, cachePath, today, true, null);
        }

        // Scans the account's own sessions plus the shared Codex app sessions. A session
        // counts for this account when its creator_account_id matches; sessions without
        // an account id count when they are in the account's own folder, or in the shared
        // folder while that folder is logged in to this account.
        public static Dictionary<string, object> ScanCodexAccount(string ownSessions, string sharedSessions, string accountId, string sharedAccountId, string cachePath, DateTime today) {
            var dirs = new List<string> { ownSessions };
            if (!string.IsNullOrEmpty(sharedSessions) && !string.Equals(Path.GetFullPath(sharedSessions).TrimEnd('\\', '/'), Path.GetFullPath(ownSessions).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) dirs.Add(sharedSessions);
            string ownPrefix = Path.GetFullPath(ownSessions).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            accountId = accountId ?? ""; sharedAccountId = sharedAccountId ?? "";
            Func<string, string, bool> include = delegate(string file, string account) {
                if (account.Length > 0) return accountId.Length > 0 && string.Equals(account, accountId, StringComparison.OrdinalIgnoreCase);
                if (Path.GetFullPath(file).StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase)) return true;
                return accountId.Length > 0 && string.Equals(sharedAccountId, accountId, StringComparison.OrdinalIgnoreCase);
            };
            return ScanFiles(dirs.ToArray(), cachePath, today, true, include);
        }

        static Dictionary<string, object> ScanFiles(string[] dirs, string cachePath, DateTime today, bool codex, Func<string, string, bool> include) {
            string projectsDir = string.Join("|", dirs);
            string todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string cutoffDay = today.AddDays(-(LocalDays - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateTime cutoffWrite = today.AddDays(-(LocalDays + 1)).ToUniversalTime();
            var cachedFiles = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(cachePath) && File.Exists(cachePath)) {
                try {
                    object cache = Json().DeserializeObject(File.ReadAllText(cachePath, Encoding.UTF8));
                    if (Text(Get(cache, "projects")) == projectsDir && Number(Get(cache, "version")) == 2 && Text(Get(cache, "kind")) == (codex ? "codex" : "claude"))
                        foreach (var pair in Map(Get(cache, "files"))) cachedFiles[pair.Key] = pair.Value;
                } catch (Exception) { cachedFiles.Clear(); }
            }
            var nextFiles = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var all = new List<Entry>();
            int scanned = 0;
            foreach (string dir in dirs) {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories).ToList(); }
                catch (IOException) { files = new string[0]; } catch (UnauthorizedAccessException) { files = new string[0]; }
                foreach (string file in files) {
                    try {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc < cutoffWrite) continue;
                        scanned++;
                        object previous;
                        cachedFiles.TryGetValue(file, out previous);
                        var entries = new List<Entry>();
                        var state = new FileState();
                        long offset = 0;
                        if (previous != null && Count(Get(previous, "length")) <= info.Length) {
                            offset = Count(Get(previous, "offset"));
                            state.Model = Text(Get(previous, "model"));
                            state.Account = Text(Get(previous, "account"));
                            double? lastTotal = Number(Get(previous, "lastTotal"));
                            state.LastTotal = lastTotal.HasValue ? (long)lastTotal.Value : -1;
                            var old = Get(previous, "entries") as IEnumerable;
                            if (old != null) foreach (object raw in old) { Entry e = Unpack(raw); if (e != null && string.CompareOrdinal(e.Day, cutoffDay) >= 0) entries.Add(e); }
                        }
                        if (previous == null || Count(Get(previous, "length")) != info.Length || Text(Get(previous, "mtime")) != info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                            offset = ReadAppended(file, offset, cutoffDay, entries, state, codex);
                        if (include == null || include(file, state.Account)) all.AddRange(entries);
                        nextFiles[file] = new Dictionary<string, object> {
                            { "length", info.Length }, { "mtime", info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) }, { "offset", offset }, { "model", state.Model }, { "account", state.Account }, { "lastTotal", state.LastTotal },
                            { "entries", entries.Select(e => (object)Pack(e)).ToArray() }
                        };
                    } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            if (!string.IsNullOrEmpty(cachePath)) {
                try { AtomicWrite(cachePath, Json().Serialize(new Dictionary<string, object> { { "version", 2 }, { "kind", codex ? "codex" : "claude" }, { "projects", projectsDir }, { "files", nextFiles } })); }
                catch (Exception) { }
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var todayTotals = Totals();
            var weekTotals = Totals();
            var days = new SortedDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            for (int i = LocalDays - 1; i >= 0; i--) days[today.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = Totals();
            var models = new Dictionary<string, double>();
            // When duplicates exist, keep the copy whose model is known (forks may lack turn_context).
            foreach (Entry e in all.OrderBy(e => e.Day, StringComparer.Ordinal).ThenBy(e => e.Model == "codex" || e.Model.Length == 0 ? 1 : 0)) {
                if (!seen.Add(e.Key) || !days.ContainsKey(e.Day)) continue;
                double cost = Cost(e.Model, e.Input, e.Output, e.CacheWrite, e.CacheRead);
                Add(weekTotals, e, cost);
                Add(days[e.Day], e, cost);
                if (e.Day == todayKey) {
                    Add(todayTotals, e, cost);
                    string name = ShortModel(e.Model);
                    if (name.Length > 0) { double value; models.TryGetValue(name, out value); models[name] = value + e.Input + e.Output + e.CacheWrite + e.CacheRead; }
                }
            }
            return new Dictionary<string, object> {
                { "today", todayTotals },
                { "week", weekTotals },
                { "days", days.Select(d => (object)new Dictionary<string, object> { { "date", d.Key }, { "totalTokens", d.Value["totalTokens"] }, { "costUsd", Math.Round((double)d.Value["costUsd"], 4) } }).ToArray() },
                { "topModelToday", models.Count == 0 ? null : models.OrderByDescending(p => p.Value).First().Key },
                { "files", scanned },
                { "scannedAt", DateTimeOffset.UtcNow.ToString("o") },
                { "pricing", "API 等价估算" }
            };
        }

        static void AtomicWrite(string path, string text) {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // ---------------------------------------------------------------- profile

        public static object ReadProfile(object profile, string dataDir) {
            return ReadProfile(profile, dataDir, DateTimeOffset.Now);
        }

        public static object ReadProfile(object profile, string dataDir, DateTimeOffset now) {
            string id = Text(Get(profile, "id")), label = Text(Get(profile, "label"));
            string home = ResolveHome(Text(Get(profile, "claudeHome")));
            var result = new Dictionary<string, object> {
                { "id", id }, { "label", label }, { "provider", "claude" }, { "ok", true },
                { "fetchedAt", now.ToUniversalTime().ToString("o") }, { "planType", "Claude" },
                { "fiveHour", null }, { "weekly", null }, { "windows", new object[0] }, { "individualLimit", null },
                { "localUsage", null }, { "quotaError", null }
            };
            string cacheRoot = string.IsNullOrWhiteSpace(dataDir) ? null : dataDir;
            try { result["localUsage"] = ScanLocal(Path.Combine(home, "projects"), cacheRoot == null ? null : Path.Combine(cacheRoot, "claude-local-cache.json"), now.LocalDateTime.Date); }
            catch (Exception) { result["localUsage"] = null; }
            Dictionary<string, object> credentials = ReadCredentials(home);
            if (credentials == null) {
                object local = result["localUsage"];
                bool hasLocal = local != null && Count(Get(Get(local, "week"), "messages")) > 0;
                if (!hasLocal) throw new FileNotFoundException("未检测到 Claude Code 登录。请点击“安装 / 登录”，安装 Claude Code 并登录 Claude 订阅账号。");
                result["planType"] = "本地统计";
                result["quotaError"] = "未检测到 Claude 订阅登录，只显示本机 Claude Code 用量。";
                return result;
            }
            result["planType"] = credentials["plan"];
            Dictionary<string, object> quota;
            try {
                quota = ReadQuota(home, (string)credentials["token"], cacheRoot == null ? null : Path.Combine(cacheRoot, "claude-usage-cache.json"), now);
            } catch (Exception ex) {
                result["ok"] = false;
                result["error"] = ex.Message;
                return result;
            }
            object data = quota["quota"];
            result["fetchedAt"] = quota["fetchedAt"];
            result["quotaCached"] = quota["cached"];
            result["fiveHour"] = Get(data, "fiveHour");
            result["weekly"] = Get(data, "weekly");
            var windows = Get(data, "windows") as IEnumerable;
            result["windows"] = windows == null ? new object[0] : windows.Cast<object>().ToArray();
            return result;
        }
    }
}
