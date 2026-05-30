---
status: ready-for-agent
title: Baler demo CAN command seam (send-or-log) + message map
depends_on: [01-crate-skeleton-shell]
---

# CAN command seam — real `can0` with log fallback + placeholder message map

A single outbound path for baler commands that uses a real SocketCAN interface
when present and otherwise logs the frame it would have sent ("mock" writes).

## Context

Signals go out over CAN. In the demo a real bus is optional, so the seam tries
`cr1140_hal::can::CanBus::open("can0")` at startup; on success it sends real
frames via `send_std`, on failure (or per-send error) it logs the formatted
frame through `tracing`. The message map is a documented placeholder table to be
replaced with the real baler DBC later.

## What to build

A small bus abstraction exposing the three baler commands (knives, wrap, bale)
that encodes each to a standard 11-bit frame and routes it to the real socket or
the log sink. A const table defines the placeholder IDs/payloads. Frame encoding
is pure and host-testable.

Placeholder map (11-bit standard):

| Signal | ID | Payload |
|--------|------|---------|
| Knives | `0x200` | `[0]` = 0 out / 1 in |
| Wrap   | `0x201` | `[0]` = 1 start |
| Bale   | `0x202` | `[0..4]` = total count, LE u32 |

## Acceptance criteria

- [ ] Opens `can0` at startup; sends real frames when available
- [ ] Falls back to a `tracing` log of the exact frame (id + bytes) when the
      bus is absent or a send fails — never panics, never blocks the UI
- [ ] Placeholder ID/payload const table, documented as demo placeholders
- [ ] Frame encoding for all three commands covered by host-runnable unit tests
- [ ] Selectable interface name (default `can0`) — e.g. via a CLI arg

## Blocked by

- 01-crate-skeleton-shell

## Comments

_None yet._
