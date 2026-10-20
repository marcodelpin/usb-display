#!/usr/bin/env bash
# Run the unit tests (dotnet, any OS). The Windows-only checks (GDI+ RGB565 byte order, full
# landscape frame length) run on Windows with `usbdisp selftest`.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
echo "== unit tests"
dotnet test "$root/tests/UsbDisplay.Tests" -c Release --nologo -v q
