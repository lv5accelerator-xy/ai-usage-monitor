$ErrorActionPreference = 'Stop'
# Offline tests for the shared Claude reader: no network, no real credentials.
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'usage-reader.ps1')
Import-ClaudeReader -Root $root
$script:Checks = 0
function Assert-Claude([bool]$Condition,[string]$Message) {
  if (-not $Condition) { throw $Message }
  $script:Checks++
}
$reader = [CodexUsageDesktop.ClaudeReader]
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('claude-reader-' + [Guid]::NewGuid().ToString('N'))
$home1 = Join-Path $temp 'claude-home'
$projects = Join-Path $home1 'projects\D--work-app'
$data = Join-Path $temp 'data'
New-Item -ItemType Directory -Force -Path $projects,$data | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$originalHttp = $reader::HttpGet
try {
  # ---- quota parsing
  $usageJson = '{"five_hour":{"utilization":35.0,"resets_at":"2026-02-06T22:00:00+00:00"},"seven_day":{"utilization":114.0,"resets_at":"2026-02-12T20:00:00+00:00"},"seven_day_opus":null,"seven_day_sonnet":{"utilization":39.5,"resets_at":"2026-02-09T14:00:00+00:00"}}'
  $parsed = $reader::ParseUsage($usageJson)
  Assert-Claude ($parsed['fiveHour']['remainingPercent'] -eq 65) '5-hour utilization converts to remaining percent.'
  Assert-Claude ($parsed['fiveHour']['kind'] -eq '5h' -and $parsed['weekly']['kind'] -eq 'weekly') 'Claude windows reuse the Codex window kinds.'
  Assert-Claude ($parsed['weekly']['remainingPercent'] -eq 0) 'Over-limit utilization clamps to 0% remaining, never negative.'
  Assert-Claude ($parsed['fiveHour']['resetsAt'] -eq '2026-02-06T22:00:00.0000000+00:00') 'Reset timestamps normalize to UTC ISO-8601.'
  Assert-Claude (@($parsed['windows']).Count -eq 3) 'Null model-specific windows are omitted, present ones kept.'
  $rejected = $false
  try { [void]$reader::ParseUsage('{"seven_day_opus":null}') } catch { $rejected = $true }
  Assert-Claude $rejected 'A response without subscription windows is an error, not 100% remaining.'
  $rejected = $false
  try { [void]$reader::ParseUsage('<html>') } catch { $rejected = $true }
  Assert-Claude $rejected 'Non-JSON responses are rejected.'
  Assert-Claude ($reader::PlanLabel('max','default_claude_max_20x') -eq 'Max 20x') 'Plan label includes the Max multiplier.'
  Assert-Claude ($reader::PlanLabel('pro','') -eq 'Pro') 'Pro plan label.'
  Assert-Claude ($reader::StatusMessage(401) -match '/login') 'Expired login explains how to recover.'

  # ---- pricing
  $opus55 = $reader::Price('claude-opus-5-5')
  Assert-Claude ($opus55[0] -eq 4 -and $opus55[1] -eq 20 -and $opus55[2] -eq 5) 'Opus 5.5 list price with 1.25x cache write.'
  Assert-Claude (($reader::Price('claude-opus-4-1-20250805'))[0] -eq 15) 'Opus 4.1 legacy price.'
  Assert-Claude (($reader::Price('claude-opus-4-20250514'))[0] -eq 15) 'Dated Opus 4 id does not parse the date as a minor version.'
  Assert-Claude (($reader::Price('claude-opus-4-6'))[0] -eq 5) 'Opus 4.6 price.'
  Assert-Claude (($reader::Price('claude-sonnet-4-5-20250929'))[0] -eq 3) 'Sonnet 4.5 price.'
  Assert-Claude (($reader::Price('claude-sonnet-5-5'))[1] -eq 10) 'Sonnet 5.5 price.'
  Assert-Claude (($reader::Price('claude-3-5-haiku-20241022'))[0] -eq 0.8) 'Legacy family-after-version naming.'
  Assert-Claude (($reader::Price('<synthetic>'))[0] -eq 0) 'Synthetic messages cost nothing.'
  Assert-Claude ([Math]::Abs($reader::Cost('claude-sonnet-4-6',1000000,1000000,0,1000000) - 18.3) -lt 0.000001) 'Cost combines input, output and cache reads.'
  Assert-Claude ($reader::ShortModel('claude-opus-5-5') -eq 'Opus 5.5' -and $reader::ShortModel('claude-sonnet-4-20250514') -eq 'Sonnet 4') 'Short model names.'

  # ---- local logs
  $now = [DateTimeOffset]::Now
  $today = $now.LocalDateTime.Date
  function Line([string]$Id,[string]$Request,[DateTimeOffset]$At,[string]$Model,[int]$In,[int]$Out,[int]$CacheWrite = 0,[int]$CacheRead = 0) {
    return ('{"type":"assistant","requestId":"' + $Request + '","timestamp":"' + $At.ToUniversalTime().ToString('o') + '","message":{"id":"' + $Id + '","model":"' + $Model + '","content":[{"type":"text","text":"用量 ok"}],"usage":{"input_tokens":' + $In + ',"output_tokens":' + $Out + ',"cache_creation_input_tokens":' + $CacheWrite + ',"cache_read_input_tokens":' + $CacheRead + '}}}')
  }
  $todayNoon = New-Object DateTimeOffset ($today.AddHours(12))
  if ($todayNoon -gt $now) { $todayNoon = $now.AddMinutes(-1) }
  $session = Join-Path $projects 'session-a.jsonl'
  $lines = @(
    '{"type":"user","message":{"role":"user","content":"hi"}}',
    (Line 'msg_1' 'req_1' $todayNoon 'claude-sonnet-4-6' 1000 500 200 3000),
    # Streaming duplicate of the same message/request must count once.
    (Line 'msg_1' 'req_1' $todayNoon 'claude-sonnet-4-6' 1000 500 200 3000),
    (Line 'msg_2' 'req_2' $todayNoon.AddDays(-2) 'claude-opus-5-5' 2000 100),
    (Line 'msg_old' 'req_old' $todayNoon.AddDays(-30) 'claude-opus-5-5' 99999 99999),
    'not json {"usage" "assistant"',
    (Line 'msg_zero' 'req_zero' $todayNoon '<synthetic>' 0 0)
  )
  [System.IO.File]::WriteAllText($session,(($lines -join "`n") + "`n"),$utf8)
  # A resumed session copies an earlier message into a second file.
  $subagent = Join-Path $projects 'session-a\subagents\agent-1.jsonl'
  New-Item -ItemType Directory -Force -Path (Split-Path $subagent) | Out-Null
  [System.IO.File]::WriteAllText($subagent,((Line 'msg_1' 'req_1' $todayNoon 'claude-sonnet-4-6' 1000 500 200 3000) + "`n" + (Line 'msg_3' 'req_3' $todayNoon 'claude-haiku-4-5' 10 20) + "`n"),$utf8)
  $cache = Join-Path $data 'claude-local-cache.json'
  $local = $reader::ScanLocal((Join-Path $home1 'projects'),$cache,$today)
  Assert-Claude ($local['today']['messages'] -eq 2) 'Duplicate streaming lines and copied messages count once.'
  Assert-Claude ($local['today']['totalTokens'] -eq (1000+500+200+3000+30)) 'Today totals include cache tokens.'
  Assert-Claude ($local['week']['messages'] -eq 3) 'Seven-day window includes recent days and excludes older logs.'
  Assert-Claude (@($local['days']).Count -eq 7) 'Seven daily buckets are returned.'
  Assert-Claude ($local['topModelToday'] -eq 'Sonnet 4.6') 'Top model today is reported.'
  $expected = [CodexUsageDesktop.ClaudeReader]::Cost('claude-sonnet-4-6',1000,500,200,3000) + [CodexUsageDesktop.ClaudeReader]::Cost('claude-haiku-4-5',10,20,0,0)
  Assert-Claude ([Math]::Abs($local['today']['costUsd'] - $expected) -lt 0.0000001) 'Cost is computed per message model.'
  Assert-Claude (Test-Path -LiteralPath $cache) 'Local scan writes an aggregate cache.'
  $cacheText = [System.IO.File]::ReadAllText($cache)
  Assert-Claude (-not $cacheText.Contains('用量 ok') -and -not $cacheText.Contains('"text"')) 'Cache stores token counts only, never message content.'

  # Incremental read: a partial trailing line is not consumed until completed.
  $partial = Line 'msg_4' 'req_4' $todayNoon 'claude-sonnet-4-6' 7 7
  [System.IO.File]::AppendAllText($session,$partial.Substring(0,40),$utf8)
  $local = $reader::ScanLocal((Join-Path $home1 'projects'),$cache,$today)
  Assert-Claude ($local['today']['messages'] -eq 2) 'A line still being written is ignored.'
  [System.IO.File]::AppendAllText($session,$partial.Substring(40) + "`n",$utf8)
  $local = $reader::ScanLocal((Join-Path $home1 'projects'),$cache,$today)
  Assert-Claude ($local['today']['messages'] -eq 3 -and $local['today']['totalTokens'] -eq (1000+500+200+3000+30+14)) 'Appended lines are read incrementally from the cache offset.'
  $local = $reader::ScanLocal((Join-Path $home1 'projects'),$cache,$today)
  Assert-Claude ($local['today']['messages'] -eq 3) 'Unchanged files reuse cached entries without double counting.'
  $none = $reader::ScanLocal((Join-Path $temp 'missing'),$null,$today)
  Assert-Claude ($none['week']['messages'] -eq 0) 'A missing projects folder yields empty statistics.'

  # ---- Codex rollout logs
  $codexSessions = Join-Path $temp 'codex-home\sessions\2026\11\16'
  New-Item -ItemType Directory -Force -Path $codexSessions | Out-Null
  function CodexToken([DateTimeOffset]$At,[long]$Total,[long]$In,[long]$Cached,[long]$Out) {
    return ('{"timestamp":"' + $At.ToUniversalTime().ToString('o') + '","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1,"cached_input_tokens":0,"output_tokens":1,"reasoning_output_tokens":0,"total_tokens":' + $Total + '},"last_token_usage":{"input_tokens":' + $In + ',"cached_input_tokens":' + $Cached + ',"output_tokens":' + $Out + ',"reasoning_output_tokens":10,"total_tokens":' + ($In + $Out) + '},"model_context_window":258400},"rate_limits":{}}}')
  }
  $rollout = Join-Path $codexSessions 'rollout-a.jsonl'
  $codexLines = @(
    '{"type":"session_meta","payload":{"id":"s1","cwd":"D:\\secret-project"}}',
    '{"type":"turn_context","payload":{"model":"gpt-5.5","cwd":"D:\\secret-project"}}',
    (CodexToken $todayNoon 1500 1000 600 500),
    # Same cumulative total again: a repeated event, not a new call.
    (CodexToken $todayNoon 1500 1000 600 500),
    '{"type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{}}}',
    '{"type":"turn_context","payload":{"model":"gpt-5.4"}}',
    (CodexToken $todayNoon.AddMinutes(1) 3700 2000 0 200),
    (CodexToken $todayNoon.AddDays(-3) 9000 100 0 100)
  )
  [System.IO.File]::WriteAllText($rollout,(($codexLines -join "`n") + "`n"),$utf8)
  # A forked session copies an earlier event into another file.
  [System.IO.File]::WriteAllText((Join-Path $codexSessions 'rollout-fork.jsonl'),((CodexToken $todayNoon 1500 1000 600 500) + "`n"),$utf8)
  $codexCache = Join-Path $data 'codex-local-personal.json'
  $codex = $reader::ScanCodex((Join-Path $temp 'codex-home\sessions'),$codexCache,$today)
  Assert-Claude ($codex['today']['messages'] -eq 2) 'Codex: repeated and forked token_count events count once; null info is ignored.'
  Assert-Claude ($codex['today']['inputTokens'] -eq (400 + 2000) -and $codex['today']['cacheReadTokens'] -eq 600) 'Codex: cached input is split out of input.'
  Assert-Claude ($codex['today']['totalTokens'] -eq (1000 + 500 + 2000 + 200)) 'Codex: totals are input plus output (reasoning is part of output).'
  Assert-Claude ($codex['week']['messages'] -eq 3) 'Codex: seven-day window.'
  $codexCost = [CodexUsageDesktop.ClaudeReader]::Cost('gpt-5.5',400,500,0,600) + [CodexUsageDesktop.ClaudeReader]::Cost('gpt-5.4',2000,200,0,0)
  Assert-Claude ([Math]::Abs($codex['today']['costUsd'] - $codexCost) -lt 0.0000001) 'Codex: the model from turn_context prices each call.'
  Assert-Claude ($codex['topModelToday'] -eq 'GPT-5.4') 'Codex: top model today by tokens.'
  Assert-Claude ((($reader::Price('gpt-5.5'))[0] -eq 5) -and (($reader::Price('gpt-5.5'))[3] -eq 0.5) -and (($reader::Price('gpt-5.4'))[1] -eq 15)) 'OpenAI list prices.'
  $codexCacheText = [System.IO.File]::ReadAllText($codexCache)
  Assert-Claude (-not $codexCacheText.Contains('secret-project')) 'Codex cache keeps counts only, not working directories.'
  [System.IO.File]::AppendAllText($rollout,((CodexToken $todayNoon.AddMinutes(2) 3800 50 0 50) + "`n"),$utf8)
  $codex = $reader::ScanCodex((Join-Path $temp 'codex-home\sessions'),$codexCache,$today)
  Assert-Claude ($codex['today']['messages'] -eq 3 -and $codex['topModelToday'] -eq 'GPT-5.4') 'Codex: incremental reads keep the model and duplicate state from the cache.'

  # ---- profile: quota + cache + failures
  $profile = New-Object 'System.Collections.Generic.Dictionary[string,object]'
  $profile['id'] = 'claude'; $profile['label'] = 'Claude'; $profile['claudeHome'] = $home1
  $script:HttpCalls = 0
  $script:HttpStatus = 200
  $script:SeenHeaders = ''
  $reader::HttpGet = [Func[string,string,string]]{
    param($url,$token)
    $script:HttpCalls++
    $script:SeenToken = $token
    if ($script:HttpStatus -ne 200) { throw (New-Object CodexUsageDesktop.ClaudeHttpException -ArgumentList $script:HttpStatus,([CodexUsageDesktop.ClaudeReader]::StatusMessage($script:HttpStatus))) }
    return $usageJson
  }
  $localOnly = $reader::ReadProfile($profile,$data,$now)
  Assert-Claude ($localOnly['ok'] -and $localOnly['planType'] -eq '本地统计' -and $null -eq $localOnly['fiveHour']) 'Without a subscription login, local usage is still shown.'
  Assert-Claude ($script:HttpCalls -eq 0) 'No request is made without credentials.'
  $credentials = '{"claudeAiOauth":{"accessToken":"sk-ant-oat-fixture","refreshToken":"rt-fixture","expiresAt":1,"subscriptionType":"max","rateLimitTier":"default_claude_max_5x"}}'
  [System.IO.File]::WriteAllText((Join-Path $home1 '.credentials.json'),$credentials,$utf8)
  $first = $reader::ReadProfile($profile,$data,$now)
  Assert-Claude ($first['ok'] -and $first['provider'] -eq 'claude' -and $first['planType'] -eq 'Max 5x') 'Logged-in profile reads quota and plan.'
  Assert-Claude ($first['fiveHour']['remainingPercent'] -eq 65 -and $null -ne $first['localUsage']) 'Profile combines quota and local usage.'
  Assert-Claude ($script:SeenToken -eq 'sk-ant-oat-fixture') 'The OAuth access token is sent as the bearer token.'
  $quotaCache = [System.IO.File]::ReadAllText((Join-Path $data 'claude-usage-cache.json'))
  Assert-Claude (-not $quotaCache.Contains('sk-ant') -and -not $quotaCache.Contains('rt-fixture')) 'The quota cache never contains credentials.'
  $second = $reader::ReadProfile($profile,$data,$now.AddSeconds(60))
  Assert-Claude ($script:HttpCalls -eq 1 -and $second['quotaCached'] -and $second['fetchedAt'] -eq $first['fetchedAt']) 'Polls within two minutes reuse the cache and keep its timestamp.'
  $script:HttpStatus = 429
  $limited = $reader::ReadProfile($profile,$data,$now.AddMinutes(5))
  Assert-Claude ($script:HttpCalls -eq 2 -and $limited['ok'] -and $limited['quotaCached']) 'Rate limiting falls back to a recent cache.'
  $expired = $reader::ReadProfile($profile,$data,$now.AddMinutes(30))
  Assert-Claude ((-not $expired['ok']) -and $expired['error'] -match '限流' -and $null -ne $expired['localUsage']) 'Old cache is not reused; failure keeps local usage.'
  $script:HttpStatus = 401
  $denied = $reader::ReadProfile($profile,$data,$now.AddMinutes(3))
  Assert-Claude ((-not $denied['ok']) -and $denied['error'] -match '登录已过期') 'Expired login is reported, never refreshed here.'
  Assert-Claude ([System.IO.File]::ReadAllText((Join-Path $home1 '.credentials.json')) -eq $credentials) 'Claude Code credentials are never modified.'
  $script:HttpStatus = 200
  Remove-Item -Recurse -Force (Join-Path $home1 'projects')
  Remove-Item -Force (Join-Path $home1 '.credentials.json')
  $missing = $false
  try { [void]$reader::ReadProfile($profile,$data,$now) } catch { $missing = $true }
  Assert-Claude $missing 'No login and no local logs is reported as not set up.'
  $wrapped = Get-ClaudeUsageForProfile -Profile ([pscustomobject]@{ id='claude'; label='Claude'; provider='claude'; claudeHome=$home1 }) -DataRoot $data
  Assert-Claude ((-not $wrapped.ok) -and $wrapped.error -match 'Claude Code') 'Script-mode wrapper converts setup errors into an account error.'
} finally {
  $reader::HttpGet = $originalHttp
  Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
Write-Output "Claude reader: $script:Checks checks passed."
