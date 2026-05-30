# Context: cr1140-slint-demo

> Stub — fill in as domain terms get resolved (e.g. via `/grill-with-docs`).

## Responsibility

Reference **demo application** for the CR1140/CR1141, built on `cr1140-sdk` and
`cr1140-slint`. The worked example of how the layers fit together — it sits at the
top of the dependency chain and pulls in everything below. Exercises the SDK's
`Telemetry` snapshot, `ShutdownGuard`, and `config::Store` persistence, with a Slint
UI (`ui/app.slint`) rendered via the Slint integration.

## Glossary

| Term | Meaning |
|------|---------|
| `ui/app.slint` | the Slint UI definition, compiled via `build.rs` |
| `src/main.rs`  | wires the SDK conveniences to the Slint `Platform` and drives the UI |

## Conventions / decisions

- Depends on the SDK and Slint integration only — never reaches into the HAL directly.
- _(Record further decisions in `docs/adr/`.)_
