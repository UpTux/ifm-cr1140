# PRD: Baler demo — round-baler operator panel

## Problem

We need a second reference application for the CR1140/CR1141 that demonstrates a
realistic agricultural use case — a round-baler operator panel — exercising the
full stack: Slint UI on the framebuffer, keypad input, the reflash-surviving
`retain::Store`, and CAN command output. The existing `cr1140-slint-demo` is a
system dashboard; this is a domain app.

## Background / device facts

- Display 800×480, software-rendered to `/dev/fb0` via `cr1140-slint`
  (`FbPlatform`); no winit/GPU/fontconfig — static `aarch64-unknown-linux-musl`.
- Keypad (`/dev/input/event1`, gpio-keys): F1–F6, Up, Down, Left, Right, Enter.
  No physical Esc/Back — soft labels map onto F-keys. No buzzer.
- Retain EEPROM (`spi1.00`, 32 KB) via `cr1140_hal::sys::Nvmem::open_retain()` →
  `cr1140_sdk::retain::Store<T>` (A/B + CRC32, "low-frequency only", ADR-0002).
- CAN via `cr1140_hal::can::CanBus` (SocketCAN; `open` / `send_std` / `recv`).

## Goals

- A new `cr1140-baler-demo` workspace member, peer to `cr1140-slint-demo`.
- Three operator functions: **bale counter** (session + retained lifetime
  total), **knives in/out** switch, **activate wrapping** (timed cycle).
- Light-theme, multi-screen, soft-key-footer UI (built-in Slint elements only,
  no widget styling — keeps the pure-Rust static build). Per the agreed mockup.
- Signals go out over CAN; in the demo a real `can0` is used when present,
  otherwise the frame that *would* be sent is logged ("mock" writes).
- Lifetime total survives a reflash via `retain::Store`.

## Non-goals

- Real machine input frames (bale-complete, diameter, net usage are simulated
  by keypad / mock values — no inbound CAN decode in this demo).
- Interlocks between functions (the three are fully independent).
- A production CAN message map — IDs/payloads are documented placeholders to be
  replaced with the real baler DBC/J1939 later.
- High-frequency retain writes (debounced + on-exit only; stays in the
  retain module's low-frequency envelope).

## UI summary (agreed)

Header on every screen: machine/field/READY/ISO as static placeholder strings,
live clock from system time.

- **Menu (home):** selectable list (Up/Down move, Enter opens) — Bale Counter /
  Knives / Wrapping. F6 = Exit.
- **Bale Counter:** SESSION + LIFETIME TOTAL cards; stat row Avg Ø (mock) ·
  Bales/hr (live) · Net used (mock). F1 Reset Session · F2 +1 Bale (sim) ·
  F3 Reset Total (double-confirm) · F6 Back.
- **Knives:** big IN / OUT state. F1 Toggle · F6 Back.
- **Wrapping:** IDLE / WRAPPING + progress bar (~5 s cycle). F1 Start Wrap ·
  F2 Cancel · F6 Back.

## CAN message map (placeholder — 11-bit standard, replace with real DBC)

| Signal | ID | Payload |
|--------|------|---------|
| Knives | `0x200` | `[0]` = 0 out / 1 in |
| Wrap   | `0x201` | `[0]` = 1 start |
| Bale   | `0x202` | `[0..4]` = total count, LE u32 |

## Retain schema

`BalerRetain { version: u8, total_bales: u32 }` — the sole retain `T`; the demo
owns the EEPROM region. Co-running with an app that stores net config in retain
would clobber it (documented in CONTEXT.md).
