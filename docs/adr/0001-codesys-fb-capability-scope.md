# 1. Mirror CODESYS FB *capabilities*, not the FB API

- Status: accepted
- Date: 2026-05-30
- Deciders: Patrick Dahlke
- Scope: workspace-wide (primarily `cr1140-hal`, touches `cr1140-sdk`)

## Context

This workspace replaces the stock CODESYS runtime on the ifm CR1140/CR1141
ecomatDisplay with a native Rust stack (`hal → sdk → slint → demo`). ifm ships a
CODESYS Function Block (FB) library for the CR1140 that wraps the device's hardware
functions as IEC 61131-3 blocks (display, keypad, LEDs, backlight, temperature, CAN,
buzzer, RTC, watchdog, retain, …).

The question that prompted this ADR: *the CODESYS environment provides special FBs for
the CR1140 — is there something we need to support as well?*

Two framings were possible:

- **Migration parity** — make existing CODESYS apps portable, i.e. reproduce the FB
  library's API/semantics.
- **Capability completeness** — ensure the HAL exposes every *real hardware capability*
  the device offers, and let native apps use idiomatic Rust instead of FB call
  conventions.

We build greenfield native apps; we are not porting ladder/ST programs. The
architecture (a thin typed HAL with "no application policy") already rejects emulating
CODESYS semantics.

## Decision

**Treat the ifm CR1140 CODESYS FB library as a capability checklist, not an API to
replicate.** The HAL exposes every real hardware capability the device offers;
applications consume it idiomatically in Rust. We pursue capability completeness, not
migration parity.

Corollaries:

- **Pure-software FBs are out of scope.** Protocol stacks (CANopen, J1939) are not
  hardware capabilities — an app layers them on the HAL `can` primitive if needed.
- **OS-provided facilities are not HAL capabilities.** Where full systemd Linux already
  owns a concern (wall-clock, time sync), the HAL does not wrap it.
- **Policy stays out of the HAL.** Where a capability exists but its *use* is a policy
  decision (auto-brightness curve, watchdog ownership, signal handling), the HAL exposes
  only the primitive; policy lives in the SDK or the app.

### Resolved scope

Out of scope (with rationale):

| Capability | Rationale |
|------------|-----------|
| Touchscreen     | Keypad-only SKU — no touch event node in live input recon |
| CAN-FD          | `mcp251xfd` is FD-capable, but classic CAN 2.0 suffices; revisit on real need |
| CANopen / J1939 | Software protocol stacks, not hardware capabilities |
| RTC             | Wall-clock is an OS/`std` concern, not a HAL module |
| Display orientation | Install-time `fw_setenv ifm_orientation`; `mxsfb`/`drmfb` won't honor runtime rotation |

Conditionally in scope — gated on a one-pass on-device recon (tracked in
[`../device-facts.md`](../device-facts.md) "Capability recon"):

| Capability | If present |
|------------|-----------|
| Buzzer / beeper      | Thin `sys::beep` / `EV_SND` tone write |
| Hardware watchdog    | systemd-owned by default; opt-in `/dev/watchdog` HAL primitive for app-owned liveness |
| Ambient-light sensor | Thin IIO `read_illuminance` (lux); auto-brightness curve is app/SDK policy |
| Retain memory        | Equivalent to SDK `config::Store` unless recon finds FRAM/battery-SRAM (`nvmem`) |

The *framing decision* above is immutable. The conditional rows are settled by recon
outcomes recorded in `device-facts.md`, not by editing this ADR; a capability that turns
out present is then a normal HAL feature, not a reopening of this decision.

## Consequences

**Positive**

- Native apps stay idiomatic Rust; no FB call-convention emulation layer to maintain.
- The HAL surface tracks physical hardware, keeping it thin and 1:1 with hardware concerns.
- Clear, recorded rationale for *absent* features — reviewers won't mistake "out of scope"
  for "missing."

**Negative / trade-offs**

- Existing CODESYS apps are **not** portable to this stack. Acceptable: we do greenfield.
- Capabilities the silicon supports but we don't expose (notably **CAN-FD**) are deferred,
  so a future FD requirement reopens that row.
- Some capabilities remain unconfirmed until the recon pass runs; until then the HAL gap
  list is provisional for buzzer / watchdog / ALS / retain.

## Related

- [`../../cr1140-hal/CONTEXT.md`](../../cr1140-hal/CONTEXT.md) — "Capability scope vs. the CODESYS FB library"
- [`../device-facts.md`](../device-facts.md) — hardware ground truth + "Capability recon"
- [`../../cr1140-sdk/CONTEXT.md`](../../cr1140-sdk/CONTEXT.md) — `device` (wall-clock is OS/`std`, not a HAL RTC)
