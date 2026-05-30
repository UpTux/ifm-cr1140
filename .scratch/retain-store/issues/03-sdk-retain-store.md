---
status: done
---
# 03 — SDK `retain::Store<T>` (A/B double-buffer + CRC32)

New `retain` module in `cr1140-sdk`, parallel to `config::Store` but over the HAL
`sys::Nvmem` primitive (issue 01) — power-safe, reflash-surviving persistence.

Spec: [ADR-0002](../../../docs/adr/0002-retain-store-on-spi-eeprom.md) §Decision.3.

Depends on: **issue 01** (HAL `sys::Nvmem`).

## Scope

- `retain::Store<T: Serialize + DeserializeOwned>` with `open(nvmem) -> SdkResult<Self>`,
  `load() -> SdkResult<Option<T>>`, `load_or_default()`, `save(&T) -> SdkResult<()>`
  (mirror the `config::Store` surface).
- **A/B double-buffer.** Two fixed slots in the 32 KB region. Slot layout:
  `[magic, version, seq, len, CRC32, payload]`. `save()` writes the **inactive** slot
  (bump `seq`), then it becomes current by higher `seq` *and* valid CRC. `load()` picks
  the highest-`seq` slot that passes CRC; neither valid → `Ok(None)`.
- **`magic`** rejects CODESYS leftover bytes / blank EEPROM. **`version`** future-proofs
  the schema (unknown version → treat as absent, don't panic).
- **Serialization: `postcard`** (new dep; compact binary serde).
- **Endurance: `save()` is write-only-if-changed** — read the active slot, compare the
  encoded payload, **no-op if identical**. Only write (and bump `seq`) on real change.
- Errors via existing `SdkError` (add a variant if CRC/format failures don't map cleanly).

## Acceptance criteria

- `cargo test -p cr1140-sdk` passes. Tests (against a temp-file-backed fake `Nvmem`):
  round-trip; torn-write recovery (corrupt the inactive slot → previous value survives);
  blank/garbage region → `load()` is `None`; `save()` of an unchanged value performs no
  write (assert `seq` unchanged); version-mismatch handled as absent.
- Documented as low-frequency only; note SNVS LPGPR as the future high-frequency path.
- `cr1140-sdk/CONTEXT.md` glossary updated (`retain::Store`), `config` vs `retain` contrast.

## Out of scope

- `net` module / network apply (issue 04).
- Multi-region/segmented retain (ADR-0002 records single composed blob; revisit later).

## Comments

**2026-05-30 — implemented.** `cr1140_sdk::retain::Store<T>` added
(`cr1140-sdk/src/retain.rs`, behind the default `retain` feature; re-exported as
`RetainStore`). `open` / `load` / `load_or_default` / `save` over HAL `sys::Nvmem`,
mirroring `config::Store`. Two equal A/B slots framed `[magic "RTNS" | version | seq
| len | crc32 | payload]` (20-byte header); `save()` writes the inactive slot with a
bumped `seq`, becoming current only by higher seq **and** valid CRC. CRC-32 (IEEE,
`0xEDB88320`) is hand-rolled (no dep); serialization is `postcard` (new dep,
`use-std`). `save()` is write-only-if-changed (reads active slot, no-ops + no seq
bump on identical payload). Bad magic / unknown version / CRC fail / corrupt length
all read as "absent" (`Ok(None)`); encode/decode/oversize → new `SdkError::Retain`.

11 unit tests (temp-file fake, no `tempfile` dep): CRC known-vector, round-trip,
blank→None, slot-alternation + seq bump, unchanged-save no-op, torn-write recovery,
corrupt-payload→None, unknown-version→absent, oversized→err, too-small-device→err.
`cargo test -p cr1140-sdk` → 41 passed; clippy clean. `cr1140-sdk/CONTEXT.md` updated
(`retain::Store` glossary + `config` vs `retain` lifetime contrast). Documented
low-frequency-only with SNVS LPGPR as the future high-frequency path.
