# cr1140-slint-demo

A dashboard app for the ifm CR1140/CR1141, built on the layered crates:

- [`cr1140-hal`](../cr1140-hal) — framebuffer, evdev keypad, backlight, SoC temp
- [`cr1140-sdk`](../cr1140-sdk) — LED effects (`LedDriver`), system telemetry, device info
- [`cr1140-slint`](../cr1140-slint) — Slint `TargetPixel` + software-rendering `Platform`

Slint's pure-Rust software renderer draws into an `Xrgb8888` buffer that the HAL
blits to `/dev/fb0` (no winit, DRM/KMS, libinput, or fontconfig), so it
cross-compiles to the static `aarch64-unknown-linux-musl` target.

It shows live CPU/memory/temperature/uptime/load, network state (eth0/can0), and
maps the front-panel keypad: Up/Down change the backlight, Enter cycles the
keypad RGB LED colour, and F1–F6 select LED animation modes.

## Build & run

```sh
just build-slint   # cross-build the static aarch64 binary
just run-slint      # deploy to the device + run (stops the autostart app first)
```

Device address/user/target/appdir are overridable via the `CR1140_*` env vars
(see the workspace [README](../README.md)).

## Licensing

⚠️ **This binary is GPL-3.0-only**, and **Slint is licensed separately.**

This demo links **Slint**, which it uses under Slint's **GNU GPLv3** option, so
the resulting binary must be distributed under the GPLv3 (full text in
[`LICENSE`](LICENSE)). It also links `cr1140-hal` and `cr1140-sdk`, which are
dual-licensed (GPLv3 or commercial — see the workspace
[`LICENSING.md`](../LICENSING.md)).

If you want to ship a **closed-source / commercial** product based on this demo,
GPLv3 is not enough — you need **two** separate commercial licenses, from two
different parties:

1. A commercial license for `cr1140-hal` / `cr1140-sdk` from the author
   (Patrick Dahlke, <me@patrickdahlke.com>).
2. A commercial or royalty-free **Slint** license from SixtyFPS GmbH —
   <https://slint.dev>. A commercial license for the CR1140 crates does **not**
   cover Slint.

See the workspace [`LICENSING.md`](../LICENSING.md) for the full picture.
