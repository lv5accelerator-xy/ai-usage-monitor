$ErrorActionPreference = 'Stop'
# Offline tests for the native client's shared rules (desktop/AppModel.cs, desktop/RemoteWorker.cs).
# Ported from the v1.x PowerShell model, behavior, mobile-sync and remote-notification tests.
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'support\load-csharp.ps1')
Import-AppModel -Root $root
$script:Checks = 0
function Assert-Equal($Actual,$Expected,[string]$Message) {
  if ($Actual -ne $Expected) { throw "$Message (expected: $Expected; actual: $Actual)" }
  $script:Checks++
}
function Assert-True([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message }; $script:Checks++ }
function T($Value) { return ,(ConvertTo-JsonTree $Value) }
$M = [CodexUsageDesktop.Model]
$B = [CodexUsageDesktop.Behavior]
$F = [CodexUsageDesktop.Forecasts]
$Mob = [CodexUsageDesktop.Mobile]
$C = [CodexUsageDesktop.RemoteCrypto]
$J = [CodexUsageDesktop.J]

# ---------------------------------------------------------------- quota presentation
function New-Profile([string]$Id,$Five,$Weekly) {
  return ,(T @{ id = $Id; label = $Id; ok = $true; fetchedAt = [DateTimeOffset]::Now.ToString('o'); fiveHour = @{ remainingPercent = $Five }; weekly = @{ remainingPercent = $Weekly }; individualLimit = $null })
}
$personal = New-Profile 'personal' 100 95
$work = New-Profile 'work' 49 21
$work['individualLimit'] = (T @{ limit = 100; remainingPercent = 68 }).psobject.BaseObject
$pair = $M::Pair($work)
Assert-Equal $pair.FiveHour 49 '5-hour value must not be replaced by the minimum'
Assert-Equal $pair.LongTerm 21 'Weekly is the limiting long-term window'
Assert-Equal $pair.Source '每周额度' 'Source explains the displayed minimum'
$work['individualLimit']['remainingPercent'] = 10
Assert-Equal $M::Pair($work).LongTerm 10 'Monthly can be the limiting window'
$work['individualLimit']['limit'] = 0
Assert-Equal $M::Pair($work).LongTerm 21 'Zero-limit placeholder is excluded'
$work['individualLimit']['limit'] = -1
Assert-Equal $M::MeaningfulLimit($work['individualLimit']) $false 'Negative limit is excluded'
$work['individualLimit']['limit'] = 100
$work['individualLimit']['remainingPercent'] = 0
Assert-Equal $M::Pair($work).LongTerm 0 'Genuine zero remaining is preserved'
Assert-Equal $M::Percent(0) '0%' 'Zero is not missing data'
Assert-Equal $M::Percent($null) '—' 'Missing data is not zero'
Assert-Equal $M::Percent(0.4) '<1%' 'Small positive quota does not display zero'
Assert-Equal $M::Percent(99.5) '100%' 'Midpoint rounds away from zero'
Assert-Equal $M::Percent('NaN') '—' 'Invalid values stay missing'
Assert-Equal $M::Pair($null).LongTerm $null 'Absent account is safe'
Assert-Equal $M::Pair((T @{ ok = $false })).FiveHour $null 'Failed account has no invented quota'

$previous = T @{ profiles = @($personal,$work) }
$failure = T @{ id = 'personal'; label = 'personal'; ok = $false; error = 'offline' }
$freshWork = New-Profile 'work' 42 17
$incoming = T @{ profiles = @($freshWork,$failure); fetchedAt = [DateTimeOffset]::Now.ToString('o') }
$merged = $M::Merge($previous,$incoming)
$retained = $M::DisplayProfile($merged,'personal')
Assert-Equal $retained['fiveHour']['remainingPercent'] 100 'Reordered profiles keep the correct previous account'
Assert-Equal $retained['stale'] $true 'Retained quota is explicitly stale'
Assert-Equal $retained['refreshError'] 'offline' 'Failure reason remains available'
Assert-Equal $retained['fetchedAt'] $personal['fetchedAt'] 'Failure never advances the successful-data timestamp'
Assert-Equal $M::DisplayProfile($merged,'work')['fiveHour']['remainingPercent'] 42 'Other account continues updating'
Assert-Equal $M::DisplayProfile($merged,'work')['stale'] $false 'Successful account is fresh'
Assert-Equal $personal.ContainsKey('stale') $false 'Merge does not mutate previous data'
$again = $M::Merge($merged,$incoming)
Assert-Equal $M::DisplayProfile($again,'personal')['fiveHour']['remainingPercent'] 100 'Repeated failures preserve last success'
$recovered = $M::Merge($again,(T @{ profiles = @((New-Profile 'personal' 80 90)); fetchedAt = [DateTimeOffset]::Now.ToString('o') }))
Assert-Equal $M::DisplayProfile($recovered,'personal')['stale'] $false 'Recovery clears stale state'
Assert-Equal $M::DisplayProfile($recovered,'personal')['refreshError'] '' 'Recovery clears previous error'
Assert-Equal $M::DisplayProfile($M::Merge($null,(T @{ profiles = @($failure) })),'personal')['ok'] $false 'First failure does not manufacture cached data'
$pendingIncoming = T @{ profiles = @(@{ id = 'personal'; ok = $false; pending = $true; error = '' }) }
$pendingMerged = $M::Merge((T @{ profiles = @($personal) }),$pendingIncoming)
Assert-Equal $pendingMerged['profiles'][0]['pending'] $true 'Pending state is retained separately from failures'
Assert-Equal $pendingMerged['profiles'][0]['stale'] $false 'Fresh pending values are not marked as failed'
Assert-Equal ($M::UpdateText($pendingMerged['profiles'][0],[DateTimeOffset]::Now) -match '正在读取') $true 'Update text identifies the pending account'
Assert-Equal $M::Stale($personal) $false 'Recent successful data is current'
$personal['fetchedAt'] = [DateTimeOffset]::Now.AddMinutes(-11).ToString('o')
Assert-Equal $M::Stale($personal) $true 'Data expires even without an explicit refresh error'
$personal['fetchedAt'] = 'invalid'
Assert-Equal $M::Stale($personal) $true 'Invalid timestamp is not considered fresh'
Assert-Equal $M::ResetText([DateTimeOffset]::Now.AddMinutes(-1).ToString('o')) '等待额度更新' 'Expired countdown does not claim a successful reset'
$now = [DateTimeOffset]([DateTimeOffset]::Now.Date.AddHours(12))
$zero = T @{ remainingPercent = 0; resetsAt = $now.AddHours(2).ToString('o') }
Assert-Equal $M::CompactRecovery($zero,$false,$now) '约 2小时0分后恢复' 'Exhausted quota shows local recovery time'
Assert-Equal $M::CompactRecovery($zero,$true,$now) '5h 恢复时间待确认' 'Stale zero does not promise a recovery time'
$zero['resetsAt'] = $now.AddHours(14).ToString('o')
Assert-Equal $M::CompactRecovery($zero,$false,$now) '约 14小时0分后恢复' 'Long recovery keeps hours'
$zero['resetsAt'] = $now.AddMinutes(-1).ToString('o')
Assert-Equal $M::CompactRecovery($zero,$false,$now) '5h 等待刷新确认' 'Expired reset does not invent replenishment'
$zero['resetsAt'] = 'invalid'
Assert-Equal $M::CompactRecovery($zero,$false,$now) '5h 恢复时间未知' 'Malformed reset remains unknown'
$zero['resetsAt'] = $null
Assert-Equal $M::CompactRecovery($zero,$false,$now) '5h 恢复时间未知' 'Absent reset remains unknown'
$zero['remainingPercent'] = 0.4
Assert-Equal $M::CompactRecovery($zero,$false,$now) '5h / 长周期 · 剩余' 'Positive fraction is not exhausted'
Assert-Equal $M::CompactRecovery($null,$false,$now) '5h / 长周期 · 剩余' 'Missing quota is not exhausted'
$zero['remainingPercent'] = 0
$zero['resetsAt'] = $now.AddMinutes(32).ToString('o')
Assert-Equal $M::CompactRecovery($zero,$false,$now) '约 32 分钟后恢复' 'Countdown updates from remaining time'
Assert-Equal $M::Enabled((T @{ id = 'personal' })) $true 'Legacy profiles remain enabled'
Assert-Equal $M::Enabled((T @{ enabled = $false })) $false 'Hidden accounts are disabled'
$due = New-Profile 'personal' 0 30
$due['fetchedAt'] = $now.ToString('o')
$due['fiveHour']['resetsAt'] = $now.AddSeconds(-1).ToString('o')
Assert-True (-not [string]::IsNullOrEmpty($M::RecoveryKey($due,$now))) 'Exhausted reset triggers a refresh key'

# ---------------------------------------------------------------- accounts + local usage
Assert-Equal ($M::AccountIds -join ',') 'personal,work,claude' 'Two Codex slots and one Claude slot'
Assert-Equal $M::MonitorWidth(1) 220 'Single-account width'
Assert-Equal $M::MonitorWidth(2) 360 'Two-account width'
Assert-Equal $M::MonitorWidth(3) 510 'Three accounts widen the strip'
Assert-Equal $M::ExpandedHeight(1) 108 'Single-account expanded height'
Assert-Equal $M::ExpandedHeight(3) 176 'Three accounts add one row'
Assert-Equal $M::DefaultAccountName('claude') 'Claude' 'Claude default name'
Assert-Equal $M::Tokens(999) '999' 'Small token counts'
Assert-Equal $M::Tokens(1250000) '1.3M' 'Million token counts'
Assert-Equal $M::Tokens($null) '—' 'Missing token count'
Assert-Equal $M::Usd(12.345) '$12.35' 'Cost formatting'
Assert-Equal $M::Usd(0.004) '<$0.01' 'Tiny positive cost is not zero'
$usage = T @{ today = @{ totalTokens = 2500000; costUsd = 4.5; inputTokens = 1; outputTokens = 2; cacheWriteTokens = 3; cacheReadTokens = 4; messages = 9 }; week = @{ totalTokens = 12000000; costUsd = 30 }; days = @(@{ date = [DateTime]::Today.ToString('yyyy-MM-dd'); totalTokens = 77; costUsd = 0 }); topModelToday = 'Opus 5.5' }
Assert-Equal $M::LocalUsageSummary($usage) '今日 2.5M tokens · ≈$4.50　近 7 天 12.0M · ≈$30.00' 'Local usage summary'
Assert-True ($M::LocalUsageDetail($usage,'codex') -match 'OpenAI') 'Codex detail cites OpenAI prices'
Assert-True ($M::LocalUsageDetail($null,'claude') -match 'Claude Code') 'Empty Claude detail is explicit'
$emptyUsage = T @{ today = @{ totalTokens = 0; costUsd = 0 }; week = @{ totalTokens = 0; costUsd = 0 }; days = @() }
Assert-True ($M::LocalUsageEmpty($emptyUsage) -and $M::LocalUsageEmpty($null) -and -not $M::LocalUsageEmpty($usage)) 'Empty seven-day local usage is detected'
Assert-True ($M::LocalUsageSummary($emptyUsage,'claude') -match '云端会话不计入') 'Empty Claude usage explains cloud sessions instead of 0 tokens'
Assert-True ($M::LocalUsageSummary($emptyUsage,'codex') -notmatch '0 tokens') 'Empty Codex usage does not show 0 tokens'
Assert-Equal $M::LocalUsageSummary($usage,'claude') $M::LocalUsageSummary($usage) 'Non-empty usage keeps the token summary'
Assert-True ($M::LocalUsageDetail($emptyUsage,'claude') -match '云端') 'Empty Claude detail points to the quota'
$trend = $M::Trend($usage,[DateTime]::Today)
Assert-True ($trend.Values.Length -eq 7 -and $trend.Values[6] -eq 77 -and $trend.Labels[6] -eq '今' -and $trend.Highlight -eq 6) 'Seven-day trend ends today'
$claudeProfile = T @{ id = 'claude'; provider = 'claude'; ok = $true; fetchedAt = [DateTimeOffset]::Now.ToString('o'); fiveHour = @{ remainingPercent = 80 }; weekly = @{ remainingPercent = 40 } }
Assert-Equal $M::Pair($claudeProfile).LongTerm 40 'Claude long-term uses the all-model weekly window'
Assert-Equal $M::IsClaude($claudeProfile) $true 'Claude detection'
Assert-Equal $M::IsClaude($work) $false 'Codex profile is not Claude'
$migrationHome = Join-Path ([System.IO.Path]::GetTempPath()) ('claude-migration-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $migrationHome | Out-Null
try {
  $legacy = T @{ profiles = @(@{ id = 'personal'; label = '个人'; codexHome = '~/.codex-personal' }) }
  Assert-Equal $M::AddClaudeProfileIfMissing($legacy,$migrationHome,$null) $true 'Older configurations gain a Claude slot'
  Assert-Equal $legacy['profiles'].Length 2 'Existing profiles are retained'
  Assert-Equal $legacy['profiles'][1]['enabled'] $false 'Claude starts hidden when Claude Code is not set up'
  Assert-Equal $M::AddClaudeProfileIfMissing($legacy,$migrationHome,$null) $false 'Migration is idempotent'
  New-Item -ItemType Directory -Force -Path (Join-Path $migrationHome '.claude/projects') | Out-Null
  $legacy2 = T @{ profiles = @(@{ id = 'work' }) }
  [void]$M::AddClaudeProfileIfMissing($legacy2,$migrationHome,$null)
  Assert-Equal $legacy2['profiles'][1]['enabled'] $true 'Claude starts visible when Claude Code is already used'
  $profilesPath = Join-Path $migrationHome 'profiles.json'
  [CodexUsageDesktop.Profiles]::Save($legacy2,$profilesPath)
  $loaded = [CodexUsageDesktop.Profiles]::Load($profilesPath)
  Assert-Equal ([CodexUsageDesktop.Profiles]::ActiveIds($loaded) -join ',') 'work,claude' 'Profiles round-trip and active ids follow account order'
  Assert-Equal ([CodexUsageDesktop.Profiles]::Name($loaded,'work')) '工作' 'Missing labels fall back to defaults'
} finally { Remove-Item -Recurse -Force $migrationHome -ErrorAction SilentlyContinue }

# ---------------------------------------------------------------- settings compatibility
$legacySettings = T @{ ballX = 10; ballY = 20; compactOpacity = 10; monitorSize = 'huge'; dockHorizontal = 'middle'; notificationThresholds = @('30','x',15,15,120); clearMode = 'yes' }
$ui = [CodexUsageDesktop.UiSettings]::FromObject($legacySettings,440,840)
Assert-True ($ui.BallX -eq 10 -and $ui.CompactOpacity -eq 40 -and $ui.MonitorSize -eq 'small' -and $ui.DockHorizontal -eq 'none') 'Invalid UI settings fall back safely'
Assert-Equal ($ui.NotificationThresholds -join ',') '30,15' 'Thresholds are validated, de-duplicated and sorted'
Assert-True ($ui.ClearMode -and $ui.CompactMode -and -not $ui.NotificationsEnabled) 'Missing or invalid booleans keep defaults'
$roundTrip = [CodexUsageDesktop.UiSettings]::FromObject($J::Parse($J::Json($ui.ToObject())),1,1)
Assert-True ($roundTrip.BallY -eq 20 -and $roundTrip.PanelWidth -eq 440) 'UI settings round-trip'
$legacyRemote = [CodexUsageDesktop.RemoteSettings]::FromObject((T @{ enabled = $true; deviceId = 'old-device'; deviceName = 'Old PC'; pairKey = 'old-pair-key-123'; includeSummary = $false; relayUrl = 'https://ntfy.sh' }))
Assert-True ($legacyRemote.Enabled -and -not $legacyRemote.UsageSyncEnabled -and $legacyRemote.DeviceId -eq 'old-device') 'Legacy remote settings migrate with usage sync off'
Assert-True ($legacyRemote.Active) 'Completion-only settings activate the worker'
Assert-True ([CodexUsageDesktop.RemoteSettings]::FromObject((T @{ usageSyncEnabled = $true; pairKey = '123456789012'; deviceId = 'd' })).Active) 'Usage-only settings activate the worker'
Assert-True (-not [CodexUsageDesktop.RemoteSettings]::FromObject((T @{ pairKey = '123456789012'; deviceId = 'd' })).Active) 'Both features disabled keep the worker off'

# ---------------------------------------------------------------- snapping + scale
$snap = $B::Snap(-1908,120,244,44,-1920,0,0,1040,20,$true)
Assert-True ($snap.X -eq -1920 -and $snap.Horizontal -eq 'left') 'Negative-coordinate monitor snaps to its own left edge'
$snap = $B::Snap(-250,990,244,44,-1920,0,0,1040,20,$true)
Assert-True ($snap.X -eq -244 -and $snap.Y -eq 996 -and $snap.Vertical -eq 'bottom') 'Right/bottom edges use the working area'
$snap = $B::Snap(-1000,400,244,44,-1920,0,0,1040,20,$true)
Assert-True ($snap.X -eq -1000 -and $snap.Horizontal -eq 'none') 'Middle-of-screen drop releases the anchor'
$snap = $B::Snap(-1908,120,244,44,-1920,0,0,1040,20,$false)
Assert-Equal $snap.X -1908 'Snap toggle disables attraction'
$snap = $B::Snap(-3000,1200,244,44,-1920,0,0,1040,20,$false)
Assert-True ($snap.X -eq -1920 -and $snap.Y -eq 996) 'Off-screen position still clamps'
$snap = $B::Snap(50,20,244,44,0,0,100,30,20,$true)
Assert-True ($snap.X -eq 0 -and $snap.Y -eq 0) 'Oversized window does not produce inverted bounds'
Assert-Equal $B::FittedScale(1.4,2,60,328,1920,1040) 1.4 'Roomy display preserves the preferred size'
$fit = $B::FittedScale(1.4,2,60,328,1024,720)
Assert-True ($fit -lt 1.4 -and [Math]::Round(328 * 2 * $fit) -le 720) 'Large rings fit a small high-DPI display'
Assert-True ([Math]::Round(360 * 2 * $B::FittedScale(1.4,2,360,64,768,800)) -le 768) 'Compact width fits a narrow display'
Assert-Equal $B::FittedScale(1.4,2,60,328,0,0) 1.4 'Missing screen information preserves the preference'

# ---------------------------------------------------------------- low-quota alerts
$script:Clock = [DateTimeOffset]::Now
$script:Tick = 0
$script:Reset = $script:Clock.AddHours(2).ToString('o')
function Sample($Value,[string]$Id = 'personal') {
  $script:Tick++
  return ,(T @{ profiles = @(@{ id = $Id; label = $Id; accountId = 'fixture'; ok = $true; fetchedAt = $script:Clock.AddSeconds(-120 + $script:Tick).ToString('o'); fiveHour = @{ remainingPercent = $Value; resetsAt = $script:Reset } }) })
}
function Alerts($Data,$State,[bool]$Enabled = $true,[bool]$Baseline = $false) { return @($B::Alerts($Data,$State,[int[]]@(20,10),$Enabled,$Baseline,$script:Clock)) }
$state = New-Object 'System.Collections.Generic.Dictionary[string,CodexUsageDesktop.AlertEntry]'
Assert-Equal (Alerts (Sample 80) $state).Count 0 'First sample establishes a baseline'
$found = Alerts (Sample 19) $state
Assert-True ($found.Count -eq 1 -and $found[0].Threshold -eq 20 -and $found[0].Window -eq 'fiveHour') 'Crossing 20 triggers once with window and threshold'
Assert-Equal (Alerts (Sample 18) $state).Count 0 'Refresh below the same threshold is silent'
Assert-Equal (Alerts (Sample 10) $state).Count 1 'Crossing 10 triggers independently'
[void](Alerts (Sample 80) $state)
Assert-Equal (Alerts (Sample 5) $state).Count 0 'Rebound within the same cycle does not repeat fired thresholds'
$restored = $B::AlertStateFromJson($B::AlertStateToJson($state))
[void](Alerts (Sample 80) $restored)
Assert-Equal (Alerts (Sample 5) $restored).Count 0 'Deduplication survives JSON round-trip/restart'
$script:Reset = $script:Clock.AddHours(5).ToString('o')
Assert-Equal (Alerts (Sample 90) $state).Count 0 'New reset window establishes a new baseline'
$found = Alerts (Sample 5) $state
Assert-True ($found.Count -eq 1 -and $found[0].Threshold -eq 10) 'A multi-threshold drop reports the more severe level once'
Assert-Equal $state['personal|fixture|fiveHour'].Fired.Count 2 'Both crossed thresholds are remembered'
Assert-Equal (Alerts (Sample 80 'work') $state).Count 0 'A second account has its own baseline'
Assert-Equal (Alerts (Sample 19 'work') $state).Count 1 'Account notification state is independent'
$state = New-Object 'System.Collections.Generic.Dictionary[string,CodexUsageDesktop.AlertEntry]'
[void](Alerts (Sample 80) $state)
$bad = Sample 5; $bad['profiles'][0]['ok'] = $false
Assert-Equal (Alerts $bad $state).Count 0 'Failed reads never notify'
Assert-Equal $state['personal|fixture|fiveHour'].Value 80 'Failed reads cannot modify the baseline'
$bad = Sample 5; $bad['profiles'][0]['stale'] = $true
Assert-Equal (Alerts $bad $state).Count 0 'Merged stale display data never notifies'
$bad = Sample 5; $bad['profiles'][0]['fetchedAt'] = $script:Clock.AddMinutes(-11).ToString('o')
Assert-Equal (Alerts $bad $state).Count 0 'Old timestamps never notify'
$bad = Sample 5; $bad['profiles'][0]['fiveHour']['resetsAt'] = $script:Clock.AddMinutes(-1).ToString('o')
Assert-Equal (Alerts $bad $state).Count 0 'Already-expired reset windows never notify'
Assert-Equal (Alerts (Sample $null) $state).Count 0 'Missing quota is not treated as zero'
Assert-Equal (Alerts (Sample 101) $state).Count 0 'Invalid percentages are ignored'
Assert-Equal (Alerts (Sample 0) $state).Count 1 'Real zero remaining can notify'
$state = New-Object 'System.Collections.Generic.Dictionary[string,CodexUsageDesktop.AlertEntry]'
[void](Alerts (Sample 80) $state)
Assert-Equal (Alerts (Sample 19) $state $false).Count 0 'Notifications are silent when disabled'
Assert-Equal (Alerts (Sample 9) $state $true $true).Count 0 'Enabling notifications does not alert on old low values'
Assert-Equal (Alerts (Sample 8) $state).Count 0 'Remaining below the threshold does not repeat'
$state = New-Object 'System.Collections.Generic.Dictionary[string,CodexUsageDesktop.AlertEntry]'
$first = Sample 80
[void](Alerts $first $state)
$first['profiles'][0]['fiveHour']['remainingPercent'] = 5
Assert-Equal (Alerts $first $state).Count 0 'Replaying the same timestamp cannot fabricate a crossing'
$fresh = Sample 5
$fresh['profiles'][0]['individualLimit'] = (T @{ limit = 0; remainingPercent = 0 }).psobject.BaseObject
[void](Alerts $fresh $state)
Assert-Equal $state.ContainsKey('personal|fixture|individualLimit') $false 'Meaningless monthly limits are excluded'

# ---------------------------------------------------------------- recovery + read-failure notifications
$H = [CodexUsageDesktop.AccountHealth]
$health = New-Object 'System.Collections.Generic.Dictionary[string,CodexUsageDesktop.HealthEntry]'
$hnow = [DateTimeOffset]::Now
function HSample([DateTimeOffset]$At,$Five,[bool]$Ok = $true,[string]$Error = '') {
  if ($Ok) { return ,(T @{ profiles = @(@{ id = 'personal'; ok = $true; fetchedAt = $At.ToString('o'); fiveHour = @{ remainingPercent = $Five }; weekly = @{ remainingPercent = 50 } }) }) }
  return ,(T @{ profiles = @(@{ id = 'personal'; ok = $false; error = $Error }) })
}
$name = [Func[string,string]]{ param($id) '个人' }
Assert-Equal @($H::Track((HSample $hnow 0),$health,$name,$true,$true,$hnow)).Count 0 'The first sample only sets a baseline'
Assert-Equal @($H::Track((HSample $hnow 5),$health,$name,$true,$true,$hnow)).Count 0 'Replaying the same timestamp cannot fabricate a recovery'
$rec = @($H::Track((HSample $hnow.AddMinutes(1) 37),$health,$name,$true,$true,$hnow.AddMinutes(1)))
Assert-True ($rec.Count -eq 1 -and $rec[0].Kind -eq 'recovered' -and $rec[0].Text -match '5 小时额度已恢复，剩余 37%') 'Exhausted 5h quota that replenishes notifies once'
Assert-Equal @($H::Track((HSample $hnow.AddMinutes(2) 30),$health,$name,$true,$true,$hnow.AddMinutes(2))).Count 0 'Ordinary decreases are silent'
[void]$H::Track((HSample $hnow.AddMinutes(3) 0),$health,$name,$false,$true,$hnow.AddMinutes(3))
Assert-Equal @($H::Track((HSample $hnow.AddMinutes(4) 60),$health,$name,$false,$true,$hnow.AddMinutes(4))).Count 0 'Recovery notifications can be turned off'
$f0 = $hnow.AddMinutes(10)
Assert-Equal @($H::Track((HSample $f0 0 $false 'Claude 登录已过期：打开 Claude Code 运行一次'),$health,$name,$true,$true,$f0)).Count 0 'A short failure is silent'
Assert-Equal @($H::Track((HSample $f0 0 $false 'x'),$health,$name,$true,$true,$f0.AddMinutes(14))).Count 0 'Fourteen minutes is still silent'
$fail = @($H::Track((HSample $f0 0 $false 'Claude 登录已过期：打开 Claude Code 运行一次'),$health,$name,$true,$true,$f0.AddMinutes(15)))
Assert-True ($fail.Count -eq 1 -and $fail[0].Kind -eq 'failure' -and $fail[0].Text -match '个人' -and $fail[0].Text -match 'Claude Code 运行一次') 'Fifteen minutes of failures notifies once with a recovery hint'
Assert-Equal @($H::Track((HSample $f0 0 $false 'x'),$health,$name,$true,$true,$f0.AddMinutes(40))).Count 0 'One failure episode notifies once'
Assert-Equal @($H::Track((HSample $f0.AddMinutes(41) 80),$health,$name,$true,$true,$f0.AddMinutes(41))).Count 0 'A single good read after a failure is not yet a reconnection'
Assert-Equal @($H::Track((HSample $f0 0 $false 'x'),$health,$name,$true,$true,$f0.AddMinutes(42))).Count 0 'Flapping reads stay inside one failure episode'
Assert-Equal @($H::Track((HSample $f0 0 $false 'x'),$health,$name,$true,$true,$f0.AddMinutes(60))).Count 0 'Flapping reads do not notify again'
[void]$H::Track((HSample $f0.AddMinutes(61) 80),$health,$name,$true,$true,$f0.AddMinutes(61))
$back = @($H::Track((HSample $f0.AddMinutes(71) 79),$health,$name,$true,$true,$f0.AddMinutes(71)))
Assert-True ($back.Count -eq 1 -and $back[0].Kind -eq 'restored') 'A notified failure reports reconnection after ten minutes of good reads'
Assert-Equal @($H::Track((HSample $f0 0 $false 'x'),$health,$name,$true,$true,$f0.AddMinutes(72))).Count 0 'A new failure starts silently'
Assert-Equal @($H::Track((HSample $f0 0 $false 'x'),$health,$name,$true,$true,$f0.AddMinutes(100))).Count 0 'A repeated failure within 12 hours is not notified again'
$coolHealth = New-Object 'System.Collections.Generic.Dictionary[string,CodexUsageDesktop.HealthEntry]'
[void]$H::Track((HSample $f0 0 $false 'Claude 用量接口暂时限流，稍后会自动重试。'),$coolHealth,$name,$true,$true,$f0)
Assert-Equal @($H::Track((HSample $f0 0 $false 'Claude 用量接口暂时限流，稍后会自动重试。'),$coolHealth,$name,$true,$true,$f0.AddMinutes(30))).Count 0 'Rate limiting never counts as a read failure'
$offHealth = New-Object 'System.Collections.Generic.Dictionary[string,CodexUsageDesktop.HealthEntry]'
[void]$H::Track((HSample $f0 0 $false 'x'),$offHealth,$name,$true,$false,$f0.AddMinutes(50))
Assert-Equal @($H::Track((HSample $f0 0 $false 'x'),$offHealth,$name,$true,$false,$f0.AddMinutes(70))).Count 0 'Failure notifications can be turned off'
Assert-Equal @($H::Track((T @{ profiles = @(@{ id = 'personal'; ok = $false; pending = $true }) }),$health,$name,$true,$true,$f0.AddMinutes(90))).Count 0 'Pending reads are neither failures nor successes'
Assert-True ($H::FailureHint('未找到 Codex CLI') -match '账号与首次使用') 'Codex CLI failures point to setup'
$channelSettings = [CodexUsageDesktop.UiSettings]::FromObject((T @{ updateChannel = 'beta'; recoveryNotifications = $false }),440,840)
Assert-True ($channelSettings.UpdateChannel -eq 'beta' -and -not $channelSettings.RecoveryNotifications -and $channelSettings.FailureNotifications) 'Channel and notification switches persist; defaults are on'
Assert-Equal ([CodexUsageDesktop.UiSettings]::FromObject((T @{ updateChannel = 'nightly' }),440,840)).UpdateChannel 'stable' 'Unknown channels fall back to stable'

# ---------------------------------------------------------------- burn-rate forecast
$base = [DateTimeOffset]::new(2026,11,16,12,0,0,[TimeSpan]::Zero)
$reset5 = $base.AddHours(3).ToString('o')
function New-History { return ,(New-Object 'System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[CodexUsageDesktop.UsageSample]]') }
function Add-Sample($History,[DateTimeOffset]$At,$Five,[string]$Reset = $reset5) {
  $F::AddSamples($History,(T @{ profiles = @(@{ id = 'personal'; ok = $true; fetchedAt = $At.ToString('o'); fiveHour = @{ remainingPercent = $Five; resetsAt = $Reset }; weekly = @{ remainingPercent = 60; resetsAt = $base.AddDays(3).ToString('o') } }) }),$At)
}
function Window($Remaining,$Reset = $reset5) { return ,(T @{ remainingPercent = $Remaining; resetsAt = $Reset }) }
$history = New-History
for ($m = 0; $m -le 20; $m++) { Add-Sample $history $base.AddMinutes($m) (80 - $m * 0.5) }
$fnow = $base.AddMinutes(20)
Add-Sample $history $base.AddMinutes(20).AddSeconds(10) 70
Assert-Equal $history['personal|fiveHour'].Count 21 'Samples closer than 50 seconds are not duplicated'
$forecast = $F::Burn($history,'personal','fiveHour',(Window 70),$fnow)
Assert-True ($forecast.State -eq 'exhaust' -and [Math]::Round($forecast.RatePerHour) -eq 30 -and [Math]::Round($forecast.MinutesLeft) -eq 140 -and $forecast.Text -match '用完') 'Steady 30%/h consumption predicts exhaustion before reset'
Assert-Equal $F::Burn($history,'personal','fiveHour',(Window 70 $base.AddMinutes(60).ToString('o')),$fnow).State 'none' 'Other reset cycles are not mixed in'
$safe = New-History
for ($m = 0; $m -le 20; $m++) { Add-Sample $safe $base.AddMinutes($m) (80 - $m * 0.5) $base.AddMinutes(40).ToString('o') }
Assert-Equal $F::Burn($safe,'personal','fiveHour',(Window 70 $base.AddMinutes(40).ToString('o')),$fnow).State 'safe' 'Quota that lasts until reset is safe'
Assert-Equal $F::Burn($history,'personal','weekly',(Window 60 $base.AddDays(3).ToString('o')),$fnow).State 'none' 'Weekly needs a longer span'
$idle = New-History
for ($m = 0; $m -le 15; $m++) { Add-Sample $idle $base.AddMinutes($m) 90 }
Assert-Equal $F::Burn($idle,'personal','fiveHour',(Window 90),$base.AddMinutes(15)).State 'idle' 'No consumption is idle'
$jump = New-History
for ($m = 0; $m -le 12; $m++) { Add-Sample $jump $base.AddMinutes($m) (20 - $m) $null }
Add-Sample $jump $base.AddMinutes(13) 100 $null
Assert-Equal $F::Burn($jump,'personal','fiveHour',(Window 100 $null),$base.AddMinutes(13)).State 'none' 'A jump up starts a new forecast window'
Assert-Equal $F::Burn($history,'personal','fiveHour',(Window 0),$fnow).State 'empty' 'Exhausted quota uses the recovery countdown'
$failedRead = T @{ profiles = @(@{ id = 'work'; ok = $false; fetchedAt = $fnow.ToString('o'); fiveHour = @{ remainingPercent = 1 } }) }
$F::AddSamples($history,$failedRead,$fnow)
Assert-Equal $history.ContainsKey('work|fiveHour') $false 'Failed reads never become samples'
$reloaded = $F::FromJson($F::ToJson($history))
Assert-True ($reloaded['personal|fiveHour'].Count -eq 21 -and $F::Burn($reloaded,'personal','fiveHour',(Window 70),$fnow).State -eq 'exhaust') 'History survives a restart'
Assert-True ($F::Detail($forecast) -match '30%/小时') 'Detail shows the rate'

# ---------------------------------------------------------------- crypto (wire compatibility with v0.7+ and the PWA)
$vector = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures\mobile-crypto-vector.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Equal $C::RemoteTopic($vector.pairKey) $vector.completionTopic 'Completion topic stays byte-for-byte stable'
Assert-Equal $C::MobileTopic($vector.pairKey) $vector.usageTopic 'Usage topic matches the shared vector'
Assert-True ($C::MobileTopic($vector.pairKey + '-other') -ne $vector.usageTopic -and $C::MobileTopic($vector.pairKey) -ne $C::RemoteTopic($vector.pairKey)) 'Topics differ per key and per purpose'
$iv = New-Object byte[] 16
for ($i = 0; $i -lt 16; $i++) { $iv[$i] = [byte]$i }
Assert-Equal $C::Protect($vector.plainText,$vector.pairKey,$iv) $vector.cipherText 'Deterministic encryption matches the cross-runtime fixture'
Assert-Equal $C::Unprotect($vector.cipherText,$vector.pairKey) $vector.plainText 'The fixture decrypts'
$cipher = $C::Protect('{"hello":"remote Codex","value":42}','pair-key-test-123456789')
Assert-Equal $C::Unprotect($cipher,'pair-key-test-123456789') '{"hello":"remote Codex","value":42}' 'Random-IV payload round-trips'
$bytes = [Convert]::FromBase64String($cipher); $bytes[20] = $bytes[20] -bxor 1
$failed = $false
try { [void]$C::Unprotect([Convert]::ToBase64String($bytes),'pair-key-test-123456789') } catch { $failed = $true }
Assert-True $failed 'Tampered payload fails authentication'
Assert-True ($C::RemoteTopic('k') -match '^codex-[a-f0-9]{48}$') 'Topic does not expose the pair key'
Assert-True ($C::NewPairKey().Length -ge 22 -and $C::NewPairKey() -ne $C::NewPairKey()) 'Generated pair keys are long and random'
$rejected = $false
try { [void]$C::RelayUrl('http://example.com') } catch { $rejected = $true }
Assert-True $rejected 'Non-local HTTP relay is rejected'
Assert-Equal $C::RelayUrl('http://localhost:8080/') 'http://localhost:8080' 'Localhost HTTP is allowed and trimmed'
Assert-Equal $C::RelayUrl('') 'https://ntfy.sh' 'Default relay'

# ---------------------------------------------------------------- mobile snapshot
$snapNow = [DateTimeOffset]::Parse('2026-09-28T12:00:00Z')
$settings = New-Object CodexUsageDesktop.RemoteSettings
$settings.DeviceId = 'device-fixture'; $settings.DeviceName = '测试电脑'; $settings.UsageSyncEnabled = $true; $settings.PairKey = 'pair-key-123456789'
$mp = T @{ id = 'personal'; label = 'Personal'; enabled = $true; ok = $true; stale = $false; fetchedAt = $snapNow.AddSeconds(-30).ToString('o'); codexHome = 'C:\Users\secret\.codex-personal'; accountId = 'account-secret'; accessToken = 'token-secret'
  fiveHour = @{ remainingPercent = 82; resetsAt = $snapNow.AddHours(2).ToString('o') }; weekly = @{ remainingPercent = 63; resetsAt = $snapNow.AddDays(4).ToString('o') }; individualLimit = $null
  localUsage = @{ today = @{ totalTokens = 1234567.8; costUsd = 3.456; inputTokens = 1 }; week = @{ totalTokens = 9000000; costUsd = 20 }; topModelToday = 'GPT-5.5'; days = @() } }
$mw = T @{ id = 'work'; label = 'Work'; ok = $true; stale = $false; fetchedAt = $snapNow.ToString('o'); fiveHour = @{ remainingPercent = 0.4 }; weekly = @{ remainingPercent = 45 }; individualLimit = @{ limit = 100; remainingPercent = 27; resetsAt = $null } }
$disabled = T @{ id = 'other'; label = 'Disabled'; enabled = $false; ok = $true }
$snapshot = $Mob::Snapshot((T @{ profiles = @($mp,$mw,$disabled) }),$settings,$snapNow)
Assert-Equal $snapshot['profiles'].Length 2 'Disabled and unknown accounts are not published'
Assert-True ($snapshot['profiles'][1]['longTerm']['kind'] -eq 'workspace' -and $snapshot['profiles'][1]['longTerm']['remainingPercent'] -eq 27) 'Workspace wins when more limiting'
Assert-Equal $snapshot['profiles'][1]['fiveHour']['remainingPercent'] 0.4 'Sub-percent values are preserved'
Assert-True ($snapshot['profiles'][0]['localUsage']['today']['totalTokens'] -eq 1234567 -and $snapshot['profiles'][0]['localUsage']['today']['costUsd'] -eq 3.46) 'Local usage reduces to rounded aggregates'
$json = $J::Json($snapshot)
foreach ($forbidden in @('codexHome','accountId','accessToken','token-secret','C:\\Users\\secret','inputTokens','topModelToday','GPT-5.5','Disabled')) {
  Assert-True (-not $json.Contains($forbidden)) ('Snapshot must not contain ' + $forbidden)
}
$mp['stale'] = $true
Assert-Equal $Mob::Snapshot((T @{ profiles = @($mp) }),$settings,$snapNow)['profiles'][0]['ok'] $false 'Stale desktop data is unavailable on mobile'
$mp['stale'] = $false
Assert-True ($null -eq $Mob::LongTerm((T @{ weekly = $null; individualLimit = $null }))) 'Missing long-term windows remain missing'
Assert-Equal $Mob::LongTerm((T @{ weekly = $null; individualLimit = @{ limit = 1; remainingPercent = 0 } }))['remainingPercent'] 0 'Workspace zero is not missing'
$hash = $Mob::Hash($snapshot)
$later = $Mob::Snapshot((T @{ profiles = @($mp,$mw) }),$settings,$snapNow.AddMinutes(1))
Assert-Equal $Mob::Hash($later) $hash 'Timestamps do not defeat duplicate suppression'
$pstate = T @{ lastPublishedHash = $hash; lastPublishedAt = $snapNow.ToString('o') }
Assert-True (-not $Mob::PublishDue($hash,$pstate,$snapNow.AddMinutes(4),5,'')) 'Unchanged data is suppressed before the heartbeat'
Assert-True ($Mob::PublishDue($hash,$pstate,$snapNow.AddMinutes(5),5,'')) 'Heartbeat publishes after five minutes'
Assert-True ($Mob::PublishDue(('0' * 64),$pstate,$snapNow.AddMinutes(1),5,'')) 'Changed data publishes immediately'
Assert-True ($Mob::PublishDue($hash,$pstate,$snapNow.AddMinutes(1),5,'new-topic')) 'Pair-key rotation publishes to the new topic'

# ---------------------------------------------------------------- completions + background worker (fake relay, no network)
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('codex-native-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
try {
  $session = Join-Path $temp 'rollout-test.jsonl'
  $cnow = [DateTimeOffset]::UtcNow
  $lines = @(
    ('{"timestamp":"' + $cnow.AddSeconds(-5).ToString('o') + '","type":"session_meta","payload":{"cwd":"C:\\work\\tft-cn-companion"}}'),
    ('{"timestamp":"' + $cnow.ToString('o') + '","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-123","last_agent_message":"build completed and tests passed","error":null}}')
  )
  [System.IO.File]::WriteAllLines($session,$lines,(New-Object System.Text.UTF8Encoding($false)))
  $events = @([CodexUsageDesktop.Completions]::FromFile($session,$cnow.AddMinutes(-1),'pc-a','Office-PC',$true))
  Assert-True ($events.Count -eq 1 -and $events[0]['id'] -eq 'pc-a:turn-123' -and $events[0]['project'] -eq 'tft-cn-companion' -and $events[0]['status'] -eq 'completed') 'task_complete becomes one deduplicable event with the project name'
  Assert-Equal $events[0]['summary'] 'build completed and tests passed' 'Summary opt-in includes the final response'
  Assert-Equal @([CodexUsageDesktop.Completions]::FromFile($session,$cnow.AddMinutes(-1),'pc-a','Office-PC',$false))[0]['summary'] '' 'Summary is omitted by default'
  Assert-Equal @([CodexUsageDesktop.Completions]::FromFile($session,$cnow.AddMinutes(1),'pc-a','Office-PC',$true)).Count 0 'Old completions do not notify after startup'

  $profileHome = Join-Path $temp 'profile-codex-home'
  [System.IO.File]::WriteAllText((Join-Path $temp 'profiles.json'),('{"profiles":[{"id":"personal","codexHome":"' + $profileHome.Replace('\','\\') + '","enabled":true},{"id":"claude","provider":"claude","claudeHome":"~/.claude","enabled":true}]}'))
  $worker = New-Object CodexUsageDesktop.RemoteWorker -ArgumentList $temp
  Assert-True (@($worker.CodexHomes()) -contains $profileHome) 'Configured Codex homes are watched; Claude is not a Codex home'

  [CodexUsageDesktop.FakeRelay]::Install()
  $worker.LoopDelayMs = 50
  $usageOnly = New-Object CodexUsageDesktop.RemoteSettings
  $usageOnly.UsageSyncEnabled = $true; $usageOnly.PairKey = '123456789012-worker'; $usageOnly.DeviceId = 'worker-device'; $usageOnly.DeviceName = 'Fixture PC'
  $usageOnly.Save($worker.SettingsPath)
  [void][CodexUsageDesktop.RemoteWorker]::WritePending((T @{ profiles = @($mp) }),$usageOnly,$worker.MobilePendingPath)
  $worker.Start()
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
  while (@([CodexUsageDesktop.FakeRelay]::Snapshot()).Count -eq 0 -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
  $requests = @([CodexUsageDesktop.FakeRelay]::Snapshot())
  Assert-True ($requests.Count -ge 1 -and $requests[0][0] -eq 'POST' -and $requests[0][1] -eq ('https://ntfy.sh/' + $C::MobileTopic($usageOnly.PairKey))) 'Usage-only worker publishes the pending snapshot to the mobile topic'
  $published = $J::Parse($C::Unprotect($requests[0][2],$usageOnly.PairKey))
  Assert-True ($published['type'] -eq 'usage_snapshot' -and $published['profiles'][0]['id'] -eq 'personal') 'The published snapshot decrypts with the pairing key'
  Assert-True (-not ($requests | Where-Object { $_[0] -eq 'GET' })) 'Usage-only mode never polls the completion relay'
  Start-Sleep -Milliseconds 400
  Assert-Equal @([CodexUsageDesktop.FakeRelay]::Snapshot()).Count 1 'Unchanged snapshots are not re-published before the heartbeat'
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
  while ([DateTimeOffset]::UtcNow -lt $deadline -and -not ((Test-Path $worker.MobileStatusPath) -and ($J::Parse([IO.File]::ReadAllText($worker.MobileStatusPath))["state"] -eq "running"))) { Start-Sleep -Milliseconds 50 }
  Assert-Equal $J::Parse([IO.File]::ReadAllText($worker.MobileStatusPath))['state'] 'running' 'Mobile status reports a healthy sync'

  # Switching to completion mode restarts the session from the settings file.
  $peer = 'peer-device'
  $foreign = $C::Protect($J::Json((T @{ version = 1; id = ($peer + ':turn-9'); deviceId = $peer; deviceName = 'Laptop'; turnId = 'turn-9'; project = 'demo'; status = 'completed'; finishedAt = [DateTimeOffset]::UtcNow.ToString('o'); summary = '' })),$usageOnly.PairKey)
  $own = $C::Protect($J::Json((T @{ version = 1; id = 'worker-device:turn-1'; deviceId = 'worker-device'; turnId = 'turn-1' })),$usageOnly.PairKey)
  [CodexUsageDesktop.FakeRelay]::GetResponse = ('{"event":"open"}' + "`n" + $J::Json((T @{ event = 'message'; id = 'relay-1'; message = $foreign })) + "`n" + $J::Json((T @{ event = 'message'; id = 'relay-2'; message = $own })) + "`n" + $J::Json((T @{ event = 'message'; id = 'relay-3'; message = 'garbage' })))
  Start-Sleep -Milliseconds 1100
  $usageOnly.Enabled = $true
  $usageOnly.Save($worker.SettingsPath)
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
  while (-not (Test-Path $worker.InboxPath) -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
  Start-Sleep -Milliseconds 300
  $inbox = @(Get-Content -LiteralPath $worker.InboxPath -Encoding UTF8 | Where-Object { $_ })
  Assert-True ($inbox.Count -eq 1 -and $inbox[0].Contains('peer-device:turn-9')) 'Relay messages from other devices reach the inbox; own and undecryptable messages are ignored'
  $gets = @([CodexUsageDesktop.FakeRelay]::Snapshot() | Where-Object { $_[0] -eq 'GET' })
  Assert-True ($gets.Count -ge 1 -and $gets[0][1] -eq ('https://ntfy.sh/' + $C::RemoteTopic($usageOnly.PairKey) + '/json?poll=1&since=12s')) 'Completion mode polls the completion topic'
  $sessionsDir = Join-Path $profileHome 'sessions\2026\11\16'
  New-Item -ItemType Directory -Force -Path $sessionsDir | Out-Null
  [System.IO.File]::WriteAllLines((Join-Path $sessionsDir 'rollout-live.jsonl'),@(('{"timestamp":"' + [DateTimeOffset]::UtcNow.AddSeconds(1).ToString('o') + '","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-live","error":null}}')),(New-Object System.Text.UTF8Encoding($false)))
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
  $completion = $null
  while ($null -eq $completion -and [DateTimeOffset]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 100
    foreach ($request in @([CodexUsageDesktop.FakeRelay]::Snapshot())) {
      if ($request[0] -eq 'POST' -and $request[1].EndsWith($C::RemoteTopic($usageOnly.PairKey))) { $completion = $J::Parse($C::Unprotect($request[2],$usageOnly.PairKey)) }
    }
  }
  Assert-True ($null -ne $completion -and $completion['id'] -eq 'worker-device:turn-live' -and $completion['summary'] -eq '') 'A new local task_complete is published without a summary by default'
  $worker.Stop()
  Assert-True (-not $worker.Running) 'The worker thread stops'
  $usageOnly.Enabled = $false; $usageOnly.UsageSyncEnabled = $false
  $usageOnly.Save($worker.SettingsPath)
  $worker.Start()
  Start-Sleep -Milliseconds 300
  Assert-True (-not $worker.Running -and $J::Parse([IO.File]::ReadAllText($worker.StatusPath))['state'] -eq 'disabled') 'Disabled settings stop the worker and report it'
  Assert-True (-not [CodexUsageDesktop.RemoteWorker]::WritePending((T @{ profiles = @($mp) }),$usageOnly,(Join-Path $temp 'none.json')) -and -not (Test-Path (Join-Path $temp 'none.json'))) 'Disabled sync queues nothing'
} finally {
  try { $worker.Stop() } catch {}
  Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- the client no longer depends on PowerShell at runtime
$clientSource = Get-Content -LiteralPath (Join-Path $root 'desktop\Client.cs') -Raw -Encoding UTF8
$traySource = (Get-ChildItem (Join-Path $root 'desktop') -Filter 'TrayApp*.cs' | ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }) -join "`n"
Assert-True (-not $clientSource.Contains('powershell.exe') -and -not $traySource.Contains('powershell')) 'The packaged client never starts PowerShell for the UI or worker'
Assert-True (-not (Test-Path (Join-Path $root 'tray.ps1')) -and -not (Test-Path (Join-Path $root 'remote-worker.ps1'))) 'PowerShell UI and worker scripts are retired'
Write-Output "Native model: $script:Checks checks passed."
