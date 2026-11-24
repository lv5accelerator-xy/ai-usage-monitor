# Changelog

## 2.2.0 - 2026-11-24

- 界面改为苹果风格的磨砂玻璃质感：悬浮窗与额度面板半透明，背后的桌面被系统模糊（Windows 10 1803+ / Windows 11）；配色改用 macOS 深色模式的系统色，卡片与悬浮窗使用大圆角、细描边和顶部高光。
- 额度条改为圆角胶囊，贴边圆环改为连续的圆头弧线，七日趋势改为圆角柱；数字改用 Segoe UI Semibold。按钮改为胶囊样式，右键菜单使用圆角蓝色高亮（Windows 11 上菜单与窗口为系统圆角），标题栏与滚动条跟随深色。
- 右键新增 **磨砂玻璃效果**（默认开启）；关闭后两个窗口恢复完全不透明。**清晰模式** 现在表示紧凑条不再叠加额外透明度。
- iPhone 网页同步换成玻璃材质：毛玻璃卡片、彩色柔光背景、胶囊按钮与 iOS 系统色。

## 2.1.2 - 2026-11-23

- 仓库改名为 `lv5accelerator-xy/ai-usage-monitor`：客户端的更新源、"版本说明 / 下载客户端"、"使用指南"和手机额度网址都改用新地址。
- 2.1.1 及更早的客户端会因为下载地址与旧仓库名不一致而停止自动更新，需要手动下载一次本版 [CodexUsage.exe](https://github.com/lv5accelerator-xy/ai-usage-monitor/releases/latest/download/CodexUsage.exe)；之后照常自动更新。设置、账号和通知记录保留。
- iPhone 手机额度网页地址改为 `https://lv5accelerator-xy.github.io/ai-usage-monitor/`，需要在 Safari 打开新地址后重新"添加到主屏幕"并输入配对密钥。

## 2.1.1 - 2026-11-16

- Fewer notifications: the read-failure reminder no longer repeats when reads flap (e.g. the Claude usage endpoint rate-limiting while you are idle, then one good read, then failing again). An account is reported at most once every 12 hours, and **额度读取已恢复** only appears after 10 minutes of successful reads.
- Rate limiting (429) is no longer treated as a read failure; it clears by itself.
- Read-failure reminders wait while you are away from the computer (no keyboard or mouse input for 10 minutes) and are shown when you are back if the problem persists.
- **额度已恢复** fires at most once every 3 hours per quota window; the burn-rate early warning tolerates reset times that drift by seconds, so one reset cycle warns once.
- Every desktop notification is written to the log, which makes any remaining noise easy to trace.

## 2.1.0 - 2026-11-16

- Beta update channel: right-click **客户端更新 → 接收测试版（先于朋友更新）**. New versions are published as GitHub prereleases and reach only beta clients; the **Promote release to stable** workflow turns a tested prerelease into the latest release for everyone (same binary, no rebuild). Stable clients keep using `releases/latest`, which never returns prereleases.
- The updater re-reads the channel before every check; switching channels triggers an immediate check. Switching back to stable never downgrades.
- New reminders under right-click **通知** (both on by default, independent of the low-quota switch): **额度恢复提醒** when a 5-hour or weekly quota that was used up replenishes, and **读取异常提醒** when an account (or the whole reader) has failed for 15 minutes, with a recovery hint such as re-running Claude Code after an expired login; a follow-up notice confirms reconnection.
- The low-quota menu is renamed **通知 → 低额度提醒**.

## 2.0.2 - 2026-11-16

- When there is no local session in the last seven days, the local-usage line no longer shows "0 tokens · $0.00" with an empty chart. The Claude card explains that cloud sessions (Claude app / web) are not written to local logs and that the 5-hour / weekly quota is the real usage figure; Codex cards say there is no local session. Applies to the desktop card, tooltips and the iPhone PWA.

## 2.0.1 - 2026-11-16

- Local Codex usage now includes sessions from the Codex desktop app, which keeps every account's sessions in `~/.codex/sessions`. Previously only the per-account homes (`~/.codex-personal`, `~/.codex-work`) were scanned and the figure stayed at 0. Each session is assigned to its account by `creator_account_id`; only the account id is read from `auth.json`.

## 2.0.0 - 2026-11-16

- The UI is now native C#: floating monitor (compact / expanded / rings), detail panel, tray menu, account setup and remote-notification dialogs, refresh lifecycle, low-quota and forecast notifications, full-screen hiding, DPI and display recovery. `tray.ps1`, `ui-experience.ps1`, `remote-ui.ps1`, `ui-model.ps1`, `ui-behavior.ps1` and `launcher.ps1` are retired.
- The cross-device completion notification and mobile sync worker runs as a background thread inside the client instead of a hidden PowerShell process. Relay topics, encryption (AES-256-CBC + HMAC-SHA256) and the file protocol are unchanged and verified against the shared cross-runtime vector, so paired PCs on v1.x and the iPhone PWA keep working.
- The installed client launches its UI as a second instance of `CodexUsage.exe` (`--ui`); update checks, staged installs and rollback after a failed start are unchanged. No PowerShell process is started unless you open a login helper.
- Existing `profiles.json`, `ui-settings.json`, notification ledger, forecast history and remote settings are read in place; no migration step is required.
- The payload now contains only login helpers, `profiles.json` and the icon; drawing controls are compiled into the EXE.
- Tests: model, alert, forecast, snapping, settings, mobile snapshot, crypto and background-worker rules run offline against the C# code, including an end-to-end worker run with a fake relay; the native UI smoke test runs the real EXE (`--ui-smoke`) in CI and in the packaged self-test.
- Fixes the bottom-docked resting position calculation for three visible accounts.
- `start.bat` in a source checkout now builds and starts the native EXE.

## 1.1.0 - 2026-11-16

- Burn-rate forecast for every 5-hour and weekly quota: a least-squares rate over recent fresh readings in the current reset cycle (last hour for 5h, last 12 h for weekly). Cards show "预计 HH:mm 用完" or "按当前速度可用到重置"; the compact strip warns before the 5h or weekly quota runs out; tooltips show the rate in %/hour. Forecasts need about 10 minutes (5h) or 2 hours (weekly) of readings and never mix reset cycles.
- Optional early-warning notification (with low-quota notifications enabled): 5h quota projected to run out within 30 minutes, weekly within 24 hours; once per window and reset cycle.
- Forecast history (timestamps and percentages only) persists locally in `logs/usage-history.json` across restarts.
- Local Codex usage from `<CODEX_HOME>/sessions/**/rollout-*.jsonl`: per-call `last_token_usage`, repeated events and forked copies counted once, model taken from `turn_context`, cached input split out, OpenAI list-price cost estimate. Incremental reads with an aggregate-only cache per account.
- Seven-day token trend chart on Codex and Claude cards (today highlighted); tooltips list the daily breakdown.
- iPhone PWA shows local usage for Codex accounts as well.

## 1.0.0 - 2026-11-16

- Add a Claude account slot next to the two Codex accounts. It reads the Claude subscription 5-hour and weekly (all-model) quota from Claude Code's own login, plus model-specific weekly windows (Sonnet / Opus) when Claude reports them.
- Add local Claude Code usage statistics from `~/.claude/projects/**/*.jsonl`: today and seven-day token totals (input / output / cache write / cache read), request count, main model, a daily breakdown and an API-price cost estimate. Streaming duplicates and messages copied into resumed sessions are counted once; files are read incrementally from a cached offset.
- Without a Claude subscription login, the Claude card still shows local usage; quota is shown as unknown, never as 0% or 100%.
- Claude credentials are read-only: the access token is used for one HTTPS request and never cached, logged or refreshed (refreshing would rotate Claude Code's refresh token). Expired logins and rate limits are reported with recovery hints.
- Respect the usage endpoint's rate limits: reuse a two-minute quota cache and fall back to a recent (15 min) cache on 429 / network / server errors; older data is marked stale as before.
- A missing Codex CLI now fails only Codex accounts; Claude keeps updating.
- Floating monitor, compact strip, rings, detail panel, account setup, tray login menu and notifications support three accounts. Older `profiles.json` files gain a Claude slot automatically, enabled only when Claude Code is already used on the computer.
- iPhone PWA shows the Claude card and aggregate local usage; unknown future account ids are skipped instead of rejecting the snapshot. The service worker cache is versioned for the update.
- Add a guided `login-claude.bat` that installs Claude Code with Anthropic's official command when missing and opens it for `/login`.
- Add offline Claude reader, model, mobile and packaged self-test coverage.

## 0.9.4 - 2026-10-13

- Preserve positive quota below 1% as `<1%` in desktop and mobile views, including the mobile transport; missing and invalid values stay unknown.
- Add opaque clear mode and small/medium/large monitor sizes, with persisted preferences and consistent size/DPI rounding.
- Fit oversized monitors to the connected display's working area without changing the saved size preference; keep docked edges anchored when changing size.
- Run up to two native account readers concurrently and publish each result independently; retain pending-account data and expose per-account update timestamps.
- Recover visible placement after display changes and resume, refresh after resume, and preserve the actual monitor dimensions when restoring saved ring positions.
- Add native size/DPI, display-removal, resume, restart, partial-result and bounded-concurrency regression fixtures; physical mixed-DPI display verification remains separate.

## 0.9.3 - 2026-10-13

- Preserve the borderless ring strip's requested width before its first frame; Windows previously widened it to the minimum tracking width on startup.
- Split account and quota-window captions into two lines while keeping the strip 60 logical pixels wide.
- Use pixel fonts for already-scaled ring geometry to avoid applying monitor DPI twice.
- Show the selected weekly/monthly quota's reset time in long-term ring tooltips, with first-show and DPI regression coverage.

## 0.8.0 - 2026-09-28

- Added the installable Codex Usage Mobile PWA for iPhone, iPad, and desktop browsers.
- Added an independent `usageSyncEnabled` setting, separate hashed usage topic, allowlisted mobile display model, atomic pending snapshot, duplicate suppression, five-minute heartbeat, and bounded network retry backoff.
- Extracted the existing completion-message cryptography into a shared PowerShell module without changing its topic or AES-256-CBC + HMAC-SHA256 wire format.
- Added Web Crypto authentication/decryption, ntfy latest polling and EventSource updates, foreground reconnect, local reset countdowns, stale/offline states, and a non-sensitive offline shell.
- Added GitHub Pages deployment, Chinese iPhone setup documentation, a threat review, and cross-runtime PowerShell/JavaScript security fixtures.

## 0.7.1 - 2026-09-22

- Fixed Codex CLI auto-install on Windows PowerShell 5.1 when OpenAI's official installer response is returned as a byte array.
- Explicitly decodes byte-array and stream responses as UTF-8 before executing the official installer script.
- Added regression coverage that verifies installer content beginning with `[CmdletBinding()]` is not converted into decimal byte values such as `91 67 109...`.


## 0.7.0 - 2026-09-22

- Added opt-in cross-device notifications for completed Codex turns.
- Detects persisted Codex `task_complete` events from local rollout JSONL sessions instead of relying on process exit.
- Added encrypted/authenticated relay messages, hashed pair topics, per-device identities, event deduplication and optional final-response summaries.
- Added a tray configuration dialog with pairing-key generation, custom HTTPS relay support and test notifications.
- Added offline regression coverage for encryption, tamper detection, task parsing, privacy defaults and relay validation.


## 0.5.0 - 2026-09-17

- Added a single Windows EXE with an embedded UI payload, per-user installation, and desktop/Start menu shortcuts.
- Added background stable-release checks, automatic verified downloads, restart-to-update actions, exit-time installation, and a previous-version rollback copy.
- Separated client data from versioned application files and migrated adjacent legacy settings/account configuration on first install.
- Added the dual lavender/mint quota icon to the EXE, shortcuts, window, and system tray.
- Added Windows client build, packaged native UI testing, update integrity/migration tests, and automated versioned GitHub Releases.

## 0.4.2 - 2026-09-17

- Made the compact strip 20% transparent by default, with a persistent 0–60% transparency slider in the right-click menu.
- Restored full opacity while expanded to keep quota details readable.
- Changed automatic quota refresh from five minutes to one minute, preserving the no-overlap worker guard.
- Added native opacity-slider and refresh-interval regression checks.

## 0.4.1 - 2026-09-17

- Added a compact two-account strip with hover expansion and delayed collapse; dragging and open menus never trigger a size change.
- Added optional edge snapping within 20 logical pixels of each monitor's working area, with stable bottom-edge expansion and persistent resting positions.
- Added opt-in Windows low-quota notifications with 20/10, 10-only, and 30/15 percent threshold presets.
- Notifications use only fresh successful reads, establish silent initial baselines, coalesce threshold crossings, and persist per-account/window/reset-cycle deduplication across restarts.
- Added regression coverage for negative monitor coordinates, stale/failed/missing data, threshold crossing, reset cycles, and JSON-restored notification state.
- Extended Windows native smoke tests for compact rendering, hover, drag guards, edge anchoring, and notification menu preferences.

## 0.4.0 - 2026-09-16

- Replaced the crowded circular monitor with a rounded, DPI-scaled two-account panel with aligned 5-hour and long-term quota columns.
- Renamed the ambiguous total label to 长周期 and added tooltips identifying the limiting weekly or meaningful monthly/workspace quota.
- Refined the detail panel with neutral dark surfaces, unified Chinese typography, prominent remaining values, rounded bars, and account-level login actions.
- Preserved last successful data on both manual and automatic refresh failures, including independent account failures matched by account ID.
- Added visible stale-data states to the floating panel and detail cards, a ten-minute freshness check, and a refresh timeout.
- Preserved scrolling across refreshes and moved account content into layout containers with system-DPI scaling.
- Integrated display behavior into the checked-in UI and removed the runtime v0.3.9 source patch.
- Added quota-state regression tests and a Windows PowerShell 5.1 native UI smoke test with fixture screenshots.

## 0.3.9 - 2026-09-16

- Floating monitor now shows the 5-hour quota before the longer-window/total quota for both Personal and Work accounts.
- Floating format changed to `5H / 总`, with separate Personal and Work rows.
- `总` uses the meaningful long-window quota: Weekly, plus a valid workspace/monthly limit when available.
- Enlarged the floating monitor slightly so both values remain readable while keeping the circular floating style.
- Reordered the detailed Work card to show 5-hour first, workspace/monthly total second, and Weekly third.
- Kept the 5-minute silent automatic refresh and saved position/size behavior.

## 0.3.8 - 2026-09-16

- Replaced the always-open dashboard workflow with a desktop floating ball plus on-demand detail panel.
- Floating ball shows Personal and Work remaining quota summaries and changes accent color as the lowest remaining quota drops.
- Added drag-and-drop positioning for the floating ball.
- Made the detailed quota panel freely resizable and movable.
- Added persistent `ui-settings.json` storage for floating-ball position, panel position/size, visibility, and always-on-top preference.
- Added right-click actions for refresh, show/hide floating ball, always-on-top, reset position, account login/switch, logs, and exit.
- Kept the 5-minute automatic refresh, but automatic refresh is now silent and preserves the current panel while new data is loaded.
- Rebuilt the panel with a two-row layout so the fixed header can never cover the Personal account card.
- Simplified `launcher.ps1`; it now normalizes encoding and launches the checked v0.3.8 source directly instead of applying runtime UI patches.

## 0.3.7 - 2026-09-16

- Fixed the Personal account card being rendered underneath the fixed header.
- Replaced the quota content area's `Dock=Fill` layout with an explicit area below the 58px header.
- Increased the minimum client height so both account cards fit without the header covering the first card.
- Kept the existing automatic refresh interval at 5 minutes.

## 0.3.6 - 2026-09-16

- Fixed `Path.GetFullPath` startup failure in `launcher.ps1` on Windows PowerShell 5.1.
- Removed the invalid use of `$MyInvocation.MyCommand.Path` from inside the normalization function.
- Replaced `Start-Process` startup with `System.Diagnostics.ProcessStartInfo`.
- Added a PowerShell syntax check for `tray.ps1` before launch.
- Kept automatic UTF-8 BOM normalization for GitHub ZIP downloads.

## 0.3.5 - 2026-09-16

- Fixed silent startup exits after downloading the repository from GitHub.
- Removed VBS/Windows Script Host from the primary startup path.
- Added `launcher.ps1` startup watchdog and `startup-error.log` diagnostics.
- Normalized GitHub-downloaded PowerShell source files locally for Windows PowerShell 5.1 before launch.
- Updated `start-debug.bat` and version metadata.

## 0.3.4
- Added dark technology-style dashboard UI.
- Fixed initial scroll position so the Personal card is fully visible.
- Added adaptive popup height while preserving scrolling for smaller displays.
- Refined quota colors, typography, cards, and refresh button styling.

## 0.3.3
- Fixed PowerShell `$HOME` read-only automatic-variable collision during result rendering.
- Added checks for other reserved PowerShell automatic variables.

## 0.3.2
- Fixed ambiguous `System.Drawing.Font` overload on Windows PowerShell 5.1.

## 0.3.1
- Reworked startup so the tray initializes before quota I/O.
- Moved account reads into an independent background worker.
- Added persistent tray/worker logs.

## 0.3.0
- Removed Node.js dependency.
- Rebuilt the application as a PowerShell/WinForms tray utility.

## 0.2.x
- Improved Codex CLI discovery and Windows login workflow.
- Added Personal and Work account separation.

## 0.1.0
- Initial dual-account quota dashboard prototype.
