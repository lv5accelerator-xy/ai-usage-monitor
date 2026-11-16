// Account setup and remote notification dialogs. C# 5 only.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodexUsage;

namespace CodexUsageDesktop {
    public sealed class SetupRow { public Dictionary<string, object> Profile; public CheckBox Enabled; public TextBox Name; public Label Status; public Button Login; }

    public sealed partial class TrayApp {
        public DpiForm SetupDialog;
        public readonly List<SetupRow> SetupRows = new List<SetupRow>();

        public static string FindClaudeCli() {
            var candidates = new List<string>();
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                foreach (string name in new[] { "claude.exe", "claude.cmd" }) candidates.Add(Path.Combine(folder.Trim('"'), name));
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"));
            foreach (string candidate in candidates) { try { if (File.Exists(candidate)) return candidate; } catch (Exception) { } }
            return null;
        }

        public string SetupStatus(object profile, string cli) {
            object current = Model.DisplayProfile(LastData, J.Text(J.Get(profile, "id")));
            bool connected = current != null && !Model.Stale(current);
            if (Model.IsClaude(profile)) {
                if (connected) return J.Text(J.Get(current, "quotaError")).Trim().Length > 0 ? "仅本地统计 · 未登录订阅" : "已连接";
                string home = Profiles.ResolveHome(J.Text(J.Get(profile, "claudeHome")), "~/.claude");
                if (File.Exists(Path.Combine(home, ".credentials.json"))) return "已有登录信息 · 等待验证";
                return string.IsNullOrWhiteSpace(ClaudeCli) ? "未安装 Claude Code" : "未登录";
            }
            if (string.IsNullOrWhiteSpace(cli)) return "未安装 Codex CLI";
            if (connected) return "已连接";
            string codexHome = Profiles.ResolveHome(J.Text(J.Get(profile, "codexHome")), "~/.codex");
            if (File.Exists(Path.Combine(codexHome, "auth.json"))) return "已有登录信息 · 等待验证";
            return "未登录";
        }

        DpiForm NewDialog(string title, int width, int height) {
            return new DpiForm {
                DisplayDpi = Popup.DisplayDpi, Text = title, Icon = AppIcon, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(U(width), U(height)),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = PopupBack, ForeColor = TextPrimary, Font = UiFont(10)
            };
        }

        Label Placed(Label label, int x, int y, int w, int h) { label.Dock = DockStyle.None; label.SetBounds(U(x), U(y), U(w), U(h)); return label; }
        T Placed<T>(T control, int x, int y, int w, int h) where T : Control { control.Dock = DockStyle.None; control.SetBounds(U(x), U(y), U(w), U(h)); return control; }

        void RefreshSetupStatus() {
            ProfileCli = UsageReader.FindCli();
            ClaudeCli = FindClaudeCli();
            foreach (SetupRow row in SetupRows) {
                row.Status.Text = SetupStatus(row.Profile, ProfileCli);
                bool missing = Model.IsClaude(row.Profile) ? string.IsNullOrWhiteSpace(ClaudeCli) : string.IsNullOrWhiteSpace(ProfileCli);
                row.Login.Text = missing ? "安装 / 登录" : "登录 / 切换";
            }
        }

        public void SaveAccountSettings() {
            if (!SetupRows.Any(r => r.Enabled.Checked)) { MessageBox.Show("至少保留一个账号。", "账号设置"); return; }
            try {
                var copy = J.Map(J.Parse(J.Json(ProfileConfig)));
                foreach (SetupRow row in SetupRows) {
                    var profile = Profiles.Find(copy, J.Text(J.Get(row.Profile, "id")));
                    string name = row.Name.Text.Trim();
                    if (name.Length == 0) throw new InvalidDataException("账号名称不能为空。");
                    profile["label"] = name;
                    profile["enabled"] = row.Enabled.Checked;
                }
                Profiles.Save(copy, ProfilesPath);
                ProfileConfig = copy;
                ProfileRevision++;
                AlertBaselinePending = true;
                ApplyProfileLayout();
                if (LastData != null) RenderData(LastData);
                SetupDialog.Close();
                if (!Smoke) StartRefresh();
            } catch (Exception ex) {
                if (Smoke) throw;
                MessageBox.Show(ex.Message, "无法保存账号设置");
            }
        }

        // previewTest returns the open, non-modal dialog for the smoke test.
        public DpiForm ShowAccountSettings(bool previewTest = false) {
            if (Smoke && !previewTest) return null;
            SetupDialog = NewDialog("账号与首次使用", 500, 440);
            SetupRows.Clear();
            SetupDialog.Controls.Add(Placed(NewLabel("选择显示的账号并命名：两个 Codex 账号 + 一个 Claude 账号，各自独立登录。", 10, false), 20, 16, 460, 32));
            int index = 0;
            foreach (string id in Model.AccountIds) {
                var profile = Profiles.Find(ProfileConfig, id);
                if (profile == null) continue;
                var row = new SetupRow {
                    Profile = profile,
                    Enabled = Placed(new CheckBox { Checked = Model.Enabled(profile), Text = id == "personal" ? "Codex 1" : id == "work" ? "Codex 2" : "Claude" }, 20, 64 + 80 * index, 90, 30),
                    Name = Placed(new TextBox { Text = AccountName(id), MaxLength = 12 }, 114, 64 + 80 * index, 224, 30),
                    Status = Placed(NewLabel("", 9, true), 114, 98 + 80 * index, 350, 26),
                    Login = Placed(NewButton("安装 / 登录"), 350, 64 + 80 * index, 128, 32)
                };
                string loginId = id;
                row.Login.Click += delegate { StartLogin(loginId); };
                SetupDialog.Controls.AddRange(new Control[] { row.Enabled, row.Name, row.Status, row.Login });
                SetupRows.Add(row);
                index++;
            }
            SetupDialog.Controls.Add(Placed(NewLabel("安装或浏览器登录完成后，点击“重新检测”。Claude 读取 Claude Code 的登录与本机会话记录。", 9, true), 20, 310, 460, 44));
            Button detect = Placed(NewButton("重新检测"), 20, 378, 120, 36);
            detect.Click += delegate { RefreshSetupStatus(); StartRefresh(); };
            Button apply = Placed(NewButton("保存"), 358, 378, 120, 36);
            apply.Name = "saveAccounts";
            apply.Click += delegate { SaveAccountSettings(); };
            SetupDialog.Controls.Add(detect);
            SetupDialog.Controls.Add(apply);
            if (previewTest) {
                foreach (SetupRow row in SetupRows) { row.Status.Text = "已连接"; row.Login.Text = "登录 / 切换"; }
                return SetupDialog;
            }
            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += delegate { foreach (SetupRow row in SetupRows) row.Status.Text = SetupStatus(row.Profile, ProfileCli); };
            try {
                RefreshSetupStatus();
                timer.Start();
                SetupDialog.ShowDialog(Popup);
                Settings.SetupSeen = true;
                SaveSettings();
            } finally { timer.Stop(); timer.Dispose(); SetupDialog.Dispose(); }
            return null;
        }

        public DpiForm RemoteDialog;

        public DpiForm ShowRemoteSettings(bool previewTest = false) {
            if (Smoke && !previewTest) return null;
            DpiForm dialog = RemoteDialog = NewDialog("远程通知与手机额度", 560, 610);
            var enabled = Placed(new CheckBox { Text = "启用跨电脑完成通知", Checked = RemoteSettings.Enabled }, 20, 14, 260, 28);
            var usage = Placed(new CheckBox { Text = "启用手机额度同步", Checked = RemoteSettings.UsageSyncEnabled }, 20, 46, 260, 28);
            var name = Placed(new TextBox { Text = RemoteSettings.DeviceName, MaxLength = 40 }, 130, 110, 400, 30);
            var key = Placed(new TextBox { Text = RemoteSettings.PairKey }, 130, 152, 200, 30);
            Button generate = Placed(NewButton("生成"), 338, 152, 86, 30);
            generate.Click += delegate { key.Text = RemoteCrypto.NewPairKey(); };
            Button copyKey = Placed(NewButton("复制密钥"), 432, 152, 98, 30);
            copyKey.Click += delegate { if (key.Text.Trim().Length > 0) Clipboard.SetText(key.Text.Trim()); };
            var summary = Placed(new CheckBox { Text = "通知中包含 Codex 最终回复摘要（最多 800 字）", Checked = RemoteSettings.IncludeSummary }, 130, 190, 400, 30);
            var relay = Placed(new TextBox { Text = RemoteSettings.RelayUrl }, 130, 228, 400, 30);
            Button copyUrl = Placed(NewButton("复制手机端地址"), 20, 398, 170, 34);
            copyUrl.Click += delegate { Clipboard.SetText(MobileUrl); };
            Button test = Placed(NewButton("发送测试通知"), 20, 490, 150, 36);
            test.Click += delegate {
                if (!RemoteSettings.Enabled || string.IsNullOrWhiteSpace(RemoteSettings.PairKey)) { MessageBox.Show("请先保存并启用跨电脑通知。", "跨电脑通知"); return; }
                File.WriteAllText(Path.Combine(DataDir, "remote-test.request"), "test");
                MessageBox.Show("测试事件已交给后台发送。另一台已配对电脑应收到通知。", "跨电脑通知");
            };
            Button save = Placed(NewButton("保存"), 400, 548, 130, 36);
            save.Name = "saveRemote";
            save.Click += delegate {
                try {
                    bool wasUsage = RemoteSettings.UsageSyncEnabled;
                    string deviceName = name.Text.Trim(), pairKey = key.Text.Trim();
                    if (deviceName.Length == 0) throw new InvalidDataException("本机名称不能为空。");
                    if ((enabled.Checked || usage.Checked) && pairKey.Length < 12) throw new InvalidDataException("启用远程功能时，配对密钥至少需要 12 个字符。");
                    string relayUrl = RemoteCrypto.RelayUrl(relay.Text);
                    var copy = new RemoteSettings { Enabled = enabled.Checked, UsageSyncEnabled = usage.Checked, DeviceId = RemoteSettings.DeviceId, DeviceName = deviceName, PairKey = pairKey, IncludeSummary = summary.Checked, RelayUrl = relayUrl };
                    if (!Smoke) copy.Save(RemoteSettingsPath);
                    RemoteSettings = copy;
                    if (copy.UsageSyncEnabled && !wasUsage) Client.TryDelete(Path.Combine(DataDir, "mobile-usage-state.json"));
                    if (LastData != null && !Smoke) RemoteWorker.WritePending(LastData, copy, Path.Combine(DataDir, "mobile-usage-pending.json"));
                    RestartRemoteWorker();
                    dialog.Close();
                } catch (Exception ex) {
                    if (Smoke) throw;
                    MessageBox.Show(ex.Message, "无法保存跨电脑通知");
                }
            };
            dialog.Controls.AddRange(new Control[] {
                enabled, usage,
                Placed(NewLabel("手机端只显示当前额度；Codex 与 Claude 登录凭证不会离开本机。", 9, true), 42, 74, 490, 28),
                Placed(NewLabel("本机名称", 9, true), 20, 112, 100, 28), name,
                Placed(NewLabel("配对密钥", 9, true), 20, 154, 100, 28), key, generate, copyKey, summary,
                Placed(NewLabel("中继地址", 9, true), 20, 230, 100, 28), relay,
                Placed(NewLabel("两台电脑填写同一配对密钥即可互相通知。消息正文会先在本机加密；默认只发送设备名、项目名、状态与时间。", 9, true), 20, 270, 510, 52),
                Placed(NewLabel("状态：" + RemoteStatusText(false), 9, true), 20, 326, 510, 28),
                Placed(NewLabel("手机同步：" + RemoteStatusText(true), 9, true), 20, 356, 510, 28),
                copyUrl, Placed(NewLabel("密钥不会加入地址；请在手机设置页手动输入。", 9, true), 204, 400, 326, 30),
                test, save
            });
            if (previewTest) return dialog;
            try { dialog.ShowDialog(Popup); } finally { dialog.Dispose(); }
            return null;
        }
    }
}
