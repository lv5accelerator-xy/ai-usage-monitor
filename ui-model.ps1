# UI-only presentation rules. No network, credentials, or WinForms dependencies.
function Test-MeaningfulLimit {
  param($Limit)
  if ($null -eq $Limit) { return $false }
  if ($null -eq $Limit.limit) { return $true }
  try { return ([double]$Limit.limit -gt 0) } catch { return $false }
}

function Get-QuotaPair {
  param($Profile)
  $fiveHour = $null
  $longTerm = $null
  $longTermResetsAt = $null
  $source = '未提供'
  if ($null -ne $Profile -and $Profile.ok) {
    if ($null -ne $Profile.fiveHour) { $fiveHour = $Profile.fiveHour.remainingPercent }
    if ($null -ne $Profile.weekly -and $null -ne $Profile.weekly.remainingPercent) {
      $longTerm = [double]$Profile.weekly.remainingPercent
      $longTermResetsAt = $Profile.weekly.resetsAt
      $source = '每周额度'
    }
    if ((Test-MeaningfulLimit $Profile.individualLimit) -and $null -ne $Profile.individualLimit.remainingPercent) {
      $monthly = [double]$Profile.individualLimit.remainingPercent
      if ($null -eq $longTerm -or $monthly -lt $longTerm) {
        $longTerm = $monthly
        $longTermResetsAt = $Profile.individualLimit.resetsAt
        $source = '工作空间 / 月度额度'
      }
    }
  }
  return [pscustomobject]@{ fiveHour = $fiveHour; longTerm = $longTerm; source = $source; longTermResetsAt = $longTermResetsAt }
}

function Get-DisplayProfile {
  param($Data,[string]$Id)
  foreach ($profile in @($Data.profiles)) {
    if ($null -ne $profile -and [string]$profile.id -eq $Id) { return $profile }
  }
  return $null
}

function Merge-DisplayData {
  param($Previous,$Incoming)
  $profiles = @()
  foreach ($profile in @($Incoming.profiles)) {
    if ($null -eq $profile) { continue }
    $old = Get-DisplayProfile -Data $Previous -Id ([string]$profile.id)
    $source = $profile
    $stale = -not $profile.ok
    # Match by account ID, never by list position. Retain last success only for this account.
    if (-not $profile.ok -and $null -ne $old -and $old.ok) { $source = $old }
    if ($profile.pending -and $null -ne $old -and $old.ok) { $stale = Test-ProfileStale $old }
    $copy = [ordered]@{}
    foreach ($property in $source.PSObject.Properties) { $copy[$property.Name] = $property.Value }
    $copy['stale'] = $stale
    $copy['pending'] = [bool]$profile.pending
    $copy['refreshError'] = if ($stale) { [string]$profile.error } else { '' }
    $profiles += [pscustomobject]$copy
  }
  return [pscustomobject]@{ profiles = $profiles; fetchedAt = $Incoming.fetchedAt }
}

function Test-ProfileStale {
  param($Profile,[DateTimeOffset]$Now = [DateTimeOffset]::Now)
  if ($null -eq $Profile -or -not $Profile.ok -or $Profile.stale) { return $true }
  $fetchedAt = [DateTimeOffset]::MinValue
  if (-not [DateTimeOffset]::TryParse([string]$Profile.fetchedAt,[ref]$fetchedAt)) { return $true }
  return (($Now - $fetchedAt).TotalMinutes -ge 10)
}

function Format-Percent {
  param($Value)
  if ($null -eq $Value) { return '—' }
  try {
    $number = [double]$Value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt 0) { return '—' }
    if ($number -gt 0 -and $number -lt 1) { return '<1%' }
    return ('{0:0}%' -f [Math]::Round([Math]::Min([double]100,$number),0,[MidpointRounding]::AwayFromZero))
  } catch { return '—' }
}

function Get-ResetText {
  param([string]$Iso)
  if ([string]::IsNullOrWhiteSpace($Iso)) { return '未提供重置时间' }
  try {
    $span = [DateTimeOffset]::Parse($Iso) - [DateTimeOffset]::Now
    if ($span.TotalSeconds -le 0) { return '等待额度更新' }
    if ($span.TotalDays -ge 1) { return ('{0}天 {1}小时后重置' -f [Math]::Floor($span.TotalDays), $span.Hours) }
    if ($span.TotalHours -ge 1) { return ('{0}小时 {1}分钟后重置' -f [Math]::Floor($span.TotalHours), $span.Minutes) }
    return ('{0}分钟后重置' -f [Math]::Max(1, [Math]::Ceiling($span.TotalMinutes)))
  } catch { return '未提供重置时间' }
}

# A compact pair is two independent remaining percentages, never a ratio or sum.
function Format-CompactPercent {
  param($Value)
  return Format-Percent $Value
}

function Get-ProfileUpdateText {
  param($Profile)
  $time = [DateTimeOffset]::MinValue
  $stamp = if ($null -ne $Profile -and $Profile.ok -and [DateTimeOffset]::TryParse([string]$Profile.fetchedAt,[ref]$time)) {
    $time.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss zzz')
  } else { '尚无成功更新' }
  return ('最近更新：' + $stamp + $(if ($Profile.pending) { ' · 正在读取' } elseif (Test-ProfileStale $Profile) { ' · 数据未更新' } else { '' }))
}

function Get-CompactRecovery {
  param($FiveHour,[bool]$Stale = $false,[DateTimeOffset]$Now = [DateTimeOffset]::Now)
  if ($null -eq $FiveHour -or $null -eq $FiveHour.remainingPercent -or [double]$FiveHour.remainingPercent -ne 0) { return '5h / 长周期 · 剩余' }
  if ($Stale) { return '5h 恢复时间待确认' }
  try {
    $raw = $FiveHour.resetsAt
    if ($null -eq $raw -or [string]::IsNullOrWhiteSpace([string]$raw)) { return '5h 恢复时间未知' }
    $reset = if ($raw -is [DateTimeOffset]) { $raw } elseif ($raw -is [DateTime]) { [DateTimeOffset]$raw } else { [DateTimeOffset]::Parse([string]$raw) }
    if ($reset -le $Now) { return '5h 等待刷新确认' }
    $minutes = [Math]::Max(1,[Math]::Ceiling(($reset - $Now).TotalMinutes))
    if ($minutes -lt 60) { return ('约 {0} 分钟后恢复' -f $minutes) }
    $hours = [Math]::Floor($minutes / 60)
    $rest = $minutes % 60
    return ('约 {0}小时{1}分后恢复' -f $hours,$rest)
  } catch { return '5h 恢复时间未知' }
}

function Get-ProfileEnabled {
  param($Profile)
  return ($null -ne $Profile -and ($Profile.PSObject.Properties.Name -notcontains 'enabled' -or [bool]$Profile.enabled))
}
function Get-RecoveryKey {
  param($Profile,[DateTimeOffset]$Now = [DateTimeOffset]::Now)
  if (Test-ProfileStale $Profile $Now) { return $null }
  if ($null -eq $Profile.fiveHour.remainingPercent -or [double]$Profile.fiveHour.remainingPercent -ne 0) { return $null }
  try {
    $reset = [DateTimeOffset]::Parse([string]$Profile.fiveHour.resetsAt)
    if ($reset -le $Now) { return ([string]$Profile.id + '|' + $reset.ToUniversalTime().ToString('o')) }
  } catch {}
  return $null
}

# ---- Accounts: two Codex slots plus one Claude slot (v1.0).
function Get-AllAccountIds { return @('personal','work','claude') }

function Get-DefaultAccountName {
  param([string]$Id)
  if ($Id -eq 'personal') { return '个人' }
  if ($Id -eq 'claude') { return 'Claude' }
  return '工作'
}

function Test-ClaudeProfile {
  param($Profile)
  return ($null -ne $Profile -and ([string]$Profile.provider -eq 'claude' -or [string]$Profile.id -eq 'claude'))
}

# Adds the Claude slot to configurations created before v1.0. Returns $true when changed.
# It starts enabled only when Claude Code is already set up on this computer.
function Add-ClaudeProfileIfMissing {
  param([Parameter(Mandatory = $true)]$Config,[string]$UserProfile = $env:USERPROFILE)
  foreach ($profile in @($Config.profiles)) { if ([string]$profile.id -eq 'claude') { return $false } }
  $claudeHome = if ([string]::IsNullOrWhiteSpace($env:CLAUDE_CONFIG_DIR)) { Join-Path $UserProfile '.claude' } else { $env:CLAUDE_CONFIG_DIR }
  $detected = (Test-Path -LiteralPath (Join-Path $claudeHome '.credentials.json')) -or (Test-Path -LiteralPath (Join-Path $claudeHome 'projects'))
  $entry = [pscustomobject]@{ id = 'claude'; label = 'Claude'; provider = 'claude'; claudeHome = '~/.claude'; enabled = [bool]$detected }
  if (-not [string]::IsNullOrWhiteSpace($env:CLAUDE_CONFIG_DIR)) { $entry.claudeHome = $env:CLAUDE_CONFIG_DIR }
  $Config.profiles = @(@($Config.profiles) + $entry)
  return $true
}

# Floating monitor geometry in logical pixels for 1-3 visible accounts.
function Get-MonitorWidth {
  param([int]$Count)
  if ($Count -le 1) { return 220 }
  return 360 + 150 * ($Count - 2)
}
function Get-ExpandedHeightFor {
  param([int]$Count)
  return 74 + 34 * [Math]::Max(1,$Count)
}

function Format-TokenCount {
  param($Value)
  try { $number = [double]$Value } catch { return '—' }
  if ($null -eq $Value -or [double]::IsNaN($number) -or $number -lt 0) { return '—' }
  if ($number -ge 1e9) { return ('{0:0.0}B' -f ($number / 1e9)) }
  if ($number -ge 1e6) { return ('{0:0.0}M' -f ($number / 1e6)) }
  if ($number -ge 1e3) { return ('{0:0.0}K' -f ($number / 1e3)) }
  return ('{0:0}' -f $number)
}

function Format-Usd {
  param($Value)
  try { $number = [double]$Value } catch { return '—' }
  if ($null -eq $Value -or [double]::IsNaN($number) -or $number -lt 0) { return '—' }
  if ($number -gt 0 -and $number -lt 0.01) { return '<$0.01' }
  if ($number -ge 1000) { return ('${0:N0}' -f $number) }
  return ('${0:0.00}' -f $number)
}

# One line for cards; tooltips get the breakdown. Cost is an API-price estimate.
function Get-LocalUsageSummary {
  param($Usage)
  if ($null -eq $Usage -or $null -eq $Usage.today) { return '本机 Claude Code 用量：暂无记录' }
  return ('今日 {0} tokens · ≈{1}　近 7 天 {2} · ≈{3}' -f (Format-TokenCount $Usage.today.totalTokens),(Format-Usd $Usage.today.costUsd),(Format-TokenCount $Usage.week.totalTokens),(Format-Usd $Usage.week.costUsd))
}

function Get-LocalUsageDetail {
  param($Usage)
  if ($null -eq $Usage -or $null -eq $Usage.today) { return '尚未在本机找到最近 7 天的 Claude Code 会话记录。' }
  $t = $Usage.today
  $lines = @(
    ('今日：输入 {0} · 输出 {1} · 缓存写入 {2} · 缓存读取 {3}' -f (Format-TokenCount $t.inputTokens),(Format-TokenCount $t.outputTokens),(Format-TokenCount $t.cacheWriteTokens),(Format-TokenCount $t.cacheReadTokens)),
    ('今日请求 {0} 次{1}' -f [int64]$t.messages,$(if ($Usage.topModelToday) { ' · 主要模型 ' + [string]$Usage.topModelToday } else { '' }))
  )
  foreach ($day in @($Usage.days)) {
    if ($null -eq $day) { continue }
    $lines += ('{0}　{1} tokens · ≈{2}' -f [string]$day.date,(Format-TokenCount $day.totalTokens),(Format-Usd $day.costUsd))
  }
  $lines += '费用按 Anthropic API 公开价格估算；订阅用户不按 token 计费。'
  return ($lines -join "`r`n")
}
