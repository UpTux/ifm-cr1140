# Context map

This is a Rust cargo workspace targeting the **ifm CR1140 / CR1141** (ecomatDisplay
4.3″, NXP i.MX 8M Nano / aarch64), for applications that run in place of the stock
CODESYS runtime. See [`docs/device-facts.md`](docs/device-facts.md) for hardware/OS
ground truth.

Each crate has its own bounded context. Read the relevant `CONTEXT.md` before
working in that crate; read this map first to find it.

| Context | Path | Responsibility |
|---------|------|----------------|
| HAL         | [`cr1140-hal/CONTEXT.md`](cr1140-hal/CONTEXT.md)               | Thin, typed hardware abstraction over fbdev / evdev / SocketCAN / sysfs |
| SDK         | [`cr1140-sdk/CONTEXT.md`](cr1140-sdk/CONTEXT.md)               | High-level app framework (run loop, telemetry, config, persistence, shutdown) over the HAL |
| Slint integ | [`cr1140-slint/CONTEXT.md`](cr1140-slint/CONTEXT.md)           | Slint platform backend wiring the HAL to linuxfb rendering + evdev events |
| Demo        | [`cr1140-slint-demo/CONTEXT.md`](cr1140-slint-demo/CONTEXT.md) | Reference application built on the SDK + Slint integration (system dashboard) |
| Baler demo  | [`cr1140-baler-demo/CONTEXT.md`](cr1140-baler-demo/CONTEXT.md) | Second reference app: a round-baler operator panel (retain + CAN + multi-screen UI) |

Dependency direction: `demo → slint + sdk → hal`. The HAL knows nothing about the
layers above it.

## Shared decisions

System-wide architectural decisions live in [`docs/adr/`](docs/adr/) (created lazily
as decisions are recorded). Crate-specific decisions, if any, live under that crate's
own `docs/adr/`.
