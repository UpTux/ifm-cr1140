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
| Avalonia support | [`cr1140-avalonia/CONTEXT.md`](cr1140-avalonia/CONTEXT.md) | Avalonia LinuxFramebuffer/DRM support published as the `Cr1140.Avalonia` NuGet package: keypad input backend (evdev → KeypadKey), `SoftKeyFooter` control, two software output backends with display rotation — `RotatingFbdevOutput` (fbdev) and the tear-free `RotatingDrmOutput` (DRM/KMS DUMB double-buffer + page-flip) — mount the panel in any orientation, onboard-LED control (`LedSysfs`/`LedMode`/`LedDriver` over `/sys/class/leds`: RGB status light + RGB keypad-button backlight with animation modes; mirrors the Rust HAL `sys` LEDs + SDK `led`), and a readable `SystemTelemetry`/`DeviceInfo` system-telemetry API (CPU/memory/temp/uptime/load + network state; mirrors the Rust SDK `metrics`/`device`) |
| Avalonia demo | [`cr1140-avalonia-demo/CONTEXT.md`](cr1140-avalonia-demo/CONTEXT.md) | .NET/Avalonia operator-panel reference app: renders to `/dev/fb0` via the Avalonia LinuxFramebuffer backend (software Skia) + the `Cr1140.Avalonia` keypad input package |

Dependency direction: `demo → slint + sdk → hal`. The HAL knows nothing about the
layers above it.

**Note:** `cr1140-avalonia-demo` and `cr1140-avalonia` are NOT part of the Cargo 
workspace (separate .NET toolchain). Dependency direction: `cr1140-avalonia-demo → 
cr1140-avalonia`. The Avalonia demos demonstrate that the device can host a 
copyleft-free operator-panel app (Avalonia is MIT-licensed; the `Cr1140.Avalonia` 
package is dual-licensed GPL-3.0-only / commercial) when that is a customer 
requirement, unlike the Slint-based demos (GPL-3.0-only due to Slint's licensing).
## Shared decisions

System-wide architectural decisions live in [`docs/adr/`](docs/adr/) (created lazily
as decisions are recorded). Crate-specific decisions, if any, live under that crate's
own `docs/adr/`.
