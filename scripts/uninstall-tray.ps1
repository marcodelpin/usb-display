# uninstall-tray.ps1 - stop the tray, remove its autostart value, and move the install folder aside
# (moved, not deleted: rename it back to restore). Config and state in %APPDATA% / %LOCALAPPDATA% stay.
$ErrorActionPreference = 'Stop'
$dir = Join-Path (Join-Path $env:LOCALAPPDATA 'Programs') 'usb-display'
$exe = Join-Path $dir 'UsbDisplayTray.exe'
# Only this user's tray: another user's tray runs from their own %LOCALAPPDATA%.
Get-Process -Name UsbDisplayTray -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } |
  ForEach-Object { Stop-Process -Id $_.Id -Force }
Remove-ItemProperty -Path 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run' -Name 'UsbDisplayTray' -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $dir) {
  $aside = '{0}.removed-{1}' -f $dir, (Get-Date -Format 'yyyyMMddHHmmss')
  Move-Item -LiteralPath $dir -Destination $aside
  "moved $dir -> $aside"
}
"autostart=absent"
