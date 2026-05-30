---
status: ready-for-human
---
# 02 — HAL read-only factory-EEPROM accessors

Expose the device's read-only factory identity (serial, MAC, article, product) from the
I²C identity EEPROM, in `cr1140-hal::sys`. Read-only — never written.

Spec: [ADR-0002](../../../docs/adr/0002-retain-store-on-spi-eeprom.md) §Decision.2.
Source bytes observed: [device-facts.md](../../../docs/device-facts.md) "nvmem / EEPROM map"
(`0-00513`, I²C 0x51).

## Why ready-for-human (not AFK-ready)

The EEPROM uses an ifm format (magic `vhip`) whose field boundaries are only **partially
reverse-engineered** from one dump. Confirmed bytes from `0-00513`:

- `vhip` magic at 0x00
- article `100008599862` (~0x0c), product `CR1140` (0x1c),
  `ecomatDisplay/4.3"/STD./E` (0x3c), asset/name `pdm3_4_001` (0x84),
  build date `28.03.2025, 14:01:14` (0xa4), serial `7998407` (0xc8),
  **MAC `00:02:01:ab:bd:49`** (0xe9)

Field offsets/lengths beyond the MAC are inferred, not authoritative. A human should
confirm the layout against the **Programming manual CR1140 / CR1141** (referenced in
`docs/superpowers/specs/2026-05-29-cr1140-rust-hal-design.md`) before committing to a
parser, OR scope v1 to only the high-confidence fields.

## Scope

- `cr1140-hal::sys` read-only accessors (discover the I²C EEPROM by stable sysfs path).
- v1 minimum: `mac() -> HalResult<[u8;6]>` (offset confirmed) + raw `read_at` access.
- Stretch (pending manual): typed `serial()`, `article()`, `product()`, `built()`.

## Acceptance criteria

- MAC accessor verified against the live device (`00:02:01:ab:bd:49`).
- Any field beyond MAC is either backed by the manual or explicitly marked best-effort.
- No write path to the factory EEPROMs.

## Out of scope

- The writable SPI retain EEPROM (issues 01/03).
