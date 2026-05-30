---
status: ready-for-agent
title: Baler demo reset session + reset total (double-confirm)
depends_on: [04-bale-counter-retain]
---

# Reset session + reset total (double-confirm)

Two resets on the Bale Counter screen: an immediate session reset, and a guarded
lifetime-total reset that wipes the retain store.

## Context

Session is freely resettable; the lifetime total is an odometer, so its reset is
guarded by a double-confirm (press to arm → press again within ~2 s to commit).
Committing zeroes `total_bales` and persists it to retain.

## What to build

F1 Reset Session → session count to 0 immediately. F3 Reset Total → first press
arms an "press again to confirm" state shown in the UI; a second press within
the timeout zeroes the total and writes retain; the armed state auto-disarms on
timeout or on leaving the screen.

## Acceptance criteria

- [ ] F1 zeroes the session count immediately
- [ ] F3 arms a confirm state (visible in the UI); second press within ~2 s
      zeroes the lifetime total and persists it to retain
- [ ] Armed state auto-disarms on timeout and on screen change
- [ ] Reset/arm/disarm logic covered by host-runnable unit tests (injected time)

## Blocked by

- 04-bale-counter-retain

## Comments

_None yet._
