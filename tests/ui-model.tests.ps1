$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'ui-model.ps1')
$script:Checks = 0
function Assert-Equal($Actual,$Expected,[string]$Message) {
  if ($Actual -ne $Expected) { throw "$Message (expected: $Expected; actual: $Actual)" }
  $script:Checks++
}
function New-Profile([string]$Id,[double]$Five,[double]$Weekly) {
  [pscustomobject]@{
    id = $Id; label = $Id; ok = $true; fetchedAt = [DateTimeOffset]::Now.ToString('o')
    fiveHour = [pscustomobject]@{ remainingPercent = $Five }
    weekly = [pscustomobject]@{ remainingPercent = $Weekly }
    individualLimit = $null
  }
}
$personal = New-Profile 'personal' 100 95
$work = New-Profile 'work' 49 21
$work.individualLimit = [pscustomobject]@{ limit = 100; remainingPercent = 68 }
$pair = Get-QuotaPair $work
Assert-Equal $pair.fiveHour 49 '5-hour value must not be replaced by the minimum'
Assert-Equal $pair.longTerm 21 'Weekly is the limiting long-term window'
Assert-Equal $pair.source '每周额度' 'Source explains the displayed minimum'
$work.individualLimit.remainingPercent = 10
Assert-Equal (Get-QuotaPair $work).longTerm 10 'Monthly can be the limiting window'
$work.individualLimit.limit = 0
Assert-Equal (Get-QuotaPair $work).longTerm 21 'Zero-limit placeholder is excluded'
$work.individualLimit.limit = -1
Assert-Equal (Test-MeaningfulLimit $work.individualLimit) $false 'Negative limit is excluded'
$work.individualLimit.limit = 100
$work.individualLimit.remainingPercent = 0
Assert-Equal (Get-QuotaPair $work).longTerm 0 'Genuine zero remaining is preserved'
Assert-Equal (Format-Percent 0) '0%' 'Zero is not missing data'
Assert-Equal (Format-Percent $null) '—' 'Missing data is not zero'
Assert-Equal (Get-QuotaPair $null).longTerm $null 'Absent account is safe'
Assert-Equal (Get-QuotaPair ([pscustomobject]@{ ok = $false })).fiveHour $null 'Failed account has no invented quota'

$previous = [pscustomobject]@{ profiles = @($personal,$work) }
$failure = [pscustomobject]@{ id = 'personal'; label = 'personal'; ok = $false; error = 'offline' }
$freshWork = New-Profile 'work' 42 17
$incoming = [pscustomobject]@{ profiles = @($freshWork,$failure); fetchedAt = [DateTimeOffset]::Now.ToString('o') }
$merged = Merge-DisplayData $previous $incoming
$retained = Get-DisplayProfile $merged 'personal'
Assert-Equal $retained.fiveHour.remainingPercent 100 'Reordered profiles keep the correct previous account'
Assert-Equal $retained.stale $true 'Retained quota is explicitly stale'
Assert-Equal $retained.refreshError 'offline' 'Failure reason remains available'
Assert-Equal $retained.fetchedAt $personal.fetchedAt 'Failure never advances the successful-data timestamp'
Assert-Equal (Get-DisplayProfile $merged 'work').fiveHour.remainingPercent 42 'Other account continues updating'
Assert-Equal (Get-DisplayProfile $merged 'work').stale $false 'Successful account is fresh'
Assert-Equal $personal.PSObject.Properties.Name.Contains('stale') $false 'Merge does not mutate previous data'
$again = Merge-DisplayData $merged $incoming
Assert-Equal (Get-DisplayProfile $again 'personal').fiveHour.remainingPercent 100 'Repeated failures preserve last success'
$recovered = Merge-DisplayData $again ([pscustomobject]@{ profiles = @((New-Profile 'personal' 80 90)); fetchedAt = [DateTimeOffset]::Now.ToString('o') })
Assert-Equal (Get-DisplayProfile $recovered 'personal').stale $false 'Recovery clears stale state'
Assert-Equal (Get-DisplayProfile $recovered 'personal').refreshError '' 'Recovery clears previous error'
$firstFailure = Merge-DisplayData $null ([pscustomobject]@{ profiles = @($failure) })
Assert-Equal (Get-DisplayProfile $firstFailure 'personal').ok $false 'First failure does not manufacture cached data'
Assert-Equal (Test-ProfileStale $personal) $false 'Recent successful data is current'
$personal.fetchedAt = [DateTimeOffset]::Now.AddMinutes(-11).ToString('o')
Assert-Equal (Test-ProfileStale $personal) $true 'Data expires even without an explicit refresh error'
$personal.fetchedAt = 'invalid'
Assert-Equal (Test-ProfileStale $personal) $true 'Invalid timestamp is not considered fresh'
Assert-Equal (Get-ResetText ([DateTimeOffset]::Now.AddMinutes(-1).ToString('o'))) '等待额度更新' 'Expired countdown does not claim a successful reset'
$now = [DateTimeOffset]::Now.Date.AddHours(12)
$now = [DateTimeOffset]$now
$zero = [pscustomobject]@{ remainingPercent = 0; resetsAt = $now.AddHours(2).ToString('o') }
Assert-Equal (Get-CompactRecovery $zero $false $now) '约 2小时0分后恢复' 'Exhausted quota shows local recovery time'
Assert-Equal (Get-CompactRecovery $zero $true $now) '5h 恢复时间待确认' 'Stale zero does not promise a recovery time'
$zero.resetsAt = $now.AddHours(14).ToString('o')
Assert-Equal (Get-CompactRecovery $zero $false $now) '约 14小时0分后恢复' 'Recovery after midnight has a day label'
$zero.resetsAt = $now.AddMinutes(-1).ToString('o')
Assert-Equal (Get-CompactRecovery $zero $false $now) '5h 等待刷新确认' 'Expired reset does not invent replenishment'
$zero.resetsAt = 'invalid'
Assert-Equal (Get-CompactRecovery $zero $false $now) '5h 恢复时间未知' 'Malformed reset remains unknown'
$zero.resetsAt = $null
Assert-Equal (Get-CompactRecovery $zero $false $now) '5h 恢复时间未知' 'Absent reset remains unknown'
$zero.resetsAt = $now.AddHours(1).UtcDateTime
Assert-Equal (Get-CompactRecovery $zero $false $now) '约 1小时0分后恢复' 'Typed JSON DateTime preserves time zone'
$zero.remainingPercent = 0.4
Assert-Equal (Get-CompactRecovery $zero $false $now) '5h / 长周期 · 剩余' 'Positive fraction is not exhausted'
Assert-Equal (Format-CompactPercent 0.4) '<1%' 'Small positive quota does not display zero'
Assert-Equal (Get-CompactRecovery $null $false $now) '5h / 长周期 · 剩余' 'Missing quota is not exhausted'
$zero.remainingPercent = 0
$zero.resetsAt = $now.AddMinutes(32).ToString('o')
Assert-Equal (Get-CompactRecovery $zero $false $now) '约 32 分钟后恢复' 'Countdown updates from remaining time'
Assert-Equal (Get-ProfileEnabled ([pscustomobject]@{ id='personal' })) $true 'Legacy profiles remain enabled'
Assert-Equal (Get-ProfileEnabled ([pscustomobject]@{ enabled=$false })) $false 'Hidden accounts are disabled'
$due = New-Profile 'personal' 0 30
$due.fetchedAt = $now.ToString('o')
$due.fiveHour | Add-Member -NotePropertyName resetsAt -NotePropertyValue ($now.AddSeconds(-1).ToString('o'))
Assert-Equal ([string]::IsNullOrEmpty((Get-RecoveryKey $due $now))) $false 'Exhausted reset triggers a refresh key'
$due.fiveHour.remainingPercent = 1
Assert-Equal (Get-RecoveryKey $due $now) $null 'Available quota does not trigger recovery refresh'
$resetFixture = New-Profile 'work' 40 30
$weeklyReset = $now.AddDays(3).ToString('o')
$monthlyReset = $now.AddDays(20).ToString('o')
$resetFixture.weekly | Add-Member -NotePropertyName resetsAt -NotePropertyValue $weeklyReset
$resetFixture.individualLimit = [pscustomobject]@{ limit=100; remainingPercent=50; resetsAt=$monthlyReset }
Assert-Equal (Get-QuotaPair $resetFixture).longTermResetsAt $weeklyReset 'Weekly-limited display carries the weekly reset'
$resetFixture.individualLimit.remainingPercent = 10
Assert-Equal (Get-QuotaPair $resetFixture).longTermResetsAt $monthlyReset 'Monthly-limited display carries the monthly reset'
$resetFixture.individualLimit.remainingPercent = 30
Assert-Equal (Get-QuotaPair $resetFixture).longTermResetsAt $weeklyReset 'Equal quotas retain the weekly source and reset'
$resetFixture.weekly = $null
Assert-Equal (Get-QuotaPair $resetFixture).longTermResetsAt $monthlyReset 'Monthly-only quota carries its own reset'
$resetFixture.individualLimit.resetsAt = $null
Assert-Equal (Get-QuotaPair $resetFixture).longTermResetsAt $null 'Missing reset remains unknown'
$resetFixture.ok = $false
Assert-Equal (Get-QuotaPair $resetFixture).longTermResetsAt $null 'Failed response does not supply a reset'
Assert-Equal (Format-Percent 0.4) '<1%' 'All views must preserve a small positive quota'
Assert-Equal (Format-Percent 0.0001) '<1%' 'Tiny positive quota is not exhausted'
Assert-Equal (Format-Percent 0) '0%' 'Only exact zero is exhausted'
Assert-Equal (Format-Percent 120) '100%' 'Display percentages are bounded'
Assert-Equal (Format-Percent -1) '—' 'Invalid negative quota remains unknown'
Assert-Equal (Format-Percent ([double]::NaN)) '—' 'NaN remains unknown'
Assert-Equal (Format-Percent ([double]::PositiveInfinity)) '—' 'Infinity remains unknown'
Assert-Equal (Format-Percent 'bad') '—' 'Malformed quota remains unknown'
Assert-Equal (Format-Percent 2.5) '3%' 'Midpoint rounding agrees with the native renderer'
$pendingFixture = New-Profile 'personal' 0 40
$pendingFixture.fetchedAt = [DateTimeOffset]::Now.ToString('o')
$pendingData = [pscustomobject]@{profiles=@($pendingFixture);fetchedAt=$pendingFixture.fetchedAt}
$incomingPending = [pscustomobject]@{profiles=@([pscustomobject]@{id='personal';ok=$false;pending=$true;error=''});fetchedAt=$pendingFixture.fetchedAt}
$mergedPending = Merge-DisplayData $pendingData $incomingPending
Assert-Equal $mergedPending.profiles[0].fiveHour.remainingPercent 0 'Pending refresh retains the last successful value'
Assert-Equal $mergedPending.profiles[0].pending $true 'Pending state is retained separately from failures'
Assert-Equal $mergedPending.profiles[0].stale $false 'Fresh pending values are not marked as failed'
Assert-Equal $mergedPending.profiles[0].fetchedAt $pendingFixture.fetchedAt 'Pending refresh retains the account timestamp'
Assert-Equal ((Get-ProfileUpdateText $mergedPending.profiles[0]) -match '正在读取') $true 'Update tooltip identifies the pending account'
# v1.0: Claude account slot, geometry and local usage formatting.
Assert-Equal ((Get-AllAccountIds) -join ',') 'personal,work,claude' 'Two Codex slots and one Claude slot'
Assert-Equal (Get-MonitorWidth 1) 220 'Single-account width is unchanged'
Assert-Equal (Get-MonitorWidth 2) 360 'Two-account width is unchanged'
Assert-Equal (Get-MonitorWidth 3) 510 'Three accounts widen the strip'
Assert-Equal (Get-ExpandedHeightFor 1) 108 'Single-account expanded height is unchanged'
Assert-Equal (Get-ExpandedHeightFor 2) 142 'Two-account expanded height is unchanged'
Assert-Equal (Get-ExpandedHeightFor 3) 176 'Three accounts add one row'
Assert-Equal (Get-DefaultAccountName 'claude') 'Claude' 'Claude default name'
Assert-Equal (Format-TokenCount 999) '999' 'Small token counts'
Assert-Equal (Format-TokenCount 1250000) '1.3M' 'Million token counts'
Assert-Equal (Format-TokenCount $null) '—' 'Missing token count'
Assert-Equal (Format-Usd 12.345) '$12.35' 'Cost formatting'
Assert-Equal (Format-Usd 0.004) '<$0.01' 'Tiny positive cost is not shown as zero'
Assert-Equal ((Get-LocalUsageSummary $null) -match '暂无') $true 'Missing local usage is explicit'
$claudeUsage = [pscustomobject]@{ today = [pscustomobject]@{ totalTokens = 2500000; costUsd = 4.5; inputTokens = 1; outputTokens = 2; cacheWriteTokens = 3; cacheReadTokens = 4; messages = 9 }; week = [pscustomobject]@{ totalTokens = 1.2e7; costUsd = 30 }; days = @(); topModelToday = 'Opus 5.5' }
Assert-Equal (Get-LocalUsageSummary $claudeUsage) '今日 2.5M tokens · ≈$4.50　近 7 天 12.0M · ≈$30.00' 'Local usage summary'
Assert-Equal ((Get-LocalUsageDetail $claudeUsage) -match '估算') $true 'Detail explains the cost is an estimate'
$claudeProfile = [pscustomobject]@{ id = 'claude'; label = 'Claude'; provider = 'claude'; ok = $true; fetchedAt = [DateTimeOffset]::Now.ToString('o'); fiveHour = [pscustomobject]@{ remainingPercent = 80 }; weekly = [pscustomobject]@{ remainingPercent = 40; resetsAt = $null }; individualLimit = $null }
Assert-Equal (Get-QuotaPair $claudeProfile).longTerm 40 'Claude long-term uses the all-model weekly window'
Assert-Equal (Test-ClaudeProfile $claudeProfile) $true 'Claude profile detection'
Assert-Equal (Test-ClaudeProfile $personal) $false 'Codex profile is not Claude'
$migrationHome = Join-Path ([System.IO.Path]::GetTempPath()) ('claude-migration-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $migrationHome | Out-Null
$savedClaudeDir = $env:CLAUDE_CONFIG_DIR
try {
  $env:CLAUDE_CONFIG_DIR = $null
  $legacy = [pscustomobject]@{ profiles = @([pscustomobject]@{ id = 'personal'; label = '个人'; codexHome = '~/.codex-personal' }) }
  Assert-Equal (Add-ClaudeProfileIfMissing -Config $legacy -UserProfile $migrationHome) $true 'Older configurations gain a Claude slot'
  Assert-Equal @($legacy.profiles).Count 2 'Existing Codex profiles are retained'
  Assert-Equal $legacy.profiles[1].enabled $false 'Claude starts hidden when Claude Code is not set up'
  Assert-Equal (Add-ClaudeProfileIfMissing -Config $legacy -UserProfile $migrationHome) $false 'Migration is idempotent'
  New-Item -ItemType Directory -Force -Path (Join-Path $migrationHome '.claude/projects') | Out-Null
  $legacy2 = [pscustomobject]@{ profiles = @([pscustomobject]@{ id = 'work'; label = '工作' }) }
  [void](Add-ClaudeProfileIfMissing -Config $legacy2 -UserProfile $migrationHome)
  Assert-Equal $legacy2.profiles[1].enabled $true 'Claude starts visible when Claude Code is already used here'
  Assert-Equal $legacy2.profiles[1].provider 'claude' 'Migrated slot is routed to the Claude reader'
} finally { $env:CLAUDE_CONFIG_DIR = $savedClaudeDir; Remove-Item -Recurse -Force $migrationHome -ErrorAction SilentlyContinue }
# v1.1: burn-rate forecast.
$base = [DateTimeOffset]::new(2026,11,16,12,0,0,[TimeSpan]::Zero)
$reset5 = $base.AddHours(3).ToString('o')
$history = @{}
function New-Sample([DateTimeOffset]$At,[double]$Five,[double]$Weekly,[string]$Reset = $reset5) {
  [pscustomobject]@{ profiles = @([pscustomobject]@{ id = 'personal'; ok = $true; fetchedAt = $At.ToString('o')
    fiveHour = [pscustomobject]@{ remainingPercent = $Five; resetsAt = $Reset }
    weekly = [pscustomobject]@{ remainingPercent = $Weekly; resetsAt = $base.AddDays(3).ToString('o') } }) }
}
# 30% per hour: 80 -> 70 over 20 minutes, one sample per minute.
for ($m = 0; $m -le 20; $m++) { Add-UsageSamples -History $history -Data (New-Sample $base.AddMinutes($m) (80 - $m * 0.5) 60) -Now $base.AddMinutes($m) }
$now = $base.AddMinutes(20)
Add-UsageSamples -History $history -Data (New-Sample $base.AddMinutes(20).AddSeconds(10) 70 60) -Now $now
Assert-Equal $history['personal|fiveHour'].Count 21 'Samples closer than 50 seconds are not duplicated'
$forecast = Get-BurnForecast -History $history -Id 'personal' -Kind 'fiveHour' -Window ([pscustomobject]@{ remainingPercent = 70; resetsAt = $reset5 }) -Now $now
Assert-Equal $forecast.state 'exhaust' 'Steady consumption predicts exhaustion before reset'
Assert-Equal ([Math]::Round($forecast.ratePerHour)) 30 'Rate is percent per hour'
Assert-Equal ([Math]::Round($forecast.minutesLeft)) 140 '70% at 30%/h lasts 140 minutes'
Assert-Equal ($forecast.text -match '用完') $true 'Forecast text names the exhaustion time'
$late = Get-BurnForecast -History $history -Id 'personal' -Kind 'fiveHour' -Window ([pscustomobject]@{ remainingPercent = 70; resetsAt = $base.AddMinutes(60).ToString('o') }) -Now $now
Assert-Equal $late.state 'none' 'Samples from another reset cycle are not mixed in'
$safeHistory = @{}
for ($m = 0; $m -le 20; $m++) { Add-UsageSamples -History $safeHistory -Data (New-Sample $base.AddMinutes($m) (80 - $m * 0.5) 60 $base.AddMinutes(40).ToString('o')) -Now $base.AddMinutes($m) }
Assert-Equal (Get-BurnForecast -History $safeHistory -Id 'personal' -Kind 'fiveHour' -Window ([pscustomobject]@{ remainingPercent = 70; resetsAt = $base.AddMinutes(40).ToString('o') }) -Now $now).state 'safe' 'Quota that lasts until reset is safe'
Assert-Equal (Get-BurnForecast -History $history -Id 'personal' -Kind 'weekly' -Window ([pscustomobject]@{ remainingPercent = 60; resetsAt = $base.AddDays(3).ToString('o') }) -Now $now).state 'none' 'Weekly needs a longer span than 20 minutes'
$idleHistory = @{}
for ($m = 0; $m -le 15; $m++) { Add-UsageSamples -History $idleHistory -Data (New-Sample $base.AddMinutes($m) 90 60) -Now $base.AddMinutes($m) }
Assert-Equal (Get-BurnForecast -History $idleHistory -Id 'personal' -Kind 'fiveHour' -Window ([pscustomobject]@{ remainingPercent = 90; resetsAt = $reset5 }) -Now $base.AddMinutes(15)).state 'idle' 'No consumption is idle, not a prediction'
$short = @{}
Add-UsageSamples -History $short -Data (New-Sample $base 80 60) -Now $base
Add-UsageSamples -History $short -Data (New-Sample $base.AddMinutes(3) 75 60) -Now $base.AddMinutes(3)
Assert-Equal (Get-BurnForecast -History $short -Id 'personal' -Kind 'fiveHour' -Window ([pscustomobject]@{ remainingPercent = 75; resetsAt = $reset5 }) -Now $base.AddMinutes(3)).state 'none' 'Too short a span gives no forecast'
$jump = @{}
for ($m = 0; $m -le 12; $m++) { Add-UsageSamples -History $jump -Data (New-Sample $base.AddMinutes($m) (20 - $m) 60 $null) -Now $base.AddMinutes($m) }
Add-UsageSamples -History $jump -Data (New-Sample $base.AddMinutes(13) 100 60 $null) -Now $base.AddMinutes(13)
Assert-Equal (Get-BurnForecast -History $jump -Id 'personal' -Kind 'fiveHour' -Window ([pscustomobject]@{ remainingPercent = 100; resetsAt = $null }) -Now $base.AddMinutes(13)).state 'none' 'A jump up (reset) starts a new forecast window'
Assert-Equal (Get-BurnForecast -History $history -Id 'personal' -Kind 'fiveHour' -Window ([pscustomobject]@{ remainingPercent = 0; resetsAt = $reset5 }) -Now $now).state 'empty' 'Exhausted quota uses the recovery countdown instead'
$failed = [pscustomobject]@{ profiles = @([pscustomobject]@{ id = 'work'; ok = $false; fetchedAt = $now.ToString('o'); fiveHour = [pscustomobject]@{ remainingPercent = 1 } }) }
Add-UsageSamples -History $history -Data $failed -Now $now
Assert-Equal $history.ContainsKey('work|fiveHour') $false 'Failed reads never become samples'
$roundTrip = ConvertFrom-HistoryJson (ConvertTo-HistoryJson $history)
Assert-Equal $roundTrip['personal|fiveHour'].Count 21 'History survives a restart'
Assert-Equal (Get-BurnForecast -History $roundTrip -Id 'personal' -Kind 'fiveHour' -Window ([pscustomobject]@{ remainingPercent = 70; resetsAt = $reset5 }) -Now $now).state 'exhaust' 'Reloaded history still forecasts'
Assert-Equal ((Get-ForecastDetail $forecast) -match '30%/小时') $true 'Detail shows the rate'
Write-Output "UI model: $script:Checks checks passed."
