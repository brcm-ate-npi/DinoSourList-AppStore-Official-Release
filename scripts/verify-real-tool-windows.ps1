param([string]$WorkRoot=$env:RUNNER_TEMP,[string]$RunId=$env:GITHUB_RUN_ID,[string]$CliPath,[switch]$Interactive)
# Real portable package validation; no MSI or machine-wide installation.
$ErrorActionPreference = 'Stop'
if (-not $CliPath) { $CliPath=Join-Path $env:LOCALAPPDATA 'DinoSaurListNative/current/DinoSaurList.Cli.exe' }
$cli=$CliPath
if (-not $Interactive -and -not [Environment]::UserInteractive) {
  # Service runners use Session 0; run only this proof in the existing signed-in user's desktop.
  $user=(Get-CimInstance Win32_ComputerSystem).UserName
  if (-not $user) { throw 'No signed-in Windows user: a real GUI tool check requires an interactive desktop.' }
  $taskName="DinoSaurList-ToolProof-$RunId"
  $result=Join-Path $WorkRoot "dinosaurlist-interactive-$RunId.json"
  $failure=Join-Path $WorkRoot "dinosaurlist-interactive-$RunId.error.txt"
  $wrapper=Join-Path $WorkRoot "dinosaurlist-interactive-$RunId.ps1"
  function Literal([string]$value) { return "'" + $value.Replace("'","''") + "'" }
  $invoke='& '+(Literal $PSCommandPath)+' -Interactive -WorkRoot '+(Literal $WorkRoot)+' -RunId '+(Literal $RunId)+' -CliPath '+(Literal $CliPath)
  $body="`$ErrorActionPreference='Stop'`ntry { $invoke; @{success=`$true} | ConvertTo-Json | Set-Content -Encoding utf8 "+(Literal $result)+" } catch { `$_ | Out-String | Set-Content -Encoding utf8 "+(Literal $failure)+"; @{success=`$false} | ConvertTo-Json | Set-Content -Encoding utf8 "+(Literal $result)+"; exit 1 }"
  Set-Content -Path $wrapper -Value $body -Encoding utf8
  $action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$wrapper+'"')
  $principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
  $options=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 3)
  $registered=$false
  try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $options | Out-Null
    $registered=$true; Start-ScheduledTask -TaskName $taskName
    $deadline=[DateTime]::UtcNow.AddSeconds(150)
    while(-not (Test-Path $result) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if(-not (Test-Path $result)) { throw 'Interactive tool proof timed out; check the signed-in Windows desktop.' }
    if(-not (Get-Content $result -Raw | ConvertFrom-Json).success) {
      if(Test-Path $failure) { Get-Content $failure | Write-Host }
      throw 'Interactive tool validation failed.'
    }
    return
  } finally {
    if($registered) { Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName $taskName -Confirm:$false }
  }
}
if(-not [Environment]::UserInteractive) { throw 'The tool validation did not enter the interactive desktop.' }
$data = Join-Path $WorkRoot "dinosaurlist-real-tool-$RunId"
$evidence = Join-Path $WorkRoot 'native-installed-evidence'
New-Item -ItemType Directory -Force $data,$evidence | Out-Null
& $cli catalogue --refresh --mode 3 --data-dir $data | Set-Content (Join-Path $data 'catalogue-result.json') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Real Box catalogue failed' }
$catalogue = Get-Content (Join-Path $data 'catalogue-result.json') -Raw | ConvertFrom-Json
$tool = $catalogue.tools | Where-Object { $_.name -eq 'PasswordGeneration' } | Select-Object -First 1
if (-not $tool -or $tool.flow.sections.installation.auto -eq 'True') { throw 'Expected portable validation tool unavailable' }
$existing = @(Get-Process PasswordGeneration -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
try {
  & $cli install --tool $tool.id --data-dir $data | Set-Content (Join-Path $evidence 'real-tool-install.json') -Encoding utf8
  if ($LASTEXITCODE -ne 0) { throw 'Real tool download/installation failed' }
  $installed = Get-Content (Join-Path $evidence 'real-tool-install.json') -Raw | ConvertFrom-Json
  if (-not (Test-Path (Join-Path $installed.directory $installed.executionFile))) { throw 'Execution target missing after real ZIP extraction' }
  & $cli launch --tool $tool.id --data-dir $data
  if ($LASTEXITCODE -ne 0) { throw 'Real tool launch failed' }
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  $opened = $null
  do {
    $opened = Get-Process PasswordGeneration -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $existing -and $_.Path.StartsWith($data,[StringComparison]::OrdinalIgnoreCase) -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($opened) { break }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  if (-not $opened) { throw 'Downloaded tool did not show a real native window' }
  @{ name=$tool.name; version=$installed.version; executable=$opened.Path; pid=$opened.Id; window=$opened.MainWindowTitle; handle=$opened.MainWindowHandle.ToInt64(); sha1=$tool.packages[0].sha1 } | ConvertTo-Json | Set-Content (Join-Path $evidence 'real-tool-window.json') -Encoding utf8
  Add-Type -AssemblyName System.Drawing
  Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DinoToolWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
'@
  $rect = New-Object DinoToolWindow+Rect
  if (-not [DinoToolWindow]::GetWindowRect($opened.MainWindowHandle,[ref]$rect)) { throw 'Cannot measure the real tool window' }
  $bitmap = New-Object System.Drawing.Bitmap ($rect.Right-$rect.Left),($rect.Bottom-$rect.Top)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $dc=$graphics.GetHdc()
  try {
    if (-not [DinoToolWindow]::PrintWindow($opened.MainWindowHandle,$dc,2)) { throw 'Cannot capture the real tool window' }
  } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
  try { $bitmap.Save((Join-Path $evidence 'real-tool-window.png')) } finally { $bitmap.Dispose() }

} finally {
  Get-Process PasswordGeneration -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $existing -and $_.Path -and $_.Path.StartsWith($data,[StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force
}
