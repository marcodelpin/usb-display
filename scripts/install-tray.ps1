# install-tray.ps1 - install or update the usb-display tray and CLI for the current user, then start the tray.
# Run it on the Windows machine the panel is plugged into, with -Zip <the published folder, zipped>.
# Per-user: no admin rights needed.
param([Parameter(Mandatory = $true)][string]$Zip)
$ErrorActionPreference = 'Stop'
$dir = Join-Path (Join-Path $env:LOCALAPPDATA 'Programs') 'usb-display'
$exe = Join-Path $dir 'UsbDisplayTray.exe'
$run = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'

# Only the tray of this install, matched by executable path: another user's tray runs from their own
# %LOCALAPPDATA% and is not ours to stop. CIM reads the path of the 64-bit tray from 32-bit
# PowerShell too, where Get-Process returns an empty Path.
function Get-OwnTray([string]$Exe) {
  @(Get-CimInstance Win32_Process -Filter "Name = 'UsbDisplayTray.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -eq $Exe })
}
$old = Get-OwnTray $exe
foreach ($p in $old) { Stop-Process -Id $p.ProcessId -Force }
if ($old.Count) { Start-Sleep -Milliseconds 800 }
"stopped {0} running instance(s)" -f $old.Count

# A config that still equals the default shipped by the previous build was never edited: move it aside
# so the new build writes its own default. An edited config is kept as it is.
$cfgFile = Join-Path (Join-Path $env:APPDATA 'usb-display') 'config.json'
$snapshot = Join-Path (Join-Path $env:LOCALAPPDATA 'usb-display') 'default-config.json'
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
