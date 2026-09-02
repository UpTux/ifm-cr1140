# Avalonia demo — how to build and deploy

This guide covers the **cr1140-avalonia-demo** (.NET 10 / Avalonia 11.3.20 operator
panel) deployment to the CR1140 device (aarch64 glibc 2.35, fbdev 800×480, keypad).

## Device facts recap

- SoC: **NXP i.MX 8M Nano** (aarch64, Cortex-A53)
- OS: **eDB2 ecomatDisplay 2.0.0.11** (Yocto, systemd, **glibc 2.35**)
- Display: **`/dev/fb0`**, 800×480, 32 bpp xRGB8888 (fbdev; DRM `/dev/dri/card0` also present but not used by this demo)
- Input: **`/dev/input/event1`** (ifm-keypad: F1=59, F2=60, F3=61, F4=62, F5=63, F6=64, Up=103, Down=108, Left=105, Right=106, Enter=28)
- Network: default IP (override via `CR1140_HOST` env var, current: `10.10.10.229`), SSH as `root`

## Approach

**Avalonia 11.3.20** with the **LinuxFramebuffer backend** (software Skia) +
**custom evdev keypad input** (`EvdevKeypadInput`). The app is cross-published
**from macOS** as a self-contained `linux-arm64` binary (NativeAOT is NOT possible
from macOS — needs a Linux builder). It runs in place of CODESYS and owns `/dev/fb0`
exclusively.

Stock Avalonia LinuxFramebuffer input (`LibInput`/`EvDev`) handles **touch/pointer
only**. This SKU is **keypad-only** (no touch), so the demo includes a hand-rolled
`IInputBackend` that polls `/dev/input/event1` for 24-byte `input_event` records
(EV_KEY type==1, value 1=down / 0=up) and maps the keycodes to a `KeypadKey` enum.

`InvariantGlobalization=true` drops the libicu dependency; the publish includes the
.NET runtime and is ~90 MB deployed.

## Build and deploy

All recipes live in the **root `justfile`** (not the Avalonia directory — it has no
`Cargo.toml` or Rust build). The device IP defaults to `CR1140_HOST=10.10.10.229`;
override if needed:

```sh
# Cross-publish from macOS to linux-arm64 (self-contained)
just publish-avalonia

# Deploy + autostart (stops CODESYS, app-launcher, and the Rust demo; enables cr1140-avalonia.service)
just deploy-avalonia

# Quick manual run (foreground; stops services but does NOT enable autostart)
just run-avalonia

# Restore stock services (unmask CODESYS/app-launcher, stop cr1140-avalonia)
just restore-avalonia
```

- **`publish-avalonia`**: runs `dotnet publish -c Release -r linux-arm64 --self-contained true -p:InvariantGlobalization=true` and emits to `cr1140-avalonia-demo/publish/linux-arm64/`.
- **`deploy-avalonia`**: publishes, scps the binary + systemd service + install.sh to the device, and runs `install.sh` on-device (disables+masks `codesys.service`, `ifm-retain-srv`, `app-launcher.service`, and `cr1140-app.service`; enables+starts `cr1140-avalonia.service`).
- **`run-avalonia`**: publishes, stops the services (but doesn't mask/enable), scps the binary, and runs it **foreground** for quick manual testing (Ctrl-C to exit).
- **`restore-avalonia`**: scps `restore.sh` to the device and runs it (unmasks stock services, removes `cr1140-avalonia.service`).

## Runtime dependency check (on-device)

The self-contained publish bundles libc, but glibc 2.35 must be present (it is).
Verify `libstdc++` and `libgcc_s` availability (Avalonia's Skia native dep links them):

```sh
ssh root@10.10.10.229 'ldconfig -p | grep -E "libstdc\+\+|libgcc_s"'
```

Expected output:

```
libstdc++.so.6 (libc6,AArch64) => /usr/lib/libstdc++.so.6
libgcc_s.so.1 (libc6,AArch64) => /lib/libgcc_s.so.1
```

If missing, the app would fail at load-time. The stock rootfs ships them.

## First-run notes

1. **Exclusive framebuffer ownership**: The install script masks `app-launcher.service`
   (which runs `ifm-local-setup`, the "setup screen" that also writes `/dev/fb0`).
   Once masked, you will NOT see the ifm launcher until `restore-avalonia` unmasks it.
2. **CODESYS watchdog**: `codesys.service` has `WatchdogSec=2s` +
   `StartLimitAction=reboot-force`. The install script disables **and** masks it to
   avoid force-reboots. Never `systemctl kill codesys` — always disable+mask.
3. **Autostart after reboot**: `cr1140-avalonia.service` is enabled; the app starts
   on boot and restarts on crash (`Restart=on-failure`, `RestartSec=2`).
4. **Co-existence with Rust demo**: The install script also masks `cr1140-app.service`
   (the Rust baler-demo autostart) so the two apps never fight for `/dev/fb0`.

## Troubleshooting

### Blank screen

The app launched but the display stays black. Causes:

- **`app-launcher` or `ifm-local-setup` still running**: check `ps aux | grep ifm-local-setup`
  and `systemctl status app-launcher.service`. If active, the launcher is writing
  over our output. Re-run `deploy/install.sh` to mask it, or kill manually:
  ```sh
  systemctl disable --now app-launcher.service
  systemctl mask app-launcher.service
  pkill ifm-local-setup
  ```
- **App crashed on start**: check `journalctl -u cr1140-avalonia.service -n 50`. Common
  errors: missing `/dev/input/event1`, wrong fbdev path, or missing `libstdc++`.

### No text / garbled font rendering

Avalonia uses **Inter font** (bundled). If text is missing or corrupted:

- Check `journalctl -u cr1140-avalonia.service` for font-loading errors.
- Verify `publish/linux-arm64/` includes the font files (`Avalonia.Fonts.Inter.dll` or
  embedded resources).
- Ensure `Program.cs` calls `.WithInterFont()`.

### No input / keypad doesn't respond

The app is running but F1..F6 / arrow keys do nothing. Causes:

- **Wrong event node**: `EvdevKeypadInput` opens `/dev/input/event1`. Verify the
  keypad is on that node:
  ```sh
  cat /proc/bus/input/devices | grep -A 5 ifm-keypad
  ```
  Expected `Handlers=… event1`. If different, update `deploy/cr1140-avalonia.service`
  `ExecStart=` and the `Program.cs` argv fallback.
- **Evdev read error**: check `journalctl -u cr1140-avalonia.service` for "Failed to
  open /dev/input/event1" or permission denied.

### Tearing / visual artifacts

The fbdev backend is single-buffered and can tear during large redraws. The DRM/KMS
backend (`StartLinuxDrm` + atomic page-flip) is the tear-free upgrade path; it's
documented in Avalonia 11.3+ but not implemented in this demo. See
`cr1140-avalonia-demo/CONTEXT.md` §Glossary "DRM upgrade path".

### Restore to stock

To remove the demo and restore the stock CODESYS launcher:

```sh
just restore-avalonia
```

This unmasks `codesys.service` (leaving it disabled, per stock), `ifm-retain-srv`,
`app-launcher.service`, and `cr1140-app.service`; removes `cr1140-avalonia.service`;
and daemon-reloads. The ifm setup screen returns on next boot.

## Next steps

- **Tear-free rendering**: switch from `StartLinuxFbDev` to `StartLinuxDrm` (DRM/KMS
  DUMB buffer + atomic page-flip). Requires Avalonia 11.3+ DRM backend.
- **NativeAOT**: run `dotnet publish -p:PublishAot=true` on a **Linux aarch64 builder**
  (not macOS) to shrink the deployed footprint (~90 MB → ~30 MB).
- **Touch support (if SKU has touch)**: remove `EvdevKeypadInput` and use Avalonia's
  stock `LinuxFramebuffer` input backend (it handles touch/pointer via LibInput).
- **CAN integration**: wire the knives/wrapping actions to `SocketCAN` (`/dev/can0`),
  mirroring the Rust demo's `can.rs` command map.

## See also

- [`cr1140-avalonia-demo/CONTEXT.md`](../cr1140-avalonia-demo/CONTEXT.md) — architecture and decisions
- [`cr1140-baler-demo/CONTEXT.md`](../cr1140-baler-demo/CONTEXT.md) — the Rust/Slint reference app (same UX)
- [`docs/device-facts.md`](device-facts.md) — hardware/OS ground truth
