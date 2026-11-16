param([switch]$NoPause)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

# Guided Claude Code setup for the Claude account slot. The dashboard only reads
# Claude Code's own login and local session logs; it never stores the token.
function Find-ClaudeCommand {
  foreach ($name in @('claude.exe','claude.cmd','claude')) {
    $command = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return [string]$command.Source }
  }
  foreach ($candidate in @((Join-Path $env:USERPROFILE '.local\bin\claude.exe'),(Join-Path $env:APPDATA 'npm\claude.cmd'))) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
  }
  return $null
}

$claudeHome = if ([string]::IsNullOrWhiteSpace($env:CLAUDE_CONFIG_DIR)) { Join-Path $env:USERPROFILE '.claude' } else { $env:CLAUDE_CONFIG_DIR }
$credentials = Join-Path $claudeHome '.credentials.json'

try {
  $claude = Find-ClaudeCommand
  if ([string]::IsNullOrWhiteSpace($claude)) {
    Write-Host '未找到 Claude Code。' -ForegroundColor Yellow
    Write-Host '将使用 Anthropic 官方安装命令：irm https://claude.ai/install.ps1 | iex'
    $answer = Read-Host '现在安装？(Y/N)'
    if ($answer -notmatch '^[Yy]') {
      Write-Host '已取消。也可以参考 https://docs.claude.com/en/docs/claude-code/setup 手动安装。'
      exit 1
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -Command 'irm https://claude.ai/install.ps1 | iex'
    $env:Path = [Environment]::GetEnvironmentVariable('Path','User') + ';' + [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + (Join-Path $env:USERPROFILE '.local\bin')
    $claude = Find-ClaudeCommand
    if ([string]::IsNullOrWhiteSpace($claude)) { throw '安装完成后仍未找到 claude，请重新打开本窗口或重启电脑后再试。' }
  }
  Write-Host ('Claude Code: ' + $claude) -ForegroundColor Green
  if (Test-Path -LiteralPath $credentials) {
    Write-Host '已检测到 Claude 订阅登录信息。' -ForegroundColor Green
    Write-Host '如需切换账号：下面会打开 Claude Code，输入 /login 重新登录；不需要切换可直接输入 /exit。'
  } else {
    Write-Host '下面会打开 Claude Code：'
    Write-Host '  1. 选择用 Claude 订阅账号（Pro / Max）登录，或在界面中输入 /login'
    Write-Host '  2. 在浏览器中完成授权'
    Write-Host '  3. 回到这里输入 /exit 退出'
  }
  Write-Host ''
  & $claude
  Write-Host ''
  if (Test-Path -LiteralPath $credentials) {
    Write-Host '完成。回到额度面板点击“重新检测”或“刷新”。' -ForegroundColor Green
  } else {
    Write-Host '还没有检测到订阅登录。没有订阅时，面板仍会显示本机 Claude Code 用量。' -ForegroundColor Yellow
  }
} catch {
  Write-Host ''
  Write-Host ('Claude 设置失败: ' + $_.Exception.Message) -ForegroundColor Red
  exit 1
}
