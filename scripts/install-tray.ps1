# install-tray.ps1 - install or update the usb-display tray and CLI for the current user, then start the tray.
# Run it on the Windows machine the panel is plugged into, with -Zip <the published folder, zipped>.
# Per-user: no admin rights needed.
param([Parameter(Mandatory = $true)][string]$Zip)
$ErrorActionPreference = 'Stop'
$dir = Join-Path (Join-Path $env:LOCALAPPDATA 'Programs') 'usb-display'
$exe = Join-Path $dir 'UsbDisplayTray.exe'
$run = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'

# Only the tray of this install: another user's tray runs from their own %LOCALAPPDATA% (its Path is
# not readable without admin rights) and is not ours to stop.
$old = @(Get-Process -Name UsbDisplayTray -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
foreach ($p in $old) { Stop-Process -Id $p.Id -Force }
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

$p = Get-Process -Name UsbDisplayTray -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1
"installed {0} version={1}" -f $dir, (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
"tray={0}" -f $(if ($p) { 'running pid=' + $p.Id + ' session=' + $p.SessionId } else { 'absent' })
Remove-Item -LiteralPath $Zip -ErrorAction SilentlyContinue
