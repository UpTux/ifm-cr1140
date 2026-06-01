# Changelog

All notable changes to **`cr1140-hal`** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This crate follows [Semantic Versioning](https://semver.org/); while `0.x`, minor
releases may contain breaking changes.

## [0.1.0] - 2026-06-01

Initial public release — hardware abstraction for the ifm CR1140 / CR1141
ecomatDisplay (NXP i.MX 8M Nano, aarch64, Yocto Linux), wrapping the stock Linux
ABIs the device exposes.

### Added
- **`display`** — framebuffer (fbdev) backend; `Surface` (xRGB8888, stride-aware
  blit) for `/dev/fb0` (800×480); double-buffering; backlight control.
- **`input`** — evdev keypad reader; `Button` mapping (F1–F6, arrows, Enter) and
  `ButtonEvent`.
- **`can`** — SocketCAN wrapper (MCP2518FD CAN-FD controller on the board).
- **`sys`** — status/keypad RGB LEDs, backlight, SoC + board (lm75) temperature,
  and the SPI EEPROM `Nvmem` (reflash-surviving NVRAM).
- Typed error handling via `HalResult` / `HalError`.

[0.1.0]: https://github.com/UpTux/ifm-cr1140/releases/tag/v0.1.0
