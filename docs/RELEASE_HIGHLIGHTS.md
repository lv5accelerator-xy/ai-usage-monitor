# 版本亮点（Release highlights）

原 README 顶部与中部的各版本说明，统一 README 版式时原样移至此处。逐项变化见 [CHANGELOG](../CHANGELOG.md)。

## v2.1.0: beta channel and more reminders

- **Beta channel.** Every new version is first published as a prerelease. Only clients with **客户端更新 → 接收测试版** enabled receive it. After testing, run the **Promote release to stable** GitHub Actions workflow (or ask for it) and all other clients update automatically. Friends stay on the stable channel by default.
- **Quota recovered.** A notification when a used-up 5-hour or weekly quota becomes available again.
- **Read failures.** A notification when an account (or the whole reader) has failed for 15 minutes, with a hint for the likely cause, and another when it reconnects. Both reminders can be turned off under right-click **通知**.

## v2.0.0: native C# client

- **No PowerShell at runtime.** The floating monitor, detail panel, tray menu, account and remote-notification dialogs, the refresh lifecycle and the cross-device notification / mobile sync worker are now part of `CodexUsage.exe`. Starting the app no longer launches hidden `powershell.exe -ExecutionPolicy Bypass` processes, which security software and managed PCs often block or flag.
- **Same data, same behavior.** `profiles.json`, `ui-settings.json`, notification history, forecast history and remote settings are read as before; paired PCs and the iPhone PWA keep working (same relay topics and AES-256-CBC + HMAC-SHA256 format, verified against the shared test vector).
- **Lighter and steadier.** One .NET Framework 4.8 process for the UI plus a short-lived reader per refresh; the background worker runs as a thread instead of a second PowerShell host. Type checking at build time replaces a class of Windows PowerShell 5.1 runtime quirks.
- **Login helpers stay scripts.** "安装 / 登录" still opens the small `.bat`/`.ps1` helpers for the Codex CLI and Claude Code login, only when you click them.

## v1.1.0: forecasts and trends

- **Burn-rate forecast.** Each 5-hour and weekly quota estimates when it will run out at the current pace ("预计 15:40 用完") or confirms it lasts until reset. The compact strip turns amber before you run out, and an optional notification warns 30 minutes (5h) or 24 hours (weekly) ahead. Forecasts start after ~10 minutes (5h) / ~2 hours (weekly) of readings.
- **Codex local usage.** Today / seven-day tokens, requests, main model and an OpenAI list-price estimate from Codex's local session logs, alongside the existing Claude statistics.
- **Seven-day trend.** A small bar chart on each card; hover for the daily breakdown.

## v1.0.0: Codex and Claude in one monitor

- **Claude quota.** The Claude slot shows the subscription **5-hour** and **weekly (all models)** remaining quota with reset countdowns, the same way as Codex. Model-specific weekly windows (for example Sonnet) appear in the detail card and tooltips when Claude reports them.
- **Local Claude Code usage.** Today and the last seven days: tokens (input / output / cache write / cache read), requests, main model and an estimated cost at public API prices. Subscription users are not billed per token; the figure is a comparison aid. Hover the usage line for a daily breakdown.
- **No subscription login?** The card still shows local usage and marks quota as unavailable.
- **Setup.** Install and log in to [Claude Code](https://docs.claude.com/en/docs/claude-code/setup), then open **账号与首次使用** and enable **Claude**, or use **账号登录 / 切换 → Claude 账号** for a guided install and `/login`. Upgrades add the Claude slot automatically and enable it only if Claude Code is already used on this computer.
- **Privacy.** The dashboard reads `%USERPROFILE%\.claude\.credentials.json` (or `CLAUDE_CONFIG_DIR`) only to send one HTTPS request to Anthropic's usage endpoint. The token is never cached, logged, uploaded to the phone or refreshed. Refreshing would rotate Claude Code's refresh token and can log out running sessions, so an expired login asks you to open Claude Code once instead. Local statistics cache only token counts per message id, never prompts or responses.
- **Rate limits.** Claude quota is requested at most every two minutes. On rate limiting or network errors the last reading (up to 15 minutes old) is reused; older values are marked stale.
- **Mobile.** The iPhone PWA shows the Claude card plus aggregate today / seven-day tokens and cost.

The Claude usage endpoint is the one Claude Code itself uses for its usage view. It is not a documented public API and may change; the dashboard then shows an error for the Claude card while Codex continues to work.

## v0.8.0: encrypted iPhone quota PWA

- Adds an opt-in, independently controlled mobile usage sync that can run without completion notifications.
- Reuses the v1 AES-256-CBC + HMAC-SHA256 message format while deriving a separate non-guessable usage topic.
- Queues a strictly allowlisted snapshot atomically after desktop refresh; the background worker handles network I/O, duplicate suppression, five-minute heartbeat, and retry backoff.
- Adds a zero-dependency installable PWA with latest-message polling, EventSource updates, foreground recovery, local countdowns, freshness/offline states, and an offline app shell.
- Adds GitHub Pages deployment plus PowerShell/JavaScript compatibility and privacy regressions.

## v0.7.0: cross-device Codex completion notifications

- Adds opt-in **跨电脑 Codex 完成通知** in the tray menu. Two PCs using the same pairing key can notify each other when a persisted Codex turn finishes.
- Watches the official local Codex rollout records under `.codex*/sessions/.../*.jsonl` and reacts to persisted `event_msg / task_complete` records rather than guessing from process exit.
- Sends device name, project folder, completion/error state and finish time. The final assistant response is **off by default** and can be enabled as an optional summary (capped at 800 characters).
- Relay payloads are encrypted and authenticated locally with keys derived from the pairing secret; the public relay topic is a SHA-256-derived identifier and does not contain the secret itself.
- Uses `https://ntfy.sh` as the default relay and supports a custom HTTPS ntfy-compatible endpoint (plain HTTP is allowed only for localhost).
- Deduplicates remote events by device/turn id and keeps a bounded local notification ledger. Existing historical turns are not replayed when the watcher starts.
- Includes offline regression tests for encryption/authentication, tamper rejection, project extraction, task-complete parsing, privacy defaults and relay URL validation.

## v0.6.2: bounded memory during repeated refreshes

- Avoid Windows PowerShell 5.1 `Select-Object -First` repeatedly decorating the same account object's type metadata. Direct account lookup prevents growing type-name strings and binding caches.
- Refresh displayed countdown/status text every 15 seconds; quota requests remain once per minute, and recovery/full-screen checks remain once per second. Manual refresh results still render immediately.
- Stop worker polling when idle, avoid reparsing unchanged updater status, and unregister card handlers/tooltips before disposal.
- Windows CI checks 2,000 idle status updates plus 240 accelerated refresh cycles, retained heap/private memory growth, collectible cards, and bounded process handles. These are accelerated fixture tests, not a claim of a real multi-hour soak or a fixed RAM ceiling on every PC.

## v0.6.1: account settings save fix

Fixes the Windows PowerShell path error when saving account names or enabled accounts. Atomic replacement now uses an explicit backup path. The Windows UI test clicks the actual Save button, reloads the saved JSON, verifies account paths are preserved, and exercises repeated saves.

## v0.6.0: accounts, recovery and desktop integration

- Compact quotas have **independent colors**. Exhausted 5h does not turn a healthy long-term value red. “总量” is renamed **长周期** throughout the current UI.
- At zero 5h quota, the second line shows a live **estimated recovery countdown**. Hover for the exact local reset timestamp. Once the reported reset is due, the UI requests one immediate refresh per account/reset, while regular one-minute polling continues. Missing and stale values never claim replenishment.
- **账号与首次使用** opens automatically on first run and is available from the right-click menu. Enable one or two account slots and edit display names (up to 12 characters). Disabled accounts are hidden and are not queried. Names and enablement are saved atomically in profiles.json; existing profile paths and login data are retained.
- Setup distinguishes missing CLI, missing login, existing credentials awaiting verification, and a fresh successful connection. “安装 CLI” / “登录 / 切换” opens the existing guided setup; “重新检测” rechecks installation and requests a quota refresh. A credential file alone is never labelled connected.
- **开机启动** and **全屏时隐藏悬浮窗** are opt-in, off by default. Startup uses the current user's Run registry entry and is removed on uninstall. Full-screen detection is scoped to the monitor containing the floating window; returning from the full-screen application restores the user's visibility preference.
- The native window layer handles per-monitor DPI changes, rescales controls, and periodically moves off-screen windows into a connected display's working area. Automated tests cover scaling roundtrips and negative-coordinate monitor geometry; mixed-DPI physical monitors still warrant real-device testing.
- Background client downloads show percentages and distinguish network, integrity, permission and disk errors. “立即检查更新” retries after failure; the installed version remains intact.
- The EXE uses a **C# quota reader** with bounded RPC waits, continuously drained stderr, independent per-account errors, and a Windows job object to clean up server children. The source/script launcher retains its PowerShell reader as a fallback. Native-reader tests talk to a local fixture server without account access.

This release advances the C# migration; it does not remove the Windows PowerShell runtime requirement for the UI and login workflow.

