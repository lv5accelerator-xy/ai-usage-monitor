# Codex 额度：Codex + Claude 用量悬浮监控

[![Validate](https://github.com/lv5accelerator-xy/ai-usage-monitor/actions/workflows/validate.yml/badge.svg?branch=main)](https://github.com/lv5accelerator-xy/ai-usage-monitor/actions/workflows/validate.yml)

Windows 桌面悬浮窗，同时显示两个 ChatGPT/Codex 账号（个人 + 工作）与一个 Claude 账号的 5 小时 / 每周剩余额度、预计用完时间和本机用量统计；可选的 iPhone 网页同步查看。原生 .NET Framework 4.8 客户端，无需管理员权限。

**[▶ 下载最新版 CodexUsage.exe](https://github.com/lv5accelerator-xy/ai-usage-monitor/releases/latest/download/CodexUsage.exe)** · [iPhone 手机额度](https://lv5accelerator-xy.github.io/ai-usage-monitor/) · [安装与分享指南](docs/FRIENDS.md)

## 功能

- **额度悬浮窗**：每个账号的 5 小时与长周期剩余百分比，紧凑条 / 展开面板 / 贴边圆环三种形态，支持贴边吸附与透明度调节。
- **预计用完时间**：按当前消耗速度估算“预计 15:40 用完”，快用完前可提前通知。
- **本机用量**：Codex 与 Claude Code 今日 / 七日的 tokens、请求数、主要模型与按公开价估算的费用，附七日趋势。
- **提醒**：低额度、额度恢复、读取异常与重新连上的 Windows 通知，均可单独关闭。
- **跨设备**：两台电脑互相推送 Codex 任务完成通知；加密同步额度快照到 iPhone 网页。
- **自动更新**：启动后与每 6 小时检查 Release，校验大小、SHA-256 与版本后安装，失败自动回滚；可选测试版通道。

各界面与设置的详细说明见[使用说明](docs/USER_GUIDE.md)。

## 当前版本

<!-- CURRENT_VERSION_START -->
**v2.3.0**
悬浮窗重画为真正的磨砂玻璃：背景半透明并模糊背后的桌面，文字保持清晰不透明；右键 **磨砂玻璃效果** 可关闭。额度面板为不透明深色，iPhone 网页为玻璃材质。
<!-- CURRENT_VERSION_END -->

完整更新记录见 [CHANGELOG](CHANGELOG.md)，各版本亮点见[版本亮点](docs/RELEASE_HIGHLIGHTS.md)。

## 安装

1. 下载 [CodexUsage.exe](https://github.com/lv5accelerator-xy/ai-usage-monitor/releases/latest/download/CodexUsage.exe)，双击后选择 **安装并启动**。程序装到 `%LOCALAPPDATA%\CodexUsage`，并创建桌面和开始菜单快捷方式。
2. 首次启动会打开 **账号与首次使用**：启用需要的 Codex 账号槽位与 Claude，按提示安装 Codex CLI / Claude Code 并登录。
3. 卸载在 Windows 设置 → 应用 → **Codex 额度**；设置与账号目录保留，方便重装。

系统要求：Windows 10/11（自带 .NET Framework 4.8）；Codex 账号需要 Codex CLI，Claude 账号需要 Claude Code；Windows PowerShell 5.1 只用于可选的登录助手。EXE 未签名，首次运行可能出现 SmartScreen 提示。

从源码运行：执行 `first-run-setup.bat` 设置并登录两个独立的 Codex 目录（`%USERPROFILE%\.codex-personal`、`%USERPROFILE%\.codex-work`），再运行 `start.bat` 编译并启动 `dist\CodexUsage.exe`。

## 隐私与安全

- 登录凭据只留在本机的 Codex / Claude 目录，不打包、不上传、不写入日志；Claude 令牌只用于向 Anthropic 用量接口发一次请求，不缓存、不刷新。
- 手机端只收到白名单内的额度快照，经本机 AES-256-CBC + HMAC-SHA256 加密后转发；配对密钥不进入网址。见[手机端安全评估](docs/MOBILE-SECURITY.md)。
- Claude 用量接口是 Claude Code 自身使用的接口，不是公开文档化的 API，可能变化；届时只有 Claude 卡片报错，Codex 照常工作。

## 仓库结构

| 路径 | 内容 |
| --- | --- |
| `desktop/` | C# 客户端：安装与更新（`Client.cs`）、悬浮窗与面板（`TrayApp*.cs`）、额度规则（`AppModel.cs`）、跨设备通知（`RemoteWorker.cs`）、Codex 读取（`UsageReader.cs`）、构建脚本 |
| `claude-reader.cs`、`ui-controls.cs` | Claude 额度与本机用量统计；圆环、分段条、趋势图等原生控件 |
| `mobile/` | iPhone 手机额度 PWA，零依赖静态页面，[开发说明](mobile/README.md) |
| `setup-*.ps1`、`login-*.bat`、`install-codex-cli.*`、`codex-tools.ps1` | 可选的安装与登录助手 |
| `profiles.json` | 账号显示名与本机目录 |
| `tests/` | 离线模型、读取器、助手与 PWA 测试 |
| `docs/` | [使用说明](docs/USER_GUIDE.md)、[版本亮点](docs/RELEASE_HIGHLIGHTS.md)、[安装与分享](docs/FRIENDS.md)、[iPhone 使用](docs/IPHONE.md)、[手机端安全](docs/MOBILE-SECURITY.md) |
| `assets/` | 图标设计与源文件，[说明](assets/README.md) |
| `.github/` | 校验构建、Pages 部署与测试版转正工作流 |

## 开发与测试

离线测试不访问账号、不联网，可用 Windows PowerShell 5.1 或 PowerShell 7：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/model.tests.ps1          # 额度规则、提醒、预测、吸附、手机快照、加密向量、后台线程
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/claude-reader.tests.ps1  # Claude 额度与本机用量日志
node tests/mobile.tests.js                                                              # iPhone PWA
```

构建 EXE 并用测试数据跑界面冒烟测试：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File desktop/build.ps1
dist\CodexUsage.exe --ui-smoke artifacts\ui-smoke-data
dist\CodexUsage.exe --self-test artifacts\client-self-test
```

GitHub Actions 在 Windows 上运行以上全部测试，截图与日志在 `windows-ui-preview` 产物中。

## 发布

`VERSION` 是发布号。推送到 `main` 且 `VERSION` 为新版本时，Actions 构建 EXE 并先发布为**测试版**（prerelease），只有开启“接收测试版”的客户端会收到；测试后运行 **Promote release to stable** 工作流转为正式版，所有客户端随之更新。已发布的版本资源不可修改，每次发布都要递增 `VERSION`。

## 授权

仓库当前未附带开源许可证。仓库公开可见不代表代码可被任意复制、修改或再发布；如需使用，请先联系仓库所有者取得授权。
