# uninstall-tray.ps1 - stop the tray, remove its autostart value, and move the install folder aside
# (moved, not deleted: rename it back to restore). Config and state in %APPDATA% / %LOCALAPPDATA% stay.
# The steps live in install-tray.ps1 -Uninstall, next to the tray stop that both scripts need.
& (Join-Path $PSScriptRoot 'install-tray.ps1') -Uninstall
