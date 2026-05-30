---
status: ready-for-agent
title: Baler demo knives in/out screen
depends_on: [02-screen-router, 03-can-command-seam]
---

# Knives screen — in/out toggle

The Knives screen: a session-only IN/OUT state with a toggle that emits a CAN
command on each change.

## Context

Covers function (2). Knife position is live machine state, not a retained
setting — it starts OUT each launch and is not persisted.

## What to build

A screen showing a large IN / OUT state indicator. F1 toggles between IN and OUT
and sends the KNIVES command via the CAN seam (payload reflects the new
position). State is in-memory only.

## Acceptance criteria

- [ ] Knives screen shows current IN / OUT state prominently
- [ ] F1 toggles the state and sends a KNIVES frame with the matching payload
- [ ] State starts OUT each launch; not persisted
- [ ] Toggle logic covered by a host-runnable unit test

## Blocked by

- 02-screen-router
- 03-can-command-seam

## Comments

_None yet._
