# Changelog

All notable changes to **`cr1140-sdk`** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This crate follows [Semantic Versioning](https://semver.org/); while `0.x`, minor
releases may contain breaking changes.

## [0.1.0] - 2026-06-01

Initial public release — UI-framework-agnostic application conveniences layered
on top of [`cr1140-hal`](https://crates.io/crates/cr1140-hal).

### Added
- **`led`** — RGB keypad-LED animation modes and a `LedDriver`.
- **`metrics`** — generic Linux telemetry (CPU, memory, load, uptime,
  temperature) and a `Telemetry` collector.
- **`device`** — OS / identity info and live network state.
- **`guard`** — `ShutdownGuard`: RAII restore of backlight/LED on exit, with
  opt-in SIGINT/SIGTERM handling (`signals` feature, standalone binaries only).
- **`config`** — atomic TOML `Store` on the writable app overlay.
- **`retain`** — reflash-surviving `Store` on the SPI EEPROM (A/B double-buffer
  + CRC32).
- **`net`** — `nmcli`-based network apply (`net` feature, off by default).
- Cargo features: `config`, `signals`, `retain` (default); `net` (opt-in).

### Design
- **Guest under host executors** (ROS 2 / Apex / Taktora): emits through the
  `tracing` facade only (never installs a subscriber); signal handling is
  opt-in; lean builds via `default-features = false`.

[0.1.0]: https://github.com/UpTux/ifm-cr1140/releases/tag/v0.1.0
