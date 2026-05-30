# Context: cr1140-hal

> Stub — fill in as domain terms get resolved (e.g. via `/grill-with-docs`).

## Responsibility

Native Rust **hardware abstraction layer** for the ifm CR1140/CR1141. Thin, typed
wrappers over the Linux kernel interfaces the device exposes — no application policy,
no run loop. The layers above (`cr1140-sdk`, `cr1140-slint`) depend on this; it
depends on nothing in the workspace.

Hardware/OS ground truth: [`../docs/device-facts.md`](../docs/device-facts.md).

## Glossary

| Term | Meaning |
|------|---------|
| `Surface`      | xRGB8888 pixel buffer, stride-aware, with `copy_from` blit |
| `FbDisplay`    | fbdev (`/dev/fb0`) mmap display; format-checked; optional double-buffering (`open_double_buffered` / `present`), `blank` |
| `ButtonReader` | evdev keypad reader; by-name discovery (`open_keypad*`); exposes `AsFd`/`AsRawFd` for `epoll` |
| `Button` / `ButtonEvent` / `InputEvent` | decoded input event types |
| `CanBus`       | SocketCAN wrapper (`open` / `send_std` / `recv`); classic CAN 2.0; Linux-only |
| `Led` / `set_led` / `read_led` | typed LED control over sysfs |
| backlight / temp | `set_backlight` / `read_backlight` / `backlight_max`, `read_temp_c` (sysfs) |
| `sys::Nvmem` | thin typed window onto an nvmem EEPROM (`read_at`/`write_at`/`len`); discovered by stable sysfs path (`SPI_RETAIN_EEPROM`), no integrity policy — the SDK `retain::Store` layers A/B + CRC on top |
| `sys::FactoryEeprom` | read-only accessor for the I²C device-identity EEPROM (`0-0051`); `mac()` is offset-confirmed, other fields are raw/best-effort. Never written |
| `HalError` / `HalResult` | error enum (`Io`/`DeviceNotFound`/`UnsupportedFormat`/`OutOfRange`/`Parse`) and result alias |
| `prelude`      | `cr1140_hal::prelude::*` re-export of common types |

## Conventions / decisions

- Fallible calls return `HalResult` so callers match on the cause.
- Modules map 1:1 to hardware concerns: `display`, `input`, `can`, `sys`.
- The `display` module renders in the panel's **native orientation**. Rotation is
  an install-time boot concern (`fw_setenv ifm_orientation`), not a runtime HAL
  surface — the `mxsfb`/`drmfb` driver won't honor runtime `FBIOPUT_VSCREENINFO`
  changes anyway (same constraint that blocks fbdev double-buffering).
- _(Record further decisions in `docs/adr/`.)_

## Capability scope vs. the CODESYS FB library

> Decision recorded in [`../docs/adr/0001-codesys-fb-capability-scope.md`](../docs/adr/0001-codesys-fb-capability-scope.md).

We replace the stock CODESYS runtime; ifm's CR1140 CODESYS Function Block library
serves as a **capability checklist** (which hardware the device offers), not an API
to replicate. Native apps use idiomatic Rust; the HAL only needs to expose every
*real* capability. Goal: capability completeness, not migration parity. Pure-software
FBs (CANopen / J1939 protocol stacks) are out of scope — an app layers them on the
`can` primitive.

**Out of scope (decided):**

| Capability | Rationale |
|------------|-----------|
| Touchscreen     | Keypad-only SKU (no touch event node in live input recon) |
| CAN-FD          | Controller (`mcp251xfd`) is FD-capable, but classic CAN 2.0 is enough; revisit when a real app needs FD |
| CANopen / J1939 | Software protocol stacks, not a hardware capability |
| RTC             | Wall-clock is an OS/`std` concern, not a HAL module (see SDK `device`) |
| Orientation     | Install-time `fw_setenv ifm_orientation` (see above) |

**Resolved by on-device recon — 2026-05-30** (raw findings in
[`../docs/device-facts.md`](../docs/device-facts.md) "Capability recon"):

| Capability | Outcome | Finding |
|------------|---------|---------|
| Buzzer / beeper      | **Dropped** — absent | `ifm-keypad` has no `EV_SND` bit; no `pwm-beeper`/`gpio-beeper` |
| Ambient-light sensor | **Dropped** — absent | `/sys/bus/iio/devices/` empty; no illuminance channel |
| Hardware watchdog    | **In scope** — present + unclaimed | `imx2-wdt` (`/dev/watchdog0`); systemd `RuntimeWatchdogSec` off. systemd-owned by default; opt-in `/dev/watchdog` HAL primitive for app-owned liveness |
| Retain memory        | **In scope — resolved** ([ADR-0002](../docs/adr/0002-retain-store-on-spi-eeprom.md)) | Own the 32 KB SPI EEPROM (`spi1.0/eeprom`; factory data is elsewhere, on the I²C EEPROMs). HAL `sys::Nvmem` primitive (+ read-only factory accessors); SDK `retain::Store` (A/B + CRC32, `postcard`) on top. Mask `ifm-retain-srv`. |
