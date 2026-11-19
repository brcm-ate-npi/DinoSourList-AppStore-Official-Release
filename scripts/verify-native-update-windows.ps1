# Exercise a published portable installation once: the app must update itself without a test-driven restart.
$ErrorActionPreference = 'Stop'
$old = $env:NATIVE_UPDATE_FROM
if ($old -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid published baseline version' }
$proof = Join-Path $env:RUNNER_TEMP "dinosaurlist-startup-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
$evidence = Join-Path $env:RUNNER_TEMP 'native-update-evidence'
$install = Join-Path $proof 'install'
$data = Join-Path $proof 'data'
New-Item -ItemType Directory -Force $proof,$evidence,$install,$data,(Join-Path $data 'Documents') | Out-Null
$feed = 'https://npi-service.kor.broadcom.net/dinosaurlist/'
$release = Invoke-RestMethod ($feed + 'releases.win-preview.json') -TimeoutSec 30
$target = @($release.Assets | Where-Object Type -eq 'Full')[0]
if (-not $target -or $target.Version -eq $old -or $target.FileName -notmatch '^DinoSaurListNative-[A-Za-z0-9.-]+-win-preview-full\.nupkg$') { throw 'No valid newer target exists' }
$portable = Join-Path $proof 'old.zip'
Invoke-WebRequest "https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/releases/download/v$old/DinoSaurListNative-win-preview-Portable.zip" -OutFile $portable -TimeoutSec 180 -MaximumRetryCount 3
Expand-Archive -LiteralPath $portable -DestinationPath $install
$app = Join-Path $install 'current/DinoSaurList.exe'
$manifest = Join-Path $install 'current/sq.version'
function Has-Version([string]$value) { return (Test-Path $manifest) -and ((Get-Content $manifest -Raw) -match ('<version>' + [regex]::Escape($value) + '</version>')) }
if (-not (Test-Path $app) -or -not (Has-Version $old)) { throw 'Published baseline installation is invalid' }
Copy-Item $manifest (Join-Path $evidence 'before.version.xml')
$tool = @{id='1';folderName='Preview-Author';name='Preview';author='Author';category='MISC';packages=@(@{fileId='2';fileName='v1.0.0-any.zip';version='1.0.0';size=1;sha1=('a'*40);platform='any'});flow=@{sections=@{platform=@{supported='any'}}};files=@(@{id='3';name='README.md';type='file';sha1=('b'*40)})}
@{fetchedAt=[DateTimeOffset]::UtcNow.ToString('o');tools=@($tool);warnings=@()} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $data 'catalogue.json') -Encoding utf8
"TOOL: Preview`nAUTHOR: Author`nPURPOSE: Validate the native engineering toolbox.`nPreview fixture." | Set-Content (Join-Path $data ('Documents/3-' + ('b'*40) + '.md')) -Encoding utf8
'preserve-update-proof' | Set-Content (Join-Path $data 'sentinel.txt') -Encoding utf8
$env:DINOSAURLIST_DATA_DIR = $data
function Get-OwnProcesses {
  Get-CimInstance Win32_Process -Filter "name='DinoSaurList.exe' OR name='Update.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($install + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }
}
function Stop-OwnProcesses { Get-OwnProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } }
try {
  # This is the only start before the manifest must advance to the target.
  Start-Process $app -RedirectStandardOutput (Join-Path $evidence 'startup.stdout.log') -RedirectStandardError (Join-Path $evidence 'startup.stderr.log') | Out-Null
  $deadline = [DateTime]::UtcNow.AddSeconds(240)
  while (-not (Has-Version $target.Version) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
  if (-not (Has-Version $target.Version)) { throw 'App did not automatically apply the update on its first start' }
  Copy-Item $manifest (Join-Path $evidence 'after.version.xml')
  $finishDeadline = [DateTime]::UtcNow.AddSeconds(60)
  while ((@(Get-OwnProcesses | Where-Object Name -eq 'Update.exe').Count -gt 0) -and [DateTime]::UtcNow -lt $finishDeadline) { Start-Sleep -Milliseconds 500 }
  if (@(Get-OwnProcesses | Where-Object Name -eq 'Update.exe').Count -gt 0) { throw 'Updater is still applying the target' }
  $package = Join-Path $proof 'expected.nupkg'
  Invoke-WebRequest ($feed + $target.FileName) -OutFile $package -TimeoutSec 180 -MaximumRetryCount 3
  if ((Get-FileHash $package -Algorithm SHA256).Hash -ne $target.SHA256) { throw 'Published target SHA256 mismatch' }
  $zip = [IO.Compression.ZipFile]::OpenRead($package)
  try {
    $entry = $zip.GetEntry('lib/app/DinoSaurList.dll')
    if (-not $entry) { throw 'Target DLL missing from release package' }
    $stream = $entry.Open()
    try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
  } finally { $zip.Dispose() }
  if ((Get-FileHash (Join-Path $install 'current/DinoSaurList.dll') -Algorithm SHA256).Hash -ne $expected) { throw 'Automatically applied DLL differs from published target' }
  Stop-OwnProcesses
  $prefix = Join-Path $evidence 'updated-ui'
  $capture = Start-Process $app -ArgumentList @('--smoke-test-output', ('"' + $prefix + '"')) -PassThru
  if (-not $capture.WaitForExit(90000) -or $capture.ExitCode -ne 0) { throw 'Updated native UI verification failed' }
  $ui = Get-Content "$prefix.json" -Raw | ConvertFrom-Json
  if ($ui.version -ne $target.Version -or -not (Test-Path "$prefix.png")) { throw 'Updated UI version or screenshot missing' }
  if ((Get-Content (Join-Path $data 'sentinel.txt') -Raw).Trim() -ne 'preserve-update-proof') { throw 'Existing app data changed during update' }
  @{from=$old;to=$target.Version;automaticDownload=$true;appliedOnFirstStart=$true;userRestartRequired=$false;dataPreserved=$true;sha256=$target.SHA256} | ConvertTo-Json | Set-Content (Join-Path $evidence 'update-result.json') -Encoding utf8
  Write-Host "::notice title=Published Windows immediate startup update verified::$old to $($target.Version); single launch, automatic apply, published DLL checksum, updated UI and data preservation passed"
} finally { Stop-OwnProcesses }
