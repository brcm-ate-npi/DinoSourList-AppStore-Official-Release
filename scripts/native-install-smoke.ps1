param([Parameter(Mandatory=$true)][string]$OperatingSystem)
$ErrorActionPreference = 'Stop'
$data = Join-Path $env:RUNNER_TEMP 'dinosaurlist-native-data'
New-Item -ItemType Directory -Force $data | Out-Null
$env:DINOSAURLIST_DATA_DIR = $data
$tool = @{
  id='1'; folderName='Preview-Author'; name='Preview'; author='Author'; category='MISC'
  packages=@(@{fileId='2';fileName='v1.0.0-any.zip';version='1.0.0';size=1;sha1=('a'*40);platform='any'})
  flow=@{sections=@{platform=@{supported='any'}}}; files=@()
}
@{fetchedAt=[DateTimeOffset]::UtcNow.ToString('o'); tools=@($tool); warnings=@()} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $data 'catalogue.json') -Encoding utf8
if ($OperatingSystem -eq 'Windows') {
  $setup = Get-ChildItem Releases -Filter '*-Setup.exe' | Select-Object -First 1
  if (-not $setup) { throw 'Setup.exe missing' }
  $install = Start-Process $setup.FullName -ArgumentList '--silent' -PassThru -Wait
  if ($install.ExitCode -ne 0) { throw "Installer failed: $($install.ExitCode)" }
  $app = Join-Path $env:LOCALAPPDATA 'DinoSaurList/current/DinoSaurList.exe'
} elseif ($OperatingSystem -eq 'macOS') {
  $setup = Get-ChildItem Releases -Filter '*-Setup.pkg' | Select-Object -First 1
  if (-not $setup) { throw 'Setup.pkg missing' }
  & sudo installer -pkg $setup.FullName -target /
  if ($LASTEXITCODE -ne 0) { throw 'macOS installer failed' }
  $app = '/Applications/DinoSaurList.app/Contents/MacOS/DinoSaurList'
} else { throw 'Unsupported smoke-test OS' }
if (-not (Test-Path $app)) { throw "Installed application missing: $app" }
$evidence = Join-Path $env:RUNNER_TEMP 'native-installed-evidence'
New-Item -ItemType Directory -Force $evidence | Out-Null
$prefix = Join-Path $evidence 'installed-ui'
$process = Start-Process $app -ArgumentList @('--smoke-test-output', "`"$prefix`"") -PassThru
if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw 'Installed app smoke timed out' }
if ($process.ExitCode -ne 0) { throw "Installed app failed: $($process.ExitCode)" }
if (-not (Test-Path "$prefix.json") -or -not (Test-Path "$prefix.png")) { throw 'Native screenshot/result missing' }
$result = Get-Content "$prefix.json" -Raw | ConvertFrom-Json
if (-not $result.visible -or $result.tools -ne 1) { throw 'Installed native UI failed validation' }
$cliName = if ($OperatingSystem -eq 'Windows') { 'DinoSaurList.Cli.exe' } else { 'DinoSaurList.Cli' }
# Velopack includes all self-contained CLI files in a subdirectory of the installed app.
$cli = Join-Path (Split-Path $app) "cli/$cliName"
if (-not (Test-Path $cli)) { throw "Installed CLI missing: $cli" }
& $cli catalogue --data-dir $data | Set-Content (Join-Path $evidence 'installed-cli.json') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Installed self-contained CLI failed' }
