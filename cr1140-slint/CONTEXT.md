# Context: cr1140-slint

> Stub — fill in as domain terms get resolved (e.g. via `/grill-with-docs`).

## Responsibility

**Slint integration** for the CR1140/CR1141: a framebuffer-compatible `TargetPixel`
plus a minimal software-rendering Slint `Platform`. Bridges the Slint UI toolkit to
`cr1140-hal`'s linuxfb display (800×480, xRGB8888) and evdev input.

## Glossary

| Term | Meaning |
|------|---------|
| `TargetPixel` (`pixel.rs`) | framebuffer-compatible pixel type matching the HAL `Surface` format (xRGB8888) |
| `Platform` (`platform.rs`) | the `slint::platform::Platform` implementation: software-renders into the HAL `FbDisplay` and feeds keypad input |

## Conventions / decisions

- Renders through the HAL `display` module rather than touching `/dev/fb0` directly.
- Input arrives through the HAL `input` module, translated to Slint window events.
- _(Record further decisions in `docs/adr/`.)_
