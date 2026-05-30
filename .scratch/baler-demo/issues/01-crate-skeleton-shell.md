---
status: ready-for-agent
title: Baler demo crate skeleton + light-theme shell on fb0
depends_on: []
---

# Crate skeleton + light-theme shell on `/dev/fb0`

Stand up a new `cr1140-baler-demo` workspace member that boots to a single
static light-theme screen on the framebuffer and reads the keypad — the runnable
end-to-end skeleton every later slice plugs into.

## Context

Peer to `cr1140-slint-demo`: same super-loop skeleton (16 ms tick,
`ShutdownGuard` + opt-in signal handler, render-only-when-dirty blit via
`cr1140-slint`'s `FbPlatform`, keypad via `cr1140_hal::input::ButtonReader`).
Differs in theme (light, per the mockup) and uses only built-in Slint elements
(no std-widgets) to keep the pure-Rust static `aarch64-unknown-linux-musl` build.

## What to build

A runnable binary that renders one screen — the agreed header (static
machine/field/READY/ISO placeholder strings + a live ticking clock from system
time) and a soft-key footer (six F1–F6 cells aligned to the keypad) — and exits
cleanly on a designated key and on SIGINT/SIGTERM.

## Acceptance criteria

- [ ] `cr1140-baler-demo` added to the workspace `members`; builds for the host
      and cross-compiles to static `aarch64-unknown-linux-musl`
- [ ] `build.rs` compiles `ui/app.slint` with `EmbedForSoftwareRenderer`
- [ ] Light-theme screen with header (static chrome + live clock) and an
      F1–F6 soft-key footer, rendered to `/dev/fb0`
- [ ] Keypad input drained each tick; clean exit via `ShutdownGuard`
- [ ] `just build-baler` and `just run-baler` recipes (mirroring
      `build-slint`/`run-slint`, stopping the autostart app for fb0 ownership)
- [ ] A `LICENSE` and a stub `CONTEXT.md` so the crate is consistent with peers

## Blocked by

None - can start immediately.

## Comments

_None yet._
