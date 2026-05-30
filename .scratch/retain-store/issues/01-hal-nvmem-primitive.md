---
status: ready-for-agent
---
# 01 — HAL `sys::Nvmem` primitive

Thin, typed wrapper over an nvmem byte device in `cr1140-hal` (`sys` module). No policy
(no A/B, no CRC) — just typed offset access. Foundation for the SDK retain store (issue 03).

Spec: [ADR-0002](../../../docs/adr/0002-retain-store-on-spi-eeprom.md) §Decision.2.
EEPROM map: [device-facts.md](../../../docs/device-facts.md) "nvmem / EEPROM map".

## Scope

- New type in `cr1140-hal::sys` (e.g. `Nvmem`) wrapping an nvmem binary attribute.
- **Discovery by stable sysfs path**, not the nvmem index name. The SPI retain EEPROM is
  `/sys/bus/spi/devices/spi1.0/eeprom` (its nvmem node is `spi1.00`, but the index can
  renumber across kernels / probe order — do **not** key on it). Provide an `open(path)`
  plus a convenience constructor for the known SPI retain EEPROM.
- API: `len() -> usize`, `read_at(offset, &mut [u8]) -> HalResult<()>`,
  `write_at(offset, &[u8]) -> HalResult<()>`. Bounds-check against `len()`; out-of-range
  → `HalError::OutOfRange`. I/O failure → `HalError::Io`.
- Writes go to the nvmem attribute (pwrite at offset). Document that durability/atomicity
  is the **caller's** concern (the SDK store layers A/B + CRC on top).

## Acceptance criteria

- `cargo test -p cr1140-hal` passes; unit tests cover bounds checks and round-trip
  read/write against a temp-file-backed fake (don't require real hardware in tests).
- Added to the `cr1140-hal` `sys` glossary row in `cr1140-hal/CONTEXT.md`.
- No new heavyweight deps (std + existing error types only).

## Out of scope

- A/B buffering, CRC, serialization (issue 03).
- The factory-EEPROM read accessors (issue 02) — separate, depends on this.
