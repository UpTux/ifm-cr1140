---
status: ready-for-agent
---
# 05 — Deploy: mask / unmask `ifm-retain-srv`

Taking ownership of the SPI EEPROM requires stopping `ifm-retain-srv` (it manages the
CODESYS retain segments we're overwriting), and restore-to-stock must bring it back.

Spec: [ADR-0002](../../../docs/adr/0002-retain-store-on-spi-eeprom.md) §Consequences.
`ifm-retain-srv` is `active` on the device (writes 3 segments to `spi1.0/eeprom`); it is
meaningless without the CODESYS runtime (already masked).

## Scope

- `deploy/install.sh`: `systemctl mask --now ifm-retain-srv` (alongside the existing
  `codesys.service` handling). Guard with `|| true` like the other units. This must
  happen **before** our app first writes the EEPROM, so the daemon can't race our writes.
- `deploy/restore-codesys.sh`: `systemctl unmask ifm-retain-srv` (it reinitializes its
  segments from CODESYS RAM on next CODESYS run — stock restore stays clean). Place it
  next to the existing `unmask codesys.service` block.

## Acceptance criteria

- After `install.sh`: `systemctl is-enabled ifm-retain-srv` → `masked`, and it is not
  running (`is-active` → inactive).
- After `restore-codesys.sh`: `ifm-retain-srv` is unmasked (back to its stock
  enabled/active state).
- Both scripts remain idempotent and non-fatal if the unit is already in the target state.

## Out of scope

- The EEPROM read/write code (issues 01/03).
- Migrating any existing CODESYS retain data (intentionally discarded — see ADR-0002).
