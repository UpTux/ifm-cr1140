# 2. Reflash-surviving retain store on the SPI EEPROM

- Status: accepted
- Date: 2026-05-30
- Deciders: Patrick Dahlke
- Scope: `cr1140-hal` (`sys::Nvmem` + factory accessors), `cr1140-sdk` (`retain`, `net`), deployment
- Follows: [`0001-codesys-fb-capability-scope.md`](0001-codesys-fb-capability-scope.md) (retain was the one capability reopened by recon)

## Context

ADR-0001 deferred "retain memory" pending on-device recon. Recon (2026-05-30,
[`../device-facts.md`](../device-facts.md) "Capability recon" + "nvmem / EEPROM map")
found genuine reflash-proof NV hardware that SDK `config::Store` does not equal:

- **SPI EEPROM** `spi1.00` (32 KB), currently carved into CODESYS-retain segments by
  `ifm-retain-srv`. Verified to hold **only** CODESYS scratch — no factory data, no MAC.
- Factory identity/calibration lives on the **I²C EEPROMs** (`0-00513` device identity +
  MAC; `0-00502` panel/board, `…notouch…`), which are read-only and survive reflash on
  their own.
- Tiny battery-backed NV also exists (SNVS LPGPR 16 B, RV-3028 NVRAM 2 B) — reserved for
  any future high-frequency retain, not used here.

The firmware update path is decisive (`sw-description`, "Firmware update" in
device-facts): a `.swu` **always `mkfs.ext4 -F`'s p2** (the overlay — `/home/cds-apps`,
`/etc`) and formats p3 whenever the partition table changes. **The EEPROMs are never
touched.** So `config::Store` on p2 does **not** survive a reflash, and p3 is not a
guaranteed survivor — the SPI EEPROM is the only writable storage that survives every
update.

Requirement: factory calibration and **network/IP config** must survive a firmware
update. Live network config under `/etc` (p2) is wiped on update, so it must be stored in
retain and re-applied on boot. Most app settings stay on `config::Store` (no EEPROM wear).

## Decision

Build a reflash-surviving **retain store on the SPI EEPROM**, layered HAL→SDK, plus a
feature-gated network-apply capability. Workload target: **power-safe, low-frequency
settings** (no high-frequency app today).

1. **Ownership.** Take the whole 32 KB SPI EEPROM. `systemctl mask ifm-retain-srv`
   (alongside `codesys.service`) — its segments are meaningless without the CODESYS
   runtime we disabled. Factory data is untouched (it lives on the I²C EEPROMs).
2. **HAL primitive** (`cr1140-hal`, `sys`): a thin `Nvmem` wrapper — discover the device
   by its **stable sysfs path** (`/sys/bus/spi/devices/spi1.0/eeprom`), not the renumber-
   able `nvmem` index `spi1.00`; `read_at` / `write_at(offset, &[u8])` / `len()`. No
   policy. Plus **read-only factory accessors** for the I²C identity EEPROM (serial / MAC
   / article). This is what completes the capability under ADR-0001 goal B.
3. **SDK store** (`cr1140-sdk`, new `retain` module): `retain::Store<T: Serialize +
   DeserializeOwned>` over the HAL primitive — parallel to `config::Store`.
   - **Integrity: A/B double-buffer.** Two slots, each `[magic, version, seq, len,
     CRC32, payload]`. Write the inactive slot + fsync; it becomes current by higher
     `seq` *and* valid CRC. Reader picks the highest-seq slot that passes CRC. A torn
     write only corrupts the inactive slot; the previous good slot survives. `magic`
     rejects CODESYS leftovers / blank EEPROM; `version` future-proofs the schema.
   - **Serialization: `postcard`** (compact binary serde; guest-minimal-footprint).
   - **Schema: one composed A/B blob.** The app owns the top-level `T` and embeds the
     SDK's `net::NetworkConfig` if it wants network in retain. Single framing, single
     slot pair (mirrors `config::Store<T>`).
   - **Endurance: `save()` is write-only-if-changed** — reads the active slot, no-ops if
     the encoded bytes are identical. Makes "call `save()` often" safe by construction.
4. **Network apply** (`cr1140-sdk`, feature-gated `net` module — off by default, per the
   "SDK is a guest" principle): `net::apply(&NetworkConfig)` shells out to `nmcli`
   (matches the stock `ifmnetworkmanager` stack), **idempotent** (modify-or-add a named
   connection, then bring it up). The **app** owns the timing — call it during boot init
   and/or from a UI handler on user input. Retain is the source of truth; the live
   `/etc` config is rewritten each boot.

## Consequences

**Positive**

- Factory calibration and network config survive every firmware update — the stated
  requirement — on the only storage the `.swu` never touches.
- Power-safe by construction (A/B + CRC), self-healing, and safe against accidental
  hot-loop `save()` (write-coalescing).
- Clean layering: device access in the HAL, integrity/format policy in the SDK
  (parallel to `config::Store`), host-specific network policy quarantined behind a
  feature flag.
- Ordinary settings stay on `config::Store` → near-zero EEPROM wear.

**Negative / trade-offs**

- Owning the EEPROM **destroys the existing CODESYS retain bytes**. Acceptable: they are
  meaningless without CODESYS, and restore-to-stock (`systemctl unmask ifm-retain-srv`)
  lets `ifm-retain-srv` reinitialize its segments. Deployment scripts must:
  `install.sh` → `mask ifm-retain-srv`; `deploy/restore-codesys.sh` → `unmask
  ifm-retain-srv`.
- `net::apply` depends on `nmcli` being present and on NetworkManager. Quarantined behind
  the `net` feature; D-Bus (`zbus`) is the future upgrade path if the subprocess
  dependency becomes a problem.
- High-frequency retain is explicitly **not** supported on the EEPROM (endurance). A
  future high-frequency workload routes to SNVS LPGPR (16 B, unlimited writes), not here.

## Related

- [`0001-codesys-fb-capability-scope.md`](0001-codesys-fb-capability-scope.md) — parent decision
- [`../device-facts.md`](../device-facts.md) — "Capability recon", "nvmem / EEPROM map", "Firmware update"
- [`../../cr1140-hal/CONTEXT.md`](../../cr1140-hal/CONTEXT.md) — `sys::Nvmem` + factory accessors
- [`../../cr1140-sdk/CONTEXT.md`](../../cr1140-sdk/CONTEXT.md) — `retain`, `net`; contrast with `config::Store`
