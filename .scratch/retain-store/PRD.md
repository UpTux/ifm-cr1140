# Retain store on the SPI EEPROM

Reflash-surviving persistence for factory calibration and network/IP config, on the
only writable storage a firmware update never touches (the 32 KB SPI EEPROM).

**Spec / source of truth:** [`docs/adr/0002-retain-store-on-spi-eeprom.md`](../../docs/adr/0002-retain-store-on-spi-eeprom.md)
(follows ADR-0001). Hardware ground truth: [`docs/device-facts.md`](../../docs/device-facts.md)
("Capability recon", "nvmem / EEPROM map", "Firmware update").

## Why

`config::Store` lives on p2, which a `.swu` **`mkfs.ext4 -F`'s on every update**. The SPI
EEPROM (`/sys/bus/spi/devices/spi1.0/eeprom`) is never touched → the only home for data
that must survive a firmware update. Verified free of factory data (that lives read-only
on the I²C EEPROMs).

## Issues

| # | Title | Status | Depends on |
|---|-------|--------|------------|
| 01 | HAL `sys::Nvmem` primitive | done | — |
| 02 | HAL read-only factory-EEPROM accessors | ready-for-human (v1 coded; live/manual verify pending) | 01 |
| 03 | SDK `retain::Store<T>` (A/B + CRC32) | done | 01 |
| 04 | SDK feature-gated `net` module (`nmcli` apply) | ready-for-human (coded; on-device smoke pending) | 03 (integration only) |
| 05 | Deploy: mask/unmask `ifm-retain-srv` | done | — |

Workload target is **power-safe low-frequency settings** — no high-frequency retain on
the EEPROM (route any future high-frequency need to SNVS LPGPR instead).
