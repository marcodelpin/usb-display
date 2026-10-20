# usb-display

A Windows tray program and a command line tool that drive the Turing Smart Screen 3.5" (rev A)
USB panel and draw live server metrics on it: load bars (CPU, memory, I/O, GPU, anything you can
query) and disk allocation rows (absolute free space, lowest free space of the last 7 days, growth
per day and days left). The values come from a Prometheus-compatible query API such as
VictoriaMetrics or Prometheus.

> **Status: experimental, AI-assisted.** The code was written with an AI coding assistant
> (Claude) and has not been reviewed line by line by a human; a second AI model reviewed it
> adversarially. It has 67 unit tests (protocol vectors, stale-value rules, storage math, config
> validation, a golden master of the Core outputs) and runs with one rev A panel on Windows.
> Use it at your own risk. Not affiliated with the panel's vendor.

## Hardware

- Turing Smart Screen 3.5" rev A: USB `1A86:5722`, serial number `USB35INCHIPSV2`, 320x480.
- The panel is a USB CDC serial port, not a monitor: nothing shows unless a program draws it.
  The tools find it by its PNP id, never by a fixed COM number.
- Wire protocol, sequences and byte vectors: [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Layout

| Path | What |
|------|------|
| `src/UsbDisplay.Core` | protocol encoding, config, PromQL client, collector, storage math (net10.0, runs on Linux too) |
| `src/UsbDisplay.Win` | serial port and PNP detection, GDI+ rendering, status files (net10.0-windows) |
| `src/UsbDisplayTray` | the tray program (WinForms NotifyIcon) |
| `src/usbdisp` | the command line tool |
| `src/UsbDisplay.Core/DefaultConfig.json` | the default config embedded in the build |
| `tests/UsbDisplay.Tests` | xunit tests of Core |
| `scripts/install-tray.ps1`, `scripts/uninstall-tray.ps1` | per-user install and removal on Windows |

## Build and test

Requires the .NET 10 SDK.

```
dotnet build UsbDisplay.slnx -c Release
dotnet test tests/UsbDisplay.Tests -c Release
```

`bash tests/run.sh` runs the same tests. The Windows projects also build on Linux
(`EnableWindowsTargeting`), but the tray, the renderer and the serial port run only on Windows.
`usbdisp selftest` adds the Windows-only checks (GDI+ RGB565 byte order, full frame length).

Publish for Windows (framework-dependent: the target needs the .NET 10 Desktop Runtime):

```
dotnet publish src/UsbDisplayTray/UsbDisplayTray.csproj -c Release -r win-x64 --self-contained false -o publish
dotnet publish src/usbdisp/usbdisp.csproj -c Release -r win-x64 --self-contained false -o publish
```

## Install (Windows, per user, no admin rights)

Zip the `publish` folder, then on the machine the panel is plugged into:

```
powershell -ExecutionPolicy Bypass -File scripts\install-tray.ps1 -Zip C:\path\to\publish.zip
```

The script stops this user's running tray (another user's tray on the same machine is left
alone), extracts the zip to `%LOCALAPPDATA%\Programs\usb-display\`,
sets the autostart value `UsbDisplayTray` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
and starts the tray. A config that still equals the default of the previous build was never
edited: it is moved aside so that the new build writes its own default. An edited config is kept.

`scripts\uninstall-tray.ps1` stops this user's tray, removes the autostart value and moves the install
folder aside (rename it back to restore). Config and state stay.

## Tray

The tray redraws the panel every `RefreshSeconds`. Menu: Refresh now, Pause / Resume, Brightness,
Open config, Reload config, Exit. The tooltip shows the panel port and the time of the last
frame, or the current error. When the panel is unplugged the tray keeps running and draws again
once the panel is back.

State files in `%LOCALAPPDATA%\usb-display\`: `status.json`, `last-frame.png` (the last frame
rendered, also when the panel was absent and nothing was sent) and `default-config.json` (the
default of the installed build).

## Configuration

On its first start the tray writes `%APPDATA%\usb-display\config.json` from the default embedded
in the build. Edit it and choose Reload config. The shipped default watches one Linux host through
node_exporter (`localhost:9100`) on a VictoriaMetrics at `http://localhost:8428`: put your own
URL, instances and queries in it. To ship a different default, edit
`src/UsbDisplay.Core/DefaultConfig.json` before building.

| Key | Meaning |
|-----|---------|
| `VmUrl` | base URL of the query API; `/api/v1/query` is appended |
| `RefreshSeconds` | frame interval, at least 2 |
| `TrendRefreshSeconds` | interval of the 7-day trend queries |
| `StaleSeconds` | a value whose source is older than this is drawn as `--` |
| `Orientation` | 2 landscape (480x320) or 3 reverse landscape: the metrics frame is laid out for 480x320. 0 portrait and 1 reverse portrait (320x480) suit only `usbdisp test` and `image` |
| `Brightness` | 0-100 |
| `Title` | header text |
| `Sources` | named sources, each with an `AgeQuery` that returns the age in seconds of its newest sample |
| `Load` | bar rows: `Label`, `Source`, `Query` (a percent), optional `ExtraLabel` / `ExtraQuery` (a number next to the bar), optional `Note`, `WarnAt` / `AlarmAt` (inclusive percent thresholds) |
| `Trends` | templates of the lowest free space (7 days) and of the growth per day (1 and 7 days); `{size}` and `{used}` are replaced by the queries of each storage row |
| `Storage` | disk rows: `Label`, `Source`, `SizeQuery`, `UsedQuery` (bytes), optional `WarnFreeBytes` (an absolute floor that forces red) |
| `StorageDaysWarn`, `StorageDaysAlarm` | storage rows turn amber / red when the days left fall under these |

Every query must return exactly one sample. A load or free-space value whose query fails, returns
no series, or whose source is older than `StaleSeconds` is drawn as `--` in amber (the number next
to a load bar takes the color of the bar). The trend columns (lowest free, growth per day, days
left) come from the last trend refresh: they are queried every `TrendRefreshSeconds` and keep
their last values while the query API cannot be reached. When no query reaches the API the
header says so. Storage rows are judged on absolute free space and days left, never on percent
used.

### Known issues

- A blank query in the config counts as an answer, so while one is there the header does not
  report an unreachable query API. Do not leave a query empty.

## Command line

```
usbdisp detect | on | off | brightness <0-100> | orientation [n] | test [n] | image <path> [n] | frame | selftest
```

| Verb | Does |
|------|------|
| `detect` | prints the COM port of the panel (exit code 2 when it is absent) |
| `on`, `off`, `brightness <0-100>` | screen on, screen off, brightness |
| `orientation [n]` | sets the orientation (default 2, landscape) |
| `test [n]`, `image <path> [n]` | draws a test pattern or an image, full screen |
| `frame` | draws one metrics frame from the config |
| `selftest` | checks the protocol vectors and the Windows-only encoding |

Options: `--port COMn` (skip detection), `--dry-run [--out <file>]` (write the bytes to a file
instead of the port), `--png <path>` (`frame`: also save the rendered frame). The tray redraws
every `RefreshSeconds`: pause it from its menu first to keep what the command line draws (a
frame the tray was already collecting when you paused can still be drawn once).

## License

MIT, see [LICENSE](LICENSE).
