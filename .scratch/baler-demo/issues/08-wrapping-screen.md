---
status: ready-for-agent
title: Baler demo wrapping screen (simulated timed cycle)
depends_on: [02-screen-router, 03-can-command-seam]
---

# Wrapping screen — simulated ~5 s cycle with progress

The Wrapping screen: a momentary "activate wrapping" that runs a simulated timed
cycle with a progress bar, emitting a CAN start frame.

## Context

Covers function (3). Start Wrap sends a single WRAP start frame; the ~5 s
progress and completion are purely local UI (no done/abort frames). The start
key is ignored while a cycle is already running.

## What to build

A screen with an IDLE / WRAPPING state and a progress bar. F1 Start Wrap sends
the WRAP command and begins a ~5 s cycle that fills the bar and auto-returns to
IDLE; F2 Cancel returns to IDLE immediately. Start is a no-op while WRAPPING.

## Acceptance criteria

- [ ] Wrapping screen shows IDLE / WRAPPING and a progress bar
- [ ] F1 Start Wrap sends a single WRAP start frame and runs a ~5 s cycle that
      auto-completes back to IDLE
- [ ] F1 ignored while a cycle is active; F2 Cancel returns to IDLE
- [ ] Cycle state machine (start/progress/complete/cancel/ignore-while-active)
      covered by host-runnable unit tests (injected time)

## Blocked by

- 02-screen-router
- 03-can-command-seam

## Comments

_None yet._
