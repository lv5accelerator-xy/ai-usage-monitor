# 使用说明（User guide）

原 README 中的客户端、手机端与界面细节说明，统一 README 版式时原样移至此处。

## Windows EXE client (recommended)

[Download the latest CodexUsage.exe](https://github.com/lv5accelerator-xy/ai-usage-monitor/releases/latest/download/CodexUsage.exe)

Double-click the EXE and select **安装并启动** in the installation window. It installs into `%LOCALAPPDATA%\CodexUsage` and creates desktop and Start menu shortcuts without administrator permissions. The client is a native .NET Framework 4.8 WinForms application (built into Windows 10/11); it does not require a separate .NET 8 or Electron runtime. Since v2.0 the UI and background worker are C#; only the optional login helpers use Windows PowerShell.

**Upgrading from the script version:** quit the old tray app, put the EXE in the old app folder, and run it once. If present, `ui-settings.json` and `profiles.json` are copied into the client's separate data directory. Existing `.codex-personal` / `.codex-work` login credentials stay where they are; no credentials are bundled or uploaded.

### Sharing and uninstalling

[中文安装与分享指南](FRIENDS.md) — share the release link with friends; each person logs in with their own account. Do not share local account files or logs.

The client is listed as **Codex 额度** in Windows Settings → Apps. Quit the tray app before uninstalling. Uninstall removes program files and shortcuts while preserving settings and Codex account directories for reinstallation. No administrator privileges are needed.

History: v0.5–v1.1 migrated the reader, updater, window integration and drawing controls to C# step by step; v2.0 completes the migration for the UI and worker. An unsigned EXE can still trigger SmartScreen reputation prompts.

Releases include `READ-ME.txt` and `SHA256SUMS.txt` alongside the EXE and update manifest. Checksums verify consistency, not publisher identity.

### Automatic client updates

- New versions are published as **prereleases** first and reach only clients with **客户端更新 → 接收测试版** enabled. The **Promote release to stable** workflow (Actions → Run workflow, optional tag) marks a tested prerelease as the latest stable release; stable clients then update as usual. The promoted binary is identical to the tested one.

- Checks the repository's latest stable GitHub Release after startup and every six hours. This is background polling, not server push.
- Downloads newer versions automatically and verifies the size, SHA-256 checksum, and embedded executable version.
- Right-click **客户端更新 → 重启并更新** to apply a downloaded update immediately. Normal exit also installs it; the next launch uses the new version.
- Right-click **客户端更新 → 立即检查更新** to check manually.
- Installation uses an atomic file replacement and retains the previous EXE. A failed post-update UI startup attempts to restore the previous version.
- Account configuration, UI preferences and notification history are kept under `%LOCALAPPDATA%\CodexUsage\data`, outside the versioned program files. They survive updates.
- If offline or a download fails verification, the running version remains available. Client logs are in `%LOCALAPPDATA%\CodexUsage\client.log`.

The release EXE is **unsigned**; Windows may show a publisher/reputation prompt. SHA-256 checks provide download integrity, not publisher signing. Notifications remain subject to Windows notification settings.

### Building and releasing

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File desktop/build.ps1` on Windows to create `dist/CodexUsage.exe` and `dist/update.json`.

GitHub Actions validates the source UI, builds the EXE, runs the packaged UI plus migration/integrity tests, and publishes a GitHub Release when `VERSION` is new. Published version assets are immutable: increment `VERSION` for each release. No developer API key or signing secret is required for the current unsigned build.

[Icon design and source assets](../assets/README.md)

## iPhone / Mobile Dashboard

v0.8.0 adds **Codex Usage Mobile**, a small installable PWA for viewing Personal and Work quota from an iPhone. Windows remains the only component that reads Codex. It publishes an allowlisted usage snapshot through a separate encrypted ntfy topic; the browser authenticates and decrypts it locally.

### First-time setup

1. On Windows, right-click Codex 额度 and open **远程通知与手机额度**.
2. Check **启用手机额度同步**, generate or enter a pairing key, save, and copy the pairing key.
3. In this repository, enable GitHub Pages with **Settings → Pages → Source → GitHub Actions**. The expected site is `https://lv5accelerator-xy.github.io/ai-usage-monitor/`.
4. Open that HTTPS address in iPhone Safari, enter the relay address and pairing key, then tap **连接**.
5. In Safari choose **分享 → 添加到主屏幕 → 添加**. Open **Codex 额度** from the new icon.

The iPhone never receives Codex `auth.json`, access/refresh tokens, cookies, account IDs, Codex profile paths, session logs, or conversation content. The pairing key is stored in `localStorage` only when **在此设备记住配对密钥** is selected; otherwise it is kept for the current browser session only. It is never placed in the URL. If a phone is lost, generate a new pairing key on Windows and save it so the old phone cannot decrypt new snapshots.

[面向非开发人员的 iPhone 使用方法](IPHONE.md) · [Mobile security review](MOBILE-SECURITY.md) · [PWA developer notes](../mobile/README.md)

The mobile refresh button fetches the latest relay snapshot. It does not remotely command the Windows computer or log in to Codex. The PWA shell works offline, but quota snapshots are not persisted by the service worker.


## Frosted glass (v2.2.0; real blur since v2.3.0)

The floating monitor is a real frosted-glass pane: its dark background is translucent and Windows blurs the desktop behind it (Windows 10 version 1803 or later, Windows 11), while text, numbers and rings stay fully opaque. The monitor paints itself, frame and contents, into a 32-bit image with real transparency and hands it to Windows (`UpdateLayeredWindow`). Right-click **磨砂玻璃效果** (on by default) to switch to an opaque dark background. On systems without blur support the background is simply translucent.

The detail panel uses the same colours, rounded cards, capsule bars and buttons but stays opaque: it is built from standard Windows controls, which cannot safely sit on a blurred background (v2.2.0 tried, and cards turned white with haloed text; v2.2.1 removed that blur).

## Follow the light (v2.5.0)

- **Light rim.** Hovering over the floating monitor lights its rim with a travelling blue-violet-amber light; in ring mode the ring under the pointer gets its own lit cell. A manual refresh ends with one cool sweep; a quota newly dropping to 10% or below triggers one warm (orange-red) sweep.
- **Breathing rings.** While quotas are being read, the ring arcs breathe and a short comet circles each track.
- **Skeletons.** Until the first read finishes, rings and the compact strip show shimmering placeholders instead of dashes.
- **Chart switch.** In each card's local-usage block, the **7 天用量 / 额度曲线** switch toggles every card between the seven-day token wave bars (rising one after another, today's bar glowing) and a heartbeat line of the 5-hour window's remaining quota over the last five hours, with a pulsing dot on the latest read. The choice is saved.
- **Toast cards.** Notifications appear as the app's own cards sliding in at the bottom-right (left of the monitor if it is there), with a lit rim (warm for warnings). They never take keyboard focus, stay while hovered, fade after about seven seconds, and open the detail panel when clicked; at most three show at once. Over full-screen apps the quieter Windows notification is used instead.
- **Elastic slider.** The **紧凑小窗透明度** slider's thumb stretches while dragged, rubber-bands past either end and springs back on release.

Animation runs only while something moves (hover, reading, a sweep, an open panel); otherwise the monitor is static.

## Compact transparency (v0.4.2): adjustable compact transparency and one-minute refresh

The monitor defaults to **清晰模式（紧凑条不额外透明）** for readable text. Disable this right-click option to restore the saved transparency: **紧凑小窗透明度** selects **0–60% transparency**, initially 20%. Clear mode disables the slider and preserves its saved value. Windows opacity affects the entire window, including text; the expanded monitor and detail panel remain fully opaque (the glass background of the monitor is translucent per pixel regardless). **悬浮窗大小 → 小 / 中 / 大** selects a preferred 100%, 120%, or 140% size independently of monitor DPI, including the rings, compact strip and expanded monitor. If a small display cannot fit that size, the window scales down to its working area while keeping the saved preference. Both preferences survive restarts.

Automatic quota refresh now runs every **60 seconds**. If a read is still running, the next tick is skipped rather than starting an overlapping worker.

The packaged native reader runs up to two account reads concurrently, with independent RPC timeouts and per-account success/error results. Each completed account is displayed immediately while the other keeps its last successful values; hover over a quota or account to see that account's own update time. The UI rejects progress from an outdated account configuration. Display-change and resume messages restore visible window placement; resume cancels an obsolete reader and requests fresh data. Automated fixtures cover simulated mixed-DPI displays, disconnection, resolution reduction, resume and persisted restart placement. Physical mixed-DPI multi-monitor testing remains a separate device check.

## Compact mode, snapping and alerts (v0.4.1): compact mode, edge snapping, optional alerts

The monitor now starts as a **360 × 64 logical-pixel strip** (220 pixels wide with one enabled account). Each account shows **5h remaining / long-term remaining** as two independent percentages. “Long-term” means the limiting long-term quota (weekly or meaningful monthly/workspace quota), not the sum of windows. When 5h reaches zero, the second line shows the estimated time until recovery; the tooltip shows the exact local timestamp. Missing reset times remain unknown; expired reset times wait for a fresh reading; stale data asks for confirmation. Hover to expand the full 5-hour/long-term view; move away for about half a second to collapse. It stays expanded while the detail panel or context menu is open, and never changes size during a drag. Right-click **紧凑模式（悬停展开）** to keep the full monitor visible instead.

**贴边吸附** is enabled by default. Release the monitor within 20 logical pixels of a screen's working-area edge to snap to it. Taskbars and negative-coordinate monitors are supported. A bottom-docked strip expands upward and returns to the same resting position. Drag away to detach, or turn snapping off in the right-click menu.

**低额度通知 → 启用通知** is off by default. Threshold presets are **20% / 10%**, **10% only**, or **30% / 15%**. Notifications identify the account and quota window; clicking a notification opens the detail panel. Windows notification settings / Do Not Disturb may suppress delivery.

- Only fresh, successful reads crossing a threshold can notify. Failed, missing, stale, or already-expired quota data never triggers an alert.
- The first successful sample after startup, enabling notifications, or changing thresholds establishes a baseline without notifying.
- Each threshold is notified at most once per account and quota window/reset cycle. Crossing two thresholds in one read produces one message for that window.
- The deduplication ledger is retained locally in `logs/notification-state.json` across restarts. No credentials are stored in it.
- A new reset timestamp starts a new cycle. If no timestamp is available, recovery above all configured thresholds rearms alerts. A gap of ten minutes or more establishes a new comparison baseline without notifying.
- Compact/expanded preference, snapping, resting position, and notification preferences are saved in `ui-settings.json`. Collapsed values marked **!** are stale or unavailable; hover for details.


## Edge ring mode

Right-click **贴边圆环（点击查看详情）** to show each enabled account's **5h** and **long-term** remaining quota as separate rings. Rings always form one vertical column, even at the top/bottom edge. The strip is 60 logical pixels wide; each ring is 40 pixels across. Account names and quota-window labels occupy separate lines, making the strip 328 logical pixels tall for two accounts or 168 for one. The narrow width is preserved on first show, and ring text scales once with monitor DPI. Drag the group to any edge. Click a ring to open details, hover for its full account name, freshness information and the displayed quota's reset time, or right-click to switch back. The mode and position survive restarts. Compact opacity also applies to rings.

Since v2.4.0 a docked ring strip auto-hides: it tucks into the screen edge leaving a thin strip visible, slides out when the pointer touches that strip (or while the right-click menu or the detail panel is open), and tucks back about half a second after the pointer leaves. Turn this off with **圆环贴边自动隐藏（鼠标碰到滑出）**. A floating (undocked) strip, or one whose edge borders another monitor or the taskbar, never hides.

Missing values display **—**, positive quota below 1% displays **<1%** across desktop and mobile views, exhausted quota displays **0%**, and stale values are muted and marked **!**. Ring mode stays compact while hovering. It can stay anywhere in the working area. When **贴边吸附** is enabled, releasing within 20 logical pixels of any edge snaps to that edge; drag away to detach. Disable **贴边吸附** for fully free placement.

v0.9.0 also includes the Windows remote-worker HOME-variable fix from PR #4. Install and restart the new desktop build to apply it; a successful build alone does not verify real iPhone synchronization.

## Floating monitor

Start **Codex 额度** from the desktop shortcut to open a compact rounded panel. Aligned columns show the remaining **5-hour** and **long-term** quotas for each account, with a visible update timestamp or stale-data warning.

| Account | 5-hour remaining | Long-term remaining |
| --- | ---: | ---: |
| Personal | 100% | 95% |
| Work | 49% | 21% |

*Example values, not live account data.*

- **长周期 (long-term)** is the lower remaining percentage of Weekly and a meaningful monthly/workspace limit. It is not a sum or a shared balance. Hover over it to see the source and individual values.
- **—** means the quota was not provided; **0%** means the reported quota is exhausted.
- Drag anywhere on the monitor to move it; click to expand/collapse the detail panel.
- Right-click for refresh, visibility, always-on-top, reset position, account login/switch, logs, and exit.
- Low remaining values are highlighted individually: amber at 35% or below, red at 15% or below. Old/unknown values are muted.

## Detail panel

The movable, resizable panel uses a neutral dark theme, consistent Chinese typography, larger primary numbers, rounded quota bars, and direct account login actions. All percentages represent **remaining** quota.

Quota order:

1. 5-hour remaining
2. Workspace/monthly remaining, when a meaningful limit is provided
3. Weekly remaining

Hover over reset times for the exact local timestamp and monthly used/limit details. Smaller windows scroll vertically; refreshes preserve the scroll position. Layout dimensions scale with the Windows system DPI when the app starts. The native window layer handles per-monitor DPI changes; physical mixed-DPI monitor combinations have not all been tested.

## Refresh and data freshness

- Refresh runs silently every **1 minute** and can also be triggered manually.
- Both manual and automatic failures retain the last successful values and show **数据未更新**.
- One account can continue updating while the other retains its last successful data. Profiles are matched by ID, not array order.
- Data older than ten minutes is marked stale even if there is no explicit refresh error.
- A read taking longer than 150 seconds is timed out so refresh does not remain permanently disabled.
- Retained values are in-memory only. Restarting the app waits for a fresh read.

## Persistent UI settings

`ui-settings.json` remembers monitor position, detail-panel position/size, visibility, and always-on-top preference. Existing v0.3.x settings remain compatible. Settings are ignored by Git.


## Security

Authentication stays in the user's local Codex profile directories. Never commit login credentials. Logs, runtime results, remote settings, pending mobile snapshots, UI preferences, test screenshots, and local Codex profiles are ignored by Git. The public Pages bundle contains only the app shell and no pairing key, topic, account data, or credentials.

## GitHub ZIP downloads

Extract **Code → Download ZIP** completely, then run `start.bat`. It compiles `dist\CodexUsage.exe` with the C# compiler that ships with .NET Framework 4.8 and starts the installer. Downloading the release EXE is simpler and receives automatic updates.
