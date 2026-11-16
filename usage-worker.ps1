param(
  [Parameter(Mandatory = $true)][string]$OutputPath,
  [Parameter(Mandatory = $true)][string]$LogPath
)

$ErrorActionPreference = 'Stop'

function Write-WorkerLog {
  param([string]$Message)
  try {
    $line = ('[{0}] {1}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff'), $Message)
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
  } catch {}
}

try {
  $root = Split-Path -Parent $MyInvocation.MyCommand.Path
  $logDir = Split-Path -Parent $LogPath
  if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }

  Write-WorkerLog 'Worker started.'
  . (Join-Path $root 'codex-tools.ps1')
  . (Join-Path $root 'usage-reader.ps1')

  $codexPath = [string](Find-CodexCommand)
  if ([string]::IsNullOrWhiteSpace($codexPath)) {
    $codexPath = ''
    Write-WorkerLog 'Codex CLI not found; Codex accounts will report an error, Claude can still update.'
  } else { Write-WorkerLog ('Codex path: ' + $codexPath) }

  $dataRoot = if ([string]::IsNullOrWhiteSpace($env:CODEX_USAGE_DATA_DIR)) { $root } else { $env:CODEX_USAGE_DATA_DIR }
  $configPath = Join-Path $dataRoot 'profiles.json'
  $data = Get-CodexUsageAllProfiles -ConfigPath $configPath -CodexPath $codexPath
  $payload = [pscustomobject]@{
    ok = $true
    readId = $env:CODEX_USAGE_READ_ID
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    data = $data
  }
  $json = $payload | ConvertTo-Json -Depth 30
  Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
  Write-WorkerLog 'Worker completed successfully.'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $detail = $_.ScriptStackTrace
  Write-WorkerLog ('ERROR: ' + $msg)
  if (-not [string]::IsNullOrWhiteSpace($detail)) { Write-WorkerLog $detail }
  try {
    $payload = [pscustomobject]@{
      ok = $false
      readId = $env:CODEX_USAGE_READ_ID
      generatedAt = (Get-Date).ToUniversalTime().ToString('o')
      error = $msg
      detail = $detail
    }
    $json = $payload | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
  } catch {}
  exit 1
}
