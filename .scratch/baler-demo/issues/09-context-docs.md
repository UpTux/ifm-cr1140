---
status: ready-for-agent
title: Baler demo CONTEXT.md + workspace docs
depends_on: [04-bale-counter-retain, 07-knives-screen, 08-wrapping-screen]
---

# CONTEXT.md + workspace docs

Document the new crate's bounded context and wire it into the repo's domain-doc
map, including the retain-region ownership caveat.

## Context

Per CLAUDE.md, each crate carries a `CONTEXT.md` and is listed in the root
`CONTEXT-MAP.md`. The baler demo also makes a non-obvious assumption worth
recording: it owns the whole retain EEPROM region (single `BalerRetain` blob),
so co-running with an app that stores net config in retain would clobber it.

## What to build

Fill in `cr1140-baler-demo/CONTEXT.md` (responsibility, glossary of the baler
domain terms, conventions) and add a row to `CONTEXT-MAP.md`. Record the
retain-region ownership note and the placeholder-CAN-map caveat where a future
reader will find them.

## Acceptance criteria

- [ ] `cr1140-baler-demo/CONTEXT.md` filled in (responsibility, glossary,
      conventions) — no longer a stub
- [ ] Row added to root `CONTEXT-MAP.md` for the baler demo
- [ ] Retain-region ownership caveat documented
- [ ] Placeholder CAN message-map noted as to-be-replaced

## Blocked by

- 04-bale-counter-retain
- 07-knives-screen
- 08-wrapping-screen

## Comments

_None yet._
