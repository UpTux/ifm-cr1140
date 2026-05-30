---
status: ready-for-agent
title: Baler demo counter stats row (Bales/hr live, Avg Ø + Net mock)
depends_on: [04-bale-counter-retain]
---

# Counter stats row — Bales/hr (live), Avg Ø + Net used (mock)

The stat strip beneath the counter cards: one live-computed metric and two
clearly-placeholder mock metrics.

## Context

Per the mockup and the "compute what's free, mock the rest" decision. We have no
diameter or net sensor in the demo, so Avg Ø and Net used are plausible mock
values; Bales/hr is derived from the session's bale timestamps.

## What to build

Track bale timestamps for the current session and compute a Bales/hr rate
(define the window — e.g. rolling over the session, documented). Render the
three-cell stat row (Avg Ø, Bales/hr, Net used) under the counter cards. Net
used may tie to completed wrap cycles once Wrapping exists, but a static mock is
acceptable here; Avg Ø is a static mock value, clearly labelled.

## Acceptance criteria

- [ ] Bales/hr computed from real session bale timestamps; updates as bales land
- [ ] Avg Ø and Net used shown as documented mock placeholders
- [ ] Stat row rendered beneath the SESSION/LIFETIME cards
- [ ] Bales/hr math covered by host-runnable unit tests (no wall-clock flakiness
      — inject time)

## Blocked by

- 04-bale-counter-retain

## Comments

_None yet._
