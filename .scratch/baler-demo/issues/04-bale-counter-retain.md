---
status: ready-for-agent
title: Baler demo bale counter + retained lifetime total
depends_on: [02-screen-router, 03-can-command-seam]
---

# Bale counter + retained lifetime total

The Bale Counter screen: a session count and a reflash-surviving lifetime total,
incremented by a simulated bale event, each bale emitting a CAN frame.

## Context

Covers function (1) of the demo. Session resets every launch; the lifetime total
persists across reboot/reflash via `retain::Store`. The retain schema is the
sole `T` for the region (the demo owns it).

Retain schema (from the agreed design):

```
BalerRetain { version: u8, total_bales: u32 }
```

Writes are debounced (coalesce bale bursts) and always flushed on graceful exit
— staying within the retain module's documented "low-frequency only" envelope.

## What to build

The Bale Counter view rendering the SESSION and LIFETIME TOTAL cards (per the
mockup). A `+1 Bale (sim)` soft-key increments both counts and sends the BALE
command via the CAN seam. On start, total loads from retain (`load_or_default`);
session starts at 0. Total is persisted debounced + on exit.

## Acceptance criteria

- [ ] SESSION and LIFETIME TOTAL cards rendered on the Bale Counter screen
- [ ] `+1 Bale (sim)` increments session + total and sends a BALE frame
- [ ] Total loaded from `retain::Store<BalerRetain>` on start; session = 0
- [ ] Total persisted debounced and flushed on graceful exit
- [ ] Counter increment/load/persist logic covered by host-runnable tests
      (temp-file nvmem, no device)

## Blocked by

- 02-screen-router
- 03-can-command-seam

## Comments

_None yet._
