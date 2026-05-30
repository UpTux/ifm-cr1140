---
status: ready-for-agent
title: Baler demo screen router (menu + three sub-screens)
depends_on: [01-crate-skeleton-shell]
---

# Screen router — list menu + Bale/Knives/Wrapping sub-screens

Add navigation: a home menu and three (initially empty) sub-screens, driven by
the keypad, with the active screen swapping the visible content and soft-key
labels.

## Context

The app is multi-screen and menu-driven (agreed). The home menu is a selectable
list — Up/Down move the cursor, Enter opens the highlighted entry; F6 is
Exit on the menu and Back on every sub-screen. Routing should be a small pure
state machine so it is host-testable without a framebuffer.

## What to build

A current-screen state with transitions: Menu → {Bale Counter, Knives,
Wrapping} on Enter over the selected list row; sub-screen → Menu on Back; Exit
from the Menu. The footer soft-key labels and the rendered body switch with the
active screen. Sub-screen bodies are placeholders at this stage (titles only).

## Acceptance criteria

- [ ] Home menu lists Bale Counter / Knives / Wrapping with a visible cursor
- [ ] Up/Down move the cursor (clamped/wrapping — pick one, documented); Enter
      opens the selected screen
- [ ] F6 = Back on sub-screens (returns to Menu), Exit on the Menu
- [ ] Footer labels and screen title reflect the active screen
- [ ] Router transitions covered by host-runnable unit tests (no fb/evdev)

## Blocked by

- 01-crate-skeleton-shell

## Comments

_None yet._
