// Cross-device Codex completion notifications and mobile usage sync.
// Runs as a background thread inside the client (formerly remote-worker.ps1).
// File protocol, relay topics and message format are unchanged from v0.7/v0.8.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace CodexUsageDesktop {
    public sealed class RemoteWorker : IDisposable {
        public readonly string DataDir;
        readonly object gate = new object();
        Thread thread;
        volatile bool stopping;
        // (method, url, body) -> response text. Replaced by tests; never logs message bodies.
        public static Func<string, string, string, string> Http = DefaultHttp;
        public int LoopDelayMs = 700;

        public string SettingsPath { get { return Path.Combine(DataDir, "remote-notifications.json"); } }
        public string InboxPath { get { return Path.Combine(DataDir, "remote-inbox.jsonl"); } }
        public string StatusPath { get { return Path.Combine(DataDir, "remote-worker-status.json"); } }
        public string TestRequestPath { get { return Path.Combine(DataDir, "remote-test.request"); } }
        public string MobilePendingPath { get { return Path.Combine(DataDir, "mobile-usage-pending.json"); } }
        public string MobileStatusPath { get { return Path.Combine(DataDir, "mobile-usage-status.json"); } }
        public string MobileStatePath { get { return Path.Combine(DataDir, "mobile-usage-state.json"); } }
        string LogPath { get { return Path.Combine(DataDir, "logs", "remote-worker.log"); } }

        public RemoteWorker(string dataDir) { DataDir = dataDir; }

        public bool Running { get { lock (gate) return thread != null && thread.IsAlive; } }

        public void Start() {
            lock (gate) {
                if (thread != null && thread.IsAlive) return;
                stopping = false;
                thread = new Thread(Run) { IsBackground = true, Name = "CodexUsage remote worker" };
                thread.Start();
            }
        }

        public void Stop() {
            Thread current;
            lock (gate) { current = thread; stopping = true; }
            if (current != null && current.IsAlive && !current.Join(3000)) Log("Worker did not stop within 3 seconds.");
            lock (gate) thread = null;
        }

        public void Restart() { Stop(); Start(); }
        public void Dispose() { Stop(); }

        void Log(string message) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", J.Inv) + "] " + message + Environment.NewLine, Encoding.UTF8);
            } catch (Exception) { }
        }

        void Status(string path, string state, string message) {
            try { J.AtomicWrite(path, J.Json(new Dictionary<string, object> { { "state", state }, { "message", message }, { "updatedAt", DateTimeOffset.UtcNow.ToString("o", J.Inv) } }), false); }
            catch (Exception) { }
        }

        static string DefaultHttp(string method, string url, string body) {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (NotSupportedException) { }
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.UserAgent = "CodexUsage/" + typeof(RemoteWorker).Assembly.GetName().Version.ToString(3);
            request.Timeout = method == "GET" ? 15000 : 12000;
            request.ReadWriteTimeout = request.Timeout;
            if (body != null) {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentType = "text/plain; charset=utf-8";
                request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) {
                var text = new StringBuilder();
                var buffer = new char[8192];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) {
                    text.Append(buffer, 0, read);
                    if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("Relay response is too large.");
                }
                return text.ToString();
            }
        }

        public List<string> CodexHomes() {
            var homes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") };
            try {
                var config = Profiles.Load(Path.Combine(DataDir, "profiles.json"));
                foreach (object profile in J.Arr(J.Get(config, "profiles"))) {
                    if (!Model.Enabled(profile) || Model.IsClaude(profile)) continue;
                    string home = J.Text(J.Get(profile, "codexHome"));
                    if (home.Trim().Length > 0) homes.Add(Profiles.ResolveHome(home, home));
                }
            } catch (Exception ex) { Log("profiles.json warning: " + ex.Message); }
            return homes.ToList();
        }

        void Publish(RemoteSettings settings, string topic, object message) {
            string relay = RemoteCrypto.RelayUrl(settings.RelayUrl);
            Http("POST", relay + "/" + topic, RemoteCrypto.Protect(J.Json(message), settings.PairKey));
        }

        void AppendInbox(object message) {
            File.AppendAllText(InboxPath, J.Json(message) + Environment.NewLine, new UTF8Encoding(false));
            try {
                if (new FileInfo(InboxPath).Length > 524288) {
                    var tail = File.ReadAllLines(InboxPath, Encoding.UTF8).Reverse().Take(200).Reverse().ToArray();
                    File.WriteAllLines(InboxPath, tail, new UTF8Encoding(false));
                }
            } catch (Exception) { }
        }

        public void PollRelay(RemoteSettings settings, HashSet<string> seenRelayIds) {
            string relay = RemoteCrypto.RelayUrl(settings.RelayUrl);
            string content = Http("GET", relay + "/" + RemoteCrypto.RemoteTopic(settings.PairKey) + "/json?poll=1&since=12s", null);
            foreach (string line in (content ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                object message;
                try { message = J.Parse(line); } catch (ArgumentException) { continue; } catch (InvalidOperationException) { continue; }
                if (J.Text(J.Get(message, "event")) != "message" || J.Text(J.Get(message, "message")).Length == 0) continue;
                string relayId = J.Text(J.Get(message, "id"));
                if (relayId.Length > 0 && !seenRelayIds.Add(relayId)) continue;
                object evt;
                try { evt = J.Parse(RemoteCrypto.Unprotect(J.Text(J.Get(message, "message")), settings.PairKey)); }
                catch (Exception ex) { Log("Ignored undecryptable relay message: " + ex.GetType().Name); continue; }
                if (J.Num(J.Get(evt, "version")) != 1 || J.Text(J.Get(evt, "deviceId")) == settings.DeviceId) continue;
                AppendInbox(evt);
            }
        }

        void Run() {
            try {
                while (!stopping) {
                    if (!RunSession()) return;
                }
            } catch (Exception ex) {
                Log("FATAL: " + ex);
                Status(StatusPath, "error", "远程通知后台异常：" + ex.Message);
            }
        }

        // One settings generation. Returns true when settings changed and the loop should restart.
        bool RunSession() {
            Directory.CreateDirectory(DataDir);
            RemoteSettings settings = RemoteSettings.Load(SettingsPath);
            DateTime settingsWrite = File.Exists(SettingsPath) ? File.GetLastWriteTimeUtc(SettingsPath) : DateTime.MinValue;
            if (!settings.Active) {
                Status(StatusPath, "disabled", "远程功能未启用");
                Status(MobileStatusPath, "disabled", "手机额度同步未启用");
                return false;
            }
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            var sent = new HashSet<string>(StringComparer.Ordinal);
            var seenRelay = new HashSet<string>(StringComparer.Ordinal);
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lastWrite = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var watchers = new List<FileSystemWatcher>();
            DateTimeOffset lastFallback = DateTimeOffset.MinValue, lastPoll = DateTimeOffset.MinValue, nextMobile = DateTimeOffset.MinValue;
            int mobileFailures = 0;
            object mobileState = null;
            try { mobileState = J.ReadFile(MobileStatePath); } catch (Exception ex) { Log("Mobile state read warning: " + ex.Message); }
            try {
                if (settings.Enabled) {
                    foreach (string home in CodexHomes()) {
                        string sessions = Path.Combine(home, "sessions");
                        if (!Directory.Exists(sessions)) continue;
                        var watcher = new FileSystemWatcher(sessions, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                        FileSystemEventHandler handler = delegate(object sender, FileSystemEventArgs e) { lock (changed) changed.Add(e.FullPath); };
                        watcher.Changed += handler;
                        watcher.Created += handler;
                        watcher.EnableRaisingEvents = true;
                        watchers.Add(watcher);
                        Log("Watching " + sessions);
                    }
                    Status(StatusPath, "running", "正在监听 Codex 完成事件 · " + settings.DeviceName);
                }
                if (settings.UsageSyncEnabled) Status(MobileStatusPath, "waiting", "等待电脑生成额度快照…");
                while (!stopping) {
                    Thread.Sleep(LoopDelayMs);
                    DateTime currentWrite = File.Exists(SettingsPath) ? File.GetLastWriteTimeUtc(SettingsPath) : DateTime.MinValue;
                    if (currentWrite != settingsWrite) { Log("Settings changed; worker will restart."); return true; }
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (settings.Enabled) {
                        lock (changed) { paths.UnionWith(changed); changed.Clear(); }
                        if ((now - lastFallback).TotalSeconds >= 12) {
                            lastFallback = now;
                            foreach (string home in CodexHomes()) {
                                string sessions = Path.Combine(home, "sessions");
                                if (!Directory.Exists(sessions)) continue;
                                try {
                                    foreach (string file in Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories))
                                        if (File.GetLastWriteTimeUtc(file) >= startedAt.UtcDateTime.AddSeconds(-5)) paths.Add(file);
                                } catch (IOException) { } catch (UnauthorizedAccessException) { }
                            }
                        }
                        foreach (string path in paths) {
                            try {
                                if (!File.Exists(path)) continue;
                                long write = File.GetLastWriteTimeUtc(path).Ticks;
                                long previous;
                                if (lastWrite.TryGetValue(path, out previous) && previous == write) continue;
                                lastWrite[path] = write;
                                foreach (var evt in Completions.FromFile(path, startedAt.AddSeconds(-5), settings.DeviceId, settings.DeviceName, settings.IncludeSummary)) {
                                    string id = J.Text(evt["id"]);
                                    if (sent.Contains(id)) continue;
                                    try {
                                        Publish(settings, RemoteCrypto.RemoteTopic(settings.PairKey), evt);
                                        sent.Add(id);
                                        Log("Published " + id + " · " + J.Text(evt["project"]));
                                    } catch (Exception ex) {
                                        Log("Publish failed: " + ex.Message);
                                        Status(StatusPath, "degraded", "检测正常，但远程发送失败；将自动重试");
                                    }
                                }
                            } catch (Exception ex) { Log("Session parse warning: " + ex.Message); }
                        }
                        if (File.Exists(TestRequestPath)) {
                            try {
                                File.Delete(TestRequestPath);
                                Publish(settings, RemoteCrypto.RemoteTopic(settings.PairKey), new Dictionary<string, object> {
                                    { "version", 1 }, { "id", settings.DeviceId + ":test:" + Guid.NewGuid().ToString("N") }, { "deviceId", settings.DeviceId },
                                    { "deviceName", settings.DeviceName }, { "turnId", "test" }, { "project", "远程通知测试" }, { "status", "completed" },
                                    { "finishedAt", DateTimeOffset.UtcNow.ToString("o", J.Inv) }, { "summary", settings.IncludeSummary ? "这是一条跨电脑 Codex 完成通知测试。" : "" }
                                });
                                Log("Published test event.");
                            } catch (Exception ex) { Log("Test publish failed: " + ex.Message); }
                        }
                        if ((now - lastPoll).TotalSeconds >= 5) {
                            lastPoll = now;
                            try {
                                PollRelay(settings, seenRelay);
                                Status(StatusPath, "running", "正在监听 Codex 完成事件 · " + settings.DeviceName);
                            } catch (Exception ex) {
                                Log("Relay poll failed: " + ex.Message);
                                Status(StatusPath, "degraded", "本机监听正常，远程中继暂时不可用");
                            }
                        }
                    }
                    if (settings.UsageSyncEnabled && now >= nextMobile) {
                        nextMobile = now.AddSeconds(2);
                        if (File.Exists(MobilePendingPath)) {
                            try {
                                object snapshot = J.ReadFile(MobilePendingPath);
                                if (J.Num(J.Get(snapshot, "version")) != 1 || J.Text(J.Get(snapshot, "type")) != "usage_snapshot") throw new InvalidDataException("Invalid mobile snapshot schema.");
                                string hash = Mobile.Hash(snapshot), topic = RemoteCrypto.MobileTopic(settings.PairKey);
                                if (Mobile.PublishDue(hash, mobileState, now, 5, topic)) {
                                    Publish(settings, topic, snapshot);
                                    mobileState = new Dictionary<string, object> { { "lastPublishedHash", hash }, { "lastPublishedAt", now.ToString("o", J.Inv) }, { "lastPublishedTopic", topic } };
                                    J.AtomicWrite(MobileStatePath, J.Json(mobileState), false);
                                    mobileFailures = 0;
                                    Status(MobileStatusPath, "running", "同步正常");
                                    Log("Published mobile usage snapshot.");
                                }
                            } catch (Exception ex) {
                                mobileFailures++;
                                int[] delays = { 5, 15, 30, 60 };
                                int delay = delays[Math.Min(mobileFailures - 1, delays.Length - 1)];
                                nextMobile = now.AddSeconds(delay);
                                Status(MobileStatusPath, "degraded", "网络错误；约 " + delay + " 秒后重试");
                                Log("Mobile usage publish failed: " + ex.Message);
                            }
                        }
                    }
                }
                return false;
            } finally {
                foreach (var watcher in watchers) { try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch (Exception) { } }
            }
        }

        // Queue an allowlisted snapshot for the worker; no network I/O on the UI thread.
        public static bool WritePending(object data, RemoteSettings settings, string path) {
            if (data == null || settings == null || !settings.UsageSyncEnabled || string.IsNullOrWhiteSpace(settings.PairKey)) return false;
            J.AtomicWrite(path, J.Json(Mobile.Snapshot(data, settings, DateTimeOffset.UtcNow)), false);
            return true;
        }
    }
}
