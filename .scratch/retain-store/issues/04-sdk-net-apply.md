---
status: ready-for-agent
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
