# Context: cr1140-baler-demo

## Responsibility

A second reference **demo application** for the CR1140/CR1141: a round-baler
operator panel. Peer to `cr1140-slint-demo` (which is a system dashboard); this
one is a *domain* app that exercises the full stack — a multi-screen Slint UI on
`/dev/fb0`, the keypad, the reflash-surviving `retain::Store`, and CAN command
output. It sits at the top of the dependency chain (`baler-demo → slint + sdk →
hal`) and owns its standalone `main` super-loop (16 ms tick, render-when-dirty
blit, `ShutdownGuard` + opt-in SIGINT/SIGTERM).

It demonstrates three independent operator functions (no interlocks between
them):

1. **Bale counter** — a per-launch session count and a reflash-surviving
   lifetime total (`retain::Store<BalerRetain>`), plus a stat strip.
2. **Knives in/out** — a session-only IN/OUT switch.
3. **Activate wrapping** — a simulated ~5 s timed cycle with a progress bar.

The UI is light-theme and uses **built-in Slint elements only** (no std-widgets)
to keep the pure-Rust static `aarch64-unknown-linux-musl` build (same constraint
and reasoning as `cr1140-slint-demo`).

## Architecture (how the slices fit)

The function logic lives in **pure, host-testable state machines** with
**injected time** (`now_ms: u64`); `main.rs` is the I/O shell that wires the
keypad and screens to those models and to the seams. User actions return CAN
[`Command`]s rather than touching hardware directly.

| Module | Role |
|--------|------|
| `src/router.rs`   | Pure navigation state machine (Menu ↔ Bale/Knives/Wrapping; F6 = Back/Exit). |
| `src/can.rs`      | The CAN command seam: pure frame `encode` + a send-or-log `BalerBus`. |
| `src/counter.rs`  | The bale `Counter`: session + lifetime total, bales/hr, resets, retain dirty-tracking. Owns `BalerRetain`. |
| `src/knives.rs`   | The `Knives` IN/OUT toggle (in-memory only). |
| `src/wrapping.rs` | The `Wrapping` timed-cycle state machine (auto-completes from `now_ms`). |
| `src/main.rs`     | Linux-only super-loop: hardware seams, retain I/O, per-screen key routing, change-detected UI refresh. Non-Linux target is a stub. |

## Glossary

| Term | Meaning |
|------|---------|
| Session count | Bales counted since launch; resets to 0 every launch and on F1 Reset Session. |
| Lifetime total | The odometer-style bale total that survives reboot/reflash via `retain::Store`. |
| Knives IN / OUT | Crop-cutting knife bank engaged (IN) or retracted (OUT). Live machine state, not persisted; starts OUT. |
| Wrapping cycle | The simulated ~5 s net/film wrap of a finished bale (`WRAP_DURATION_MS`). |
| Bales/hr | Live throughput: the average rate since the **first** session bale, extrapolated to an hour (documented in `counter.rs`). |
| Avg Ø, Net used | **Mock** stats — the demo has no diameter/net sensor, so these are clearly-labelled placeholder values. |
| `BalerRetain` | The sole retain `T`: `{ version: u8, total_bales: u32 }`. |
| `Command` | An outbound baler CAN command (`Knives(bool)` / `WrapStart` / `Bale(u32)`). |
| Soft-key | An F1–F6 footer cell whose label depends on the active screen (no physical Esc/Back — F6 is the soft Back/Exit). |

## Conventions / decisions

- Depends on the SDK and Slint integration; reaches the HAL only for the seams
  the SDK does not wrap (`can::CanBus`, `sys::Nvmem`).
- Function models are pure and host-tested with injected time (`now_ms`); the
  framebuffer/evdev/CAN I/O lives only in the Linux-only `main`. Run host tests
  with `cargo test -p cr1140-baler-demo`; build/deploy with `just build-baler` /
  `just run-baler`.
- Menu cursor **wraps** (Up from the first entry → last, Down from last →
  first).
- Retain writes are **debounced** (coalesce bale bursts; `PERSIST_DEBOUNCE_MS`)
  and always **flushed on graceful exit** — staying within the retain module's
  documented "low-frequency only" envelope (ADR-0002). The total-reset is
  guarded by a ~2 s double-confirm.

### ⚠ Caveat: this demo owns the whole retain EEPROM region

`BalerRetain` is the **sole** `retain::Store` type for the 32 KB SPI EEPROM —
the demo assumes it owns the entire region. Co-running with another app that
stores its own config (e.g. the SDK's `net::NetworkConfig`) in retain would
**clobber** this total, and vice-versa. A real multi-app deployment must compose
a single shared top-level retain `T`, not run two independent `Store`s over the
same device.

### ⚠ Caveat: the CAN message map is a placeholder

The IDs and payloads in `can.rs` are **demo placeholders**, not a production
map:

| Signal | ID | Payload |
|--------|------|---------|
| Knives | `0x200` | `[0]` = 0 out / 1 in |
| Wrap   | `0x201` | `[0]` = 1 start |
| Bale   | `0x202` | `[0..4]` = total count, LE u32 |

Replace them with the real baler DBC / J1939 definitions before any on-machine
use. A real `can0` is used when present; otherwise the frame that *would* be
sent is logged ("mock" writes). No inbound CAN is decoded — bale events,
diameter, and net usage are simulated by keypad / mock values.

- _(Record further decisions in `docs/adr/`.)_
