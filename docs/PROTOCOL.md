# Panel protocol - Turing Smart Screen 3.5" rev A

The commands, sequences and byte vectors this project uses. The same protocol is implemented in
the open-source project
[mathoudebine/turing-smart-screen-python](https://github.com/mathoudebine/turing-smart-screen-python)
(`library/lcd/lcd_comm_rev_a.py`, auto-detect by serial `USB35INCHIPSV2`).

## Device

- USB CDC serial, PNP id `USB\VID_1A86&PID_5722\USB35INCHIPSV2`. Find it by the PNP id, never
  by a fixed COM number (the number changes between machines and USB ports).
- 115200 baud, 8N1, DTR and RTS enabled.
- Panel native geometry 320x480 (portrait). Not a Windows monitor: nothing shows unless a
  program draws it.

## Command header (6 bytes)

`left`, `top`, `right`, `bottom` are 10-bit fields packed MSB first, then the command byte:

```
b0 = left >> 2
b1 = ((left & 3) << 6) + (top >> 4)
b2 = ((top & 15) << 4) + (right >> 6)
b3 = ((right & 63) << 2) + (bottom >> 8)
b4 = bottom & 255
b5 = command
```

16-byte packets reuse the same header in bytes 0-5.

## Commands

| Code | Name | Packet | Notes |
|------|------|--------|-------|
| 101 | restart | 6 | vendor waits 1 s after it |
| 108 | screen off | 6 | the vendor app sends it when it closes; skip it to keep the image |
| 109 | screen on | 6 | |
| 110 | brightness | 6 | level in `left`: 0 = brightest, 255 = darkest; vendor sends `255 - pct*255/100` |
| 121 | orientation | 16 | [6] = 100 + o (0 portrait, 1 reverse portrait, 2 landscape, 3 reverse landscape); [7..8] width, [9..10] height, big endian, as seen in that orientation (480x320 in landscape) |
| 122 | mirror | 16 | [6] = 0 off, 1 on. The vendor UI sends 1; 0 is what was verified upright here |
| 197 | bitmap | 6 + pixels | window (x, y, x+w-1, y+h-1), then w*h*2 bytes RGB565 little endian, rows top to bottom |
| 195 | pixel list | 64 | color565 at [6..7], up to 56 coordinate bytes from [8] |
| 40, 41 | LCD_28, LCD_29 | 6 | vendor sends 40 before the first bitmap; purpose unknown, not needed here |
| 136, 137, 144 | rectangle, curve pillar, curve line | 12 | "Kd" packets: 16-bit big endian left/top/right/bottom/color, an ECC byte, then the code |

## Sequences verified on the panel (2026-10-19)

- Portrait: 109, 122(0), 121(o=0, 320x480), 197(0,0,319,479) + 307200 pixel bytes. Correct.
- Landscape: 109, 122(0), 121(o=2, 480x320), 197(0,0,479,319) + 307200 pixel bytes. Correct
  and upright.
- A full frame takes about 1.3 s at the nominal 115200 baud (USB CDC ignores the baud rate).

## Vectors (selftest)

| Input | Bytes |
|-------|-------|
| header 197, (0,0,479,319) | `00 00 07 7D 3F C5` |
| header 197, (0,0,319,479) | `00 00 04 FD DF C5` |
| brightness 0 % (level 255) | `3F C0 00 00 00 6E` |
| brightness 100 % (level 0) | `00 00 00 00 00 6E` |
| orientation 2 | `00 00 00 00 00 79 66 01 E0 01 40 00 00 00 00 00` |
| orientation 0 | `00 00 00 00 00 79 64 01 40 01 E0 00 00 00 00 00` |
| mirror 0 | `00 00 00 00 00 7A 00 00 00 00 00 00 00 00 00 00` |
| color565 red, green, blue, white | `F800 07E0 001F FFFF` |
| GDI+ Format16bppRgb565 of red, blue | `00 F8 1F 00` |
