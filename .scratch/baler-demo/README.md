# Baler demo

A round-baler operator panel: a second reference app for the CR1140/CR1141
exercising the full stack — Slint UI on `/dev/fb0`, keypad, reflash-surviving
`retain::Store`, and CAN command output (real `can0` when present, logged
"mock" frames otherwise).

**Spec / source of truth:** [`PRD.md`](./PRD.md). UI follows the agreed light-theme
mockup (header chrome + live clock, multi-screen menu, soft-key footer).

## Functions

1. Bale counter — session + reflash-surviving lifetime total (`retain::Store`).
2. Knives in/out switch (session-only).
3. Activate wrapping — simulated ~5 s timed cycle.

## Issues

Tracer-bullet vertical slices. 01 is the runnable skeleton everything plugs into;
02 (router) and 03 (CAN seam) unlock the three function screens in parallel.

| # | Title | Status | Depends on |
|---|-------|--------|------------|
| 01 | Crate skeleton + light-theme shell on fb0 | ready-for-agent | — |
| 02 | Screen router (menu + 3 sub-screens) | ready-for-agent | 01 |
| 03 | CAN command seam (send-or-log) + message map | ready-for-agent | 01 |
| 04 | Bale counter + retained lifetime total | ready-for-agent | 02, 03 |
| 05 | Counter stats row (Bales/hr live, Avg Ø + Net mock) | ready-for-agent | 04 |
| 06 | Reset session + reset total (double-confirm) | ready-for-agent | 04 |
| 07 | Knives in/out screen | ready-for-agent | 02, 03 |
| 08 | Wrapping screen (simulated timed cycle) | ready-for-agent | 02, 03 |
| 09 | CONTEXT.md + workspace docs | ready-for-agent | 04, 07, 08 |

CAN IDs/payloads are **placeholders** to be replaced with the real baler DBC/J1939.
Retain writes are low-frequency only (debounced + on-exit), per ADR-0002.
