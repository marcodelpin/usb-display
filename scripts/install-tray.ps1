# install-tray.ps1 - install or update the usb-display tray and CLI for the current user, then start the tray.
# Run it on the Windows machine the panel is plugged into, with -Zip <the published folder, zipped>.
# -Uninstall instead stops the tray, removes its autostart value and moves the install folder aside
# (moved, not deleted: rename it back to restore); config and state in %APPDATA% / %LOCALAPPDATA% stay.
# Per-user: no admin rights needed.
param([string]$Zip, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
if (-not $Uninstall -and -not $Zip) { throw 'usage: install-tray.ps1 -Zip <published zip> | -Uninstall' }
$dir = Join-Path (Join-Path $env:LOCALAPPDATA 'Programs') 'usb-display'
$exe = Join-Path $dir 'UsbDisplayTray.exe'
$state = Join-Path $env:LOCALAPPDATA 'usb-display'
$run = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'

# Only the tray of this install, matched by executable path: another user's tray runs from their own
# %LOCALAPPDATA% and is not ours to stop. CIM reads the path of the 64-bit tray from 32-bit
# PowerShell too, where Get-Process returns an empty Path.
function Get-OwnTray([string]$Exe) {
  @(Get-CimInstance Win32_Process -Filter "Name = 'UsbDisplayTray.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -eq $Exe })
}

# time_utc of the last refresh the tray finished: status.json is replaced atomically after each one.
function Get-FrameMark([string]$State) {
  try { [string](Get-Content -LiteralPath (Join-Path $State 'status.json') -Raw | ConvertFrom-Json).time_utc } catch { '' }
}

# Ask our tray to finish the frame it is writing and exit (it checks for stop.request every 0.5 s): a kill
# in the middle of a 307 KB frame write can leave the panel's USB serial endpoint wedged until the device
# is restarted. A tray still running after 30 s (an older build, or a hung one) is stopped right after it
# finishes its next refresh, or after 20 s more.
function Stop-OwnTray([string]$Exe, [string]$State) {
  $procs = Get-OwnTray $Exe
  if (-not $procs.Count) { return 'stopped 0 running instance(s)' }
  $request = Join-Path $State 'stop.request'
  New-Item -ItemType Directory -Force -Path $State | Out-Null
  Set-Content -LiteralPath $request -Value (Get-Date -Format o)
  $deadline = (Get-Date).AddSeconds(30)
  while ((Get-OwnTray $Exe).Count -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
  $left = Get-OwnTray $Exe
  if ($left.Count) {
    $mark = Get-FrameMark $State
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-FrameMark $State) -eq $mark -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    foreach ($p in $left) { Stop-Process -Id $p.ProcessId -Force }
    Start-Sleep -Milliseconds 800
  }
  Remove-Item -LiteralPath $request -ErrorAction SilentlyContinue
  'stopped {0} running instance(s): {1} on request, {2} forced after a refresh' -f $procs.Count, ($procs.Count - $left.Count), $left.Count
}

Stop-OwnTray $exe $state

if ($Uninstall) {
  Remove-ItemProperty -Path $run -Name 'UsbDisplayTray' -ErrorAction SilentlyContinue
  if (Test-Path -LiteralPath $dir) {
    $aside = '{0}.removed-{1}' -f $dir, (Get-Date -Format 'yyyyMMddHHmmss')
    Move-Item -LiteralPath $dir -Destination $aside
    "moved $dir -> $aside"
  }
  'autostart=absent'
  return
}

# A config that still equals the default shipped by the previous build was never edited: move it aside
# so the new build writes its own default. An edited config is kept as it is.
$cfgFile = Join-Path (Join-Path $env:APPDATA 'usb-display') 'config.json'
$snapshot = Join-Path $state 'default-config.json'
if ((Test-Path -LiteralPath $cfgFile) -and (Test-Path -LiteralPath $snapshot)) {
  if ((Get-Content -LiteralPath $cfgFile -Raw) -ceq (Get-Content -LiteralPath $snapshot -Raw)) {
    $aside = '{0}.replaced-{1}' -f $cfgFile, (Get-Date -Format 'yyyyMMddHHmmss')
    Move-Item -LiteralPath $cfgFile -Destination $aside
    "config: unmodified previous default, moved to $aside; the new default is written at start"
  } else {
    "config: edited by the user, kept; the new defaults are in $snapshot after start"
  }
} elseif (Test-Path -LiteralPath $cfgFile) {
  "config: no default snapshot to compare with, kept"
}

New-Item -ItemType Directory -Force -Path $dir | Out-Null
Expand-Archive -LiteralPath $Zip -DestinationPath $dir -Force
Set-ItemProperty -Path $run -Name 'UsbDisplayTray' -Value ('"{0}"' -f $exe)
Start-Process -FilePath $exe -WorkingDirectory $dir
Start-Sleep -Seconds 4

$p = Get-OwnTray $exe | Select-Object -First 1
"installed {0} version={1}" -f $dir, (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
"tray={0}" -f $(if ($p) { 'running pid=' + $p.ProcessId + ' session=' + $p.SessionId } else { 'absent' })
Remove-Item -LiteralPath $Zip -ErrorAction SilentlyContinue
