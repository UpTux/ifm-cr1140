# CR1140 Native Rust HAL — Design

**Date:** 2026-05-29
**Status:** Approved (pre-implementation)

## Goal & shape

A Rust workspace that lets us build native applications for the ifm
**CR1140/CR1141** (ecomatDisplay 4.3″) and run them in place of the stock
CODESYS runtime. The device is standard embedded Linux, so "reverse-engineer a
BSP/HAL" means: discover which kernel ABIs the device exposes, stand up a
matching cross-toolchain, and build a thin, reusable Rust HAL over those ABIs.

The deliverable is a `cr1140-hal` library crate (display, buttons, CAN,
LEDs/system) plus example/tracer-bullet binaries and the tooling to
cross-compile and deploy to the device.

## Confirmed device facts (from the delivery)

Source: `ifm-delivery/delivery_ecomatDisplay43inch_cds_V2.0.0.11/`,
firmware `.swu` `sw-description`, and the `Programming manual CR1140 CR1141`.

- **SoC / arch:** NXP **i.MX 8M Nano** → **aarch64** (Cortex-A53). Rust targets
  `aarch64-unknown-linux-musl` (default) and `aarch64-unknown-linux-gnu`.
- **OS:** Yocto Linux + a CODESYS runtime. Firmware ships as a SWUpdate `.swu`
  (CPIO/SVR4) containing `core-image-ecomatdisplay-ifm-imx8mn-vhip4-pdm3.ext4.gz`.
- **eMMC layout** (`/dev/mmcblk0`): `p1` rootfs (bootable), `p2` second ext4,
  `p3` large ext4 **data partition** → target location for our binary.
- **Boot:** `init=/sbin/ifm-overlay.sh` (overlay init). Kernel args carry
  `ifm_boot_backlight`, `ifm_orientation`, `ifm_boot_status_led` — the same
  display/LED knobs the HAL will use.
- **GPL written offer** present in the delivery → kernel + BSP source obtainable
  from ifm if we need glibc-sysroot interop or driver details.
- **Access:** SSH to `192.168.1.102`, default username/password.

## Design decisions (from brainstorming)

| Decision | Choice |
|----------|--------|
| Runtime model | **Replace CODESYS entirely** — our app owns display, buttons, CAN, LEDs |
| Language | **Rust** |
| HAL scope | Display, Buttons, CAN, LEDs+system (all four) |
| Toolchain | **Both** triples installed; **musl static is the default** deploy path, glibc+device-sysroot is the escape hatch |

## Workspace architecture

Cargo workspace, pure-Rust, minimal deps (`nix` for ioctl/mmap, `socketcan`
for CAN). Backends sit behind traits only where a real device-driven choice
exists (notably `Display`).

```
cr1140-hal/        library crate — the HAL
  src/display/     fbdev backend + drm backend, auto-detect
  src/input/       evdev reader, keycode map
  src/can/         SocketCAN wrapper
  src/sys/         leds, backlight, temp, persistence, shutdown
  src/lib.rs       Hal facade tying it together
examples/          tracer bullets (hello-fb, read-buttons, can-echo, blink-led)
justfile           build + deploy helpers (target select, scp, run-on-device)
docs/              device-facts.md (ground truth), deploy notes
```

Each module is small, single-purpose, and independently testable. For each
unit we can answer: what it does, how it's used, what it depends on.

## Ground-truth phase (prerequisite — before HAL code)

The HAL's exact shape depends on facts confirmed two ways and reconciled into
`docs/device-facts.md`:

- **Offline:** decompress + read the rootfs from the `.swu`
  (`debugfs`/`ext4fuse` on macOS) → kernel version, glibc version,
  DRM-vs-fbdev, `/dev/input` nodes, CAN interfaces, `ifm-overlay.sh` init, and
  **how CODESYS is launched** (systemd unit or init.d).
- **On-device:** run the existing `cr1140-recon.sh` over SSH and capture
  `recon.txt`.

Cross-referencing pins: the display backend, the input event node, CAN channel
count, and the exact service to disable.

## HAL module interfaces (first cut)

- **display** — detect `/dev/fb0` (fbdev: `FBIOGET_VSCREENINFO` + `mmap`) else
  DRM dumb buffer; expose a framebuffer surface (`width/height/stride`, raw
  pixel write, `present()`), plus backlight set/get via sysfs.
- **input** — open `/dev/input/eventN`, decode `input_event`, surface a
  `Button` enum (F1–F6 / arrows / enter) via a keycode map captured once
  physically; non-blocking poll + blocking read.
- **can** — open `can0`/`can1` via SocketCAN, send/recv frames, bitrate context
  (default 250 k). Raw-frame API now; CANopen deferred (YAGNI).
- **sys** — status/keypad RGB LEDs (sysfs), temperature, persistence dir on
  `p3`, graceful shutdown.

## Toolchain & build

Both targets installed; **musl static** default, glibc+device-sysroot as escape
hatch for future C-interop.

```
rustup target add aarch64-unknown-linux-musl aarch64-unknown-linux-gnu
# default: cargo build --release --target aarch64-unknown-linux-musl
```

`.cargo/config.toml` sets linkers; a `justfile` wraps `build → scp → run`
against the device IP.

## Deploy & CODESYS replacement (reversible)

Persist the binary on the writable data partition (`p3`). Disable CODESYS by
masking its service (identified in the ground-truth phase) **reversibly** —
record original state and provide a restore step. Launch our app via a systemd
unit or an `ifm-overlay.sh` hook. Always keep a documented path back to stock
before disabling anything.

## Tracer-bullet milestone order

1. **Toolchain proof:** static aarch64 "hello" binary runs on device over SSH.
2. **Display:** fill the screen a color (`hello-fb`) — proves render + deploy.
3. **Buttons:** print keycodes, build the physical→`Button` map.
4. **CAN:** echo/dump frames on `can0`.
5. **LED/sys:** blink a status LED, read temperature.
6. **Integration:** one demo app using all four + the CODESYS-replacement launch.

## Testing

- Host unit tests for logic that doesn't touch hardware: pixel math, keycode
  mapping, CAN frame encode/decode.
- Hardware-touching layers get thin, mockable interfaces + on-device smoke tests
  driven from the `justfile`.
- Each tracer bullet is the integration test for its subsystem.

## Key risks / unknowns (resolved in ground-truth phase)

- fbdev may be absent (DRM-only) → display is trait-backed for exactly this.
- musl + some crate may need a glibc fallback → both triples kept ready.
- Disabling CODESYS could affect boot/watchdog/recovery → keep reversible, test
  restore first, never burn the only path back to stock.
