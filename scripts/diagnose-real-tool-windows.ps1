$ErrorActionPreference='Stop'
$cli=Join-Path $env:LOCALAPPDATA 'DinoSaurListNative/current/DinoSaurList.Cli.exe'
$data=Join-Path $env:RUNNER_TEMP "dinosaurlist-diagnostics-$env:GITHUB_RUN_ID"
$out=Join-Path $env:RUNNER_TEMP 'native-tool-diagnostics'
New-Item -ItemType Directory -Force $data,$out | Out-Null
$start=Get-Date
& $cli catalogue --refresh --mode 3 --data-dir $data | Set-Content (Join-Path $data 'catalogue.json') -Encoding utf8
if($LASTEXITCODE -ne 0){throw 'Catalogue failed'}
$catalogue=Get-Content (Join-Path $data 'catalogue.json') -Raw | ConvertFrom-Json
$tool=$catalogue.tools | Where-Object name -eq 'PasswordGeneration' | Select-Object -First 1
& $cli install --tool $tool.id --data-dir $data | Set-Content (Join-Path $out 'install.json') -Encoding utf8
if($LASTEXITCODE -ne 0){throw 'Install failed'}
$installed=Get-Content (Join-Path $out 'install.json') -Raw | ConvertFrom-Json
$exe=Join-Path $installed.directory $installed.executionFile
$p=Start-Process $exe -WorkingDirectory $installed.directory -PassThru
try{
 Start-Sleep -Seconds 5
 $p.Refresh()
 $state=@{pid=$p.Id;name=$p.ProcessName;exited=$p.HasExited;userInteractive=[Environment]::UserInteractive;runnerSession=(Get-Process -Id $PID).SessionId;path=$exe}
 if($p.HasExited){$state.exitCode=$p.ExitCode}else{$state.windowHandle=$p.MainWindowHandle.ToInt64();$state.windowTitle=$p.MainWindowTitle;$state.session=$p.SessionId}
 $state | ConvertTo-Json | Tee-Object -FilePath (Join-Path $out 'process.json')
 Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$start.AddSeconds(-10)} -ErrorAction SilentlyContinue | Where-Object {$_.Message -match 'PasswordGeneration'} | Select-Object TimeCreated,ProviderName,Id,Message | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $out 'events.json')
 Add-Type -AssemblyName System.Windows.Forms,System.Drawing
 $bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
 $bitmap=New-Object System.Drawing.Bitmap $bounds.Width,$bounds.Height
 $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
 try{$graphics.CopyFromScreen($bounds.Location,[System.Drawing.Point]::Empty,$bounds.Size);$bitmap.Save((Join-Path $out 'desktop.png'))}finally{$graphics.Dispose();$bitmap.Dispose()}
}finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force}}
