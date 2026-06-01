# cr1140-hal

Hardware abstraction layer for the ifm CR1140/CR1141 (aarch64, Yocto Linux).

`cr1140-hal` wraps the stock Linux ABIs the device exposes — fbdev (display),
evdev (buttons), SocketCAN (CAN), and sysfs (LEDs/backlight/temperature, plus
the SPI-EEPROM nvmem) — behind small, typed Rust APIs. Most fallible calls
return `HalResult`, so callers match on the `HalError` cause (missing device,
unsupported format, out-of-range value) rather than string-matching an
`std::io::Error`. `cr1140_hal::prelude::*` re-exports the common types.

## Target

Built for the ifm **CR1140 / CR1141** ecomatDisplay (NXP i.MX 8M Nano, aarch64,
Yocto Linux — 800×480 fbdev, glibc 2.35). Linux-only at runtime
(fbdev/evdev/SocketCAN/sysfs); the pure-logic parts (surface blits, input
decode, sysfs parsing) build and test on a macOS/Linux host. Typical target
triple: `aarch64-unknown-linux-musl` (static) or `aarch64-unknown-linux-gnu`.

## Install

```toml
[dependencies]
cr1140-hal = "0.1"
```

## Modules

| Module    | What it does |
|-----------|--------------|
| `display` | `Surface` (xRGB8888, stride-aware, `copy_from` blit) + `FbDisplay` (fbdev mmap, format-checked, `open` / `open_double_buffered` / `surface` / `present` / `blank`) |
| `input`   | `InputEvent` decode, `Button` / `ButtonEvent` mapping, and `ButtonReader` (`open_keypad*` by-name discovery, `AsFd` / `AsRawFd` for `epoll`) |
| `can`     | `CanBus` over SocketCAN (`open` / `send_std` / `recv`) — Linux targets only |
| `sys`     | typed `Led` enum + constants (`BACKLIGHT`, `SOC_THERMAL_ZONE`), `set_led` / `read_led`, `set_backlight` / `read_backlight`, `read_temp_c`, and `Nvmem` for the SPI-EEPROM retain store (`open_retain`, `read_at`) |

## Example

Read a button press from the front-panel keypad (illustrative):

```rust
use cr1140_hal::input::{ButtonReader, ButtonEvent};

fn main() -> cr1140_hal::HalResult<()> {
    let mut keypad = ButtonReader::open_keypad()?; // by-name evdev discovery
    loop {
        if let ButtonEvent::Pressed(button) = keypad.next_button()? {
            println!("pressed {button:?}");
        }
    }
}
```

Or open the framebuffer and present a frame:

```rust
use cr1140_hal::display::FbDisplay;

let mut fb = FbDisplay::open_double_buffered("/dev/fb0")?;
let mut surface = fb.surface();   // xRGB8888, stride-aware
// ... draw into `surface` ...
fb.present()?;                    // flip the back buffer
```

## License

Licensed under **GPL-3.0-only**, or a **commercial license** from UpTux UG for
closed-source use — see
[`LICENSING.md`](https://github.com/UpTux/ifm-cr1140/blob/main/LICENSING.md).

## Repository

<https://github.com/UpTux/ifm-cr1140>
