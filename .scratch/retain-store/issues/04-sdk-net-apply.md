---
status: ready-for-human
---
# 04 — SDK feature-gated `net` module (`nmcli` apply)

A `net` module in `cr1140-sdk`, **off by default** (feature `net`), so an app can re-apply
retained network settings — at boot or on user input. Keeps host (NetworkManager)
assumptions out of the guest-minimal default build.

Spec: [ADR-0002](../../../docs/adr/0002-retain-store-on-spi-eeprom.md) §Decision.4.

Depends on: **issue 03** for end-to-end use (app stores `NetworkConfig` in retain), but
the module itself only needs the `NetworkConfig` type — can be built in parallel.

## Scope

- `cargo` feature `net` (off by default). Under it:
  - `net::NetworkConfig` — `Serialize + DeserializeOwned` so apps embed it in their retain
    struct. Minimal v1 fields: connection name/interface, `method` (DHCP | static),
    static `address/prefix`, `gateway`, `dns` list. Keep it small; extend later.
  - `net::apply(&NetworkConfig) -> SdkResult<()>` — shells out to `nmcli`,
    **idempotent**: modify-or-add a named connection, then bring it up. Re-running with
    the same config is a no-op-equivalent (safe to call at boot *and* from a UI handler).
- Surface errors (nmcli missing, non-zero exit) via `SdkError` with the captured stderr.
- Logging through the `tracing` facade only (no subscriber) — per the guest principle.

## Acceptance criteria

- Default build (`--no-default-features` baseline + without `net`) does **not** pull in
  the module or any nmcli/network code.
- `cargo build -p cr1140-sdk --features net` compiles; unit tests cover `NetworkConfig`
  serde round-trip and the `nmcli` argument construction (DHCP vs static) via an
  injectable command runner (don't shell out in unit tests).
- Live smoke test on-device: apply a static IP, confirm `nmcli`/`ip addr` reflects it,
  then re-apply (idempotent) — record result in Comments.
- `cr1140-sdk/CONTEXT.md` updated (`net` module, feature-gated).

## Out of scope

- D-Bus (`zbus`) backend — recorded in ADR-0002 as the future upgrade path.
- Deciding *when* to apply (that's the app's call: boot init and/or UI handler).

## Comments

**2026-05-30 — code complete; moved to `ready-for-human` for the on-device smoke test.**
`cr1140_sdk::net` added (`cr1140-sdk/src/net.rs`, behind the off-by-default `net`
feature). `NetworkConfig` (connection / interface / `Method::{Dhcp,Static}` /
address / prefix / gateway / dns, `Serialize + Deserialize`) + `apply(&NetworkConfig)`.
`apply` is idempotent: probe `nmcli connection show <name>` → `modify` if it exists
else `add type ethernet`, append `ipv4.*` (DHCP clears static leftovers; static needs
address+prefix), then `connection up`. Errors (nmcli missing / non-zero exit / invalid
config) → new `SdkError::Net` with captured stderr. Logging via the `tracing` facade
only. The `nmcli` call goes through an injectable `CommandRunner` so unit tests assert
the argv without shelling out.

Verified AFK:
- Default build pulls in **no** net code: `cargo build -p cr1140-sdk --no-default-features`
  → ok; `net` module is `#[cfg(feature = "net")]`.
- `cargo build/test -p cr1140-sdk --features net` compiles; 7 unit tests (DHCP vs
  static argv, add-vs-modify, up call, stderr surfaced on failure, serde round-trip)
  pass; clippy clean. `cr1140-sdk/CONTEXT.md` updated.

**Still needs a human (hardware):** the live smoke test — apply a static IP on-device,
confirm via `nmcli` / `ip addr`, then re-apply to confirm idempotency. Record the
result here, then set `status: done`.
