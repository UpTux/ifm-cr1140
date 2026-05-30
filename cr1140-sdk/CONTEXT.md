# Context: cr1140-sdk

> Stub — fill in as domain terms get resolved (e.g. via `/grill-with-docs`).

## Responsibility

App-building **conveniences** layered on `cr1140-hal` — the "batteries" a native
CR1140 app needs regardless of how it draws. Deliberately **UI-framework agnostic**:
no Slint, no rendering, and **no run loop**. Depends on the HAL; nothing in the
workspace below it.

**Guest-friendly by design:** logs through the `tracing` facade without installing a
subscriber, and never grabs signals by default — so it composes cleanly under host
executors (ROS 2 / Apex / Taktora). (See memory: the SDK is a guest in someone
else's process.)

## Glossary

| Term | Meaning |
|------|---------|
| `led::LedDriver` | RGB keypad-LED animation modes (effects) |
| `metrics::Telemetry` | collector for generic Linux telemetry (CPU, memory, load, uptime) |
| `metrics::Snapshot` | aggregated telemetry sample; `MemInfo` is the memory breakdown |
| `device`        | device & OS identity and network state (wall-clock comes from the OS/`std`, **not** a HAL RTC — see HAL capability scope) |
| `guard::ShutdownGuard` | RAII guard restoring backlight/LED on exit; opt-in SIGINT/SIGTERM for standalone binaries |
| `ShutdownFlag`  | cooperative shutdown signal observed by app loops |
| `config::Store` | atomic persistence for the p2 overlay (`DEFAULT_APP_DIR` = `/home/cds-apps`); gated by the default `config` feature |
| `SdkError` / `SdkResult` | SDK-level error type and result alias |

## Conventions / decisions

- UI-framework agnostic: no rendering and no run loop live here.
- Logging via the `tracing` **facade only** — no subscriber installed (the host owns that).
- Signal handling and config persistence are **opt-in** / **feature-gated** to keep the guest footprint minimal.
- **Planned ([ADR-0002](../docs/adr/0002-retain-store-on-spi-eeprom.md)):** a `retain`
  module — `retain::Store<T>` over the HAL `sys::Nvmem` primitive (SPI EEPROM), A/B
  double-buffer + CRC32, `postcard`-encoded, `save()` write-only-if-changed. Unlike
  `config::Store` (p2 overlay, **wiped on every reflash**), retain **survives firmware
  updates** — for factory calibration / network config. Plus a feature-gated `net`
  module (`net::apply(&NetworkConfig)` via `nmcli`, off by default) so an app can
  re-apply retained network settings at boot or on user input.
- _(Record further decisions in `docs/adr/`.)_
