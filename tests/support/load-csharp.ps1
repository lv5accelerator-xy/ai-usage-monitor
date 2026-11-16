# Compiles the shared C# logic for offline tests. Windows PowerShell 5.1 uses the real
# System.Web.Extensions; PowerShell 7 uses a System.Text.Json shim.
function Import-AppModel {
  param([string]$Root)
  if ('CodexUsageDesktop.Model' -as [type]) { return }
  $sources = @(
    (Join-Path $Root 'desktop\AppModel.cs'),
    (Join-Path $Root 'desktop\RemoteWorker.cs'),
    (Join-Path $Root 'claude-reader.cs'),
    (Join-Path $Root 'tests\support\FakeRelay.cs')
  )
  $webExtensions = $null
  try { $webExtensions = [System.Reflection.Assembly]::Load('System.Web.Extensions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35') } catch {}
  if ($null -ne $webExtensions) {
    Add-Type -Path $sources -ReferencedAssemblies System.Web.Extensions,System.Core
  } else {
    Add-Type -Path ($sources + (Join-Path $Root 'tests\support\JavaScriptSerializerShim.cs')) -IgnoreWarnings -WarningAction SilentlyContinue
  }
}

# Hashtables/arrays -> Dictionary<string,object>/object[] like JavaScriptSerializer output.
# The conversion runs in C#: PowerShell unrolls single-element arrays and wraps values in PSObject.
function ConvertTo-JsonTree {
  param($Value)
  return ,([CodexUsageDesktop.TestTree]::From($Value))
}
