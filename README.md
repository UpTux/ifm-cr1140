# cr1140-hal

Native Rust hardware abstraction layer for the **ifm CR1140 / CR1141**
(ecomatDisplay 4.3″), for building applications that run **in place of the
stock CODESYS runtime**.

## The device

- SoC: NXP **i.MX 8M Nano** → **aarch64** (Cortex-A53)
- Kernel **5.19.16**, **glibc 2.35**, Yocto (`eDB2 ecomatDisplay`), systemd
- Display: **fbdev / linuxfb, 800×480** (`/dev/fb0`)
- Buttons: evdev (`/dev/input/eventN`), CAN: SocketCAN, LEDs/backlight/temp: sysfs
- Rootfs is a read-only base (eMMC `p1`) with a **writable overlay on `p2`**, so
  files written under `/` (e.g. `/home/cds-apps`) persist across reboots.

Full ground truth: [`docs/device-facts.md`](docs/device-facts.md).
Design + plan: [`docs/superpowers/specs/`](docs/superpowers/specs/) and
[`docs/superpowers/plans/`](docs/superpowers/plans/).

## What's here

`cr1140-hal` crate. Most fallible calls return `HalResult` (a `HalError` enum —
`Io`/`DeviceNotFound`/`UnsupportedFormat`/`OutOfRange`/`Parse`, so callers match
on the cause), and `cr1140_hal::prelude::*` re-exports the common types.

| Module | What it does |
|--------|--------------|
| `display` | `Surface` (xRGB8888, stride-aware, `copy_from` blit) + `FbDisplay` (fbdev mmap, format-checked, optional `open_double_buffered`/`present`, `blank`) |
| `input`   | `InputEvent` decode + `Button`/`ButtonEvent` mapping + `ButtonReader` (`open_keypad*` by-name discovery, `AsFd`/`AsRawFd` for `epoll`) |
| `can`     | `CanBus` over SocketCAN (open / `send_std` / `recv`) — Linux targets only |
| `sys`     | typed `Led` enum + device constants (`BACKLIGHT`, `SOC_THERMAL_ZONE`), `set_led`/`read_led`, `set_backlight`/`read_backlight`, `read_temp_c`, `backlight_max`, `list_*` |

Examples (tracer bullets): `hello`, `hello-fb`, `read-buttons`, `can-echo`,
`blink-led`, `demo` (all four modules).

## Toolchain setup (one-time, on macOS host)

```sh
brew install zig
cargo install cargo-zigbuild
rustup target add aarch64-unknown-linux-musl aarch64-unknown-linux-gnu
```

Cross-compilation uses `cargo-zigbuild`. Default target is static
**`aarch64-unknown-linux-musl`** (one scp-and-run binary). The glibc escape
hatch (for future C-interop) is pinned to the device's glibc:
`CR1140_TARGET=aarch64-unknown-linux-gnu.2.35`.

## Build, test, deploy

```sh
just test                    # host unit tests (Surface, input decode, sysfs parse)
just build-example hello-fb  # cross-build a static aarch64 example
just verify-example hello-fb # confirm it's a static aarch64 ELF
just run-example hello-fb    # scp to the device + run  (needs device)
just recon                   # run cr1140-recon.sh on the device, save docs/recon.txt
```

Device address/user/target/appdir are overridable via env:
`CR1140_HOST`, `CR1140_USER`, `CR1140_TARGET`, `CR1140_APPDIR`
(defaults: `192.168.1.102`, `root`, musl, `/home/cds-apps`).

## Replace / restore CODESYS

⚠️ `codesys.service` has a 2 s watchdog with `StartLimitAction=reboot-force` —
**never kill it; always disable+mask.** Scripts in `deploy/` do this reversibly:

```sh
# on the device (after filling docs/device-facts.md §Recovery):
sh /tmp/install.sh           # disable+mask codesys, enable cr1140-app
sh /tmp/restore-codesys.sh   # bring stock CODESYS back
```

Always confirm `restore-codesys.sh` works **before** leaving the device running
your app. Ultimate fallback: re-flash the stock `.swu` from the delivery.

## Licensing

| Crate | License | Why |
|-------|---------|-----|
| `cr1140-slint`, `cr1140-slint-demo` | **GPL-3.0-only** | They link [Slint](https://slint.dev), used here under its GNU GPLv3 option; anything that links it must be GPLv3. Full text in each crate's `LICENSE`. |
| `cr1140-hal`, `cr1140-sdk` | unset | UI-agnostic — they do **not** link Slint, so they are not bound by its GPL. Pick a license before distributing them separately. |

Shipping a binary that links Slint under GPLv3 means distributing it under GPLv3
(source available, etc.). For a closed-source product, obtain a Slint
[commercial/royalty-free license](https://slint.dev) instead and relicense the
two crates accordingly.

## Status

Host-side (workspace, toolchain, all module logic, cross-compiled static
binaries) is complete and committed. On-device steps — recon (`docs/recon.txt`),
real keycode capture, CAN bring-up, and the install/restore smoke test — run
once the device at `192.168.1.102` is reachable.
