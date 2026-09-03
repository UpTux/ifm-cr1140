# Avalonia demo — how to build and deploy

This guide covers the **cr1140-avalonia-demo** (.NET 10 / Avalonia 11.3.20 operator
panel) deployment to the CR1140 device (aarch64 glibc 2.35, fbdev 800×480, keypad).

## Device facts recap

- SoC: **NXP i.MX 8M Nano** (aarch64, Cortex-A53)
- OS: **eDB2 ecomatDisplay 2.0.0.11** (Yocto, systemd, **glibc 2.35**)
- Display: **`/dev/fb0`**, 800×480, 32 bpp xRGB8888 (fbdev). DRM `/dev/dri/card0` also present and used **by default** via the package's tear-free DRM output path (fbdev is the opt-in fallback).
- Input: **`/dev/input/event1`** (ifm-keypad: F1=59, F2=60, F3=61, F4=62, F5=63, F6=64, Up=103, Down=108, Left=105, Right=106, Enter=28)
- Network: default IP (override via `CR1140_HOST` env var, current: `10.10.10.229`), SSH as `root`

## Approach

**Avalonia 11.3.20** with **software Skia** rendering + **custom evdev keypad input**
(`EvdevKeypadInput`). Output goes through the **DRM/KMS backend by default**
(`RotatingDrmOutput`, tear-free double-buffer + page-flip on `/dev/dri/card0`), with the
single-buffered **LinuxFramebuffer** backend (`/dev/fb0`) as an opt-in fallback (`--fbdev`).
The app is cross-published **from macOS** as a self-contained `linux-arm64` binary
(NativeAOT is NOT possible from macOS — needs a Linux builder). It runs in place of CODESYS
and owns the display exclusively.

Stock Avalonia LinuxFramebuffer input (`LibInput`/`EvDev`) handles **touch/pointer
only**. This SKU is **keypad-only** (no touch), so the demo includes a hand-rolled
`IInputBackend` that polls `/dev/input/event1` for 24-byte `input_event` records
(EV_KEY type==1, value 1=down / 0=up) and maps the keycodes to a `KeypadKey` enum.

`InvariantGlobalization=true` drops the libicu dependency; the publish includes the
.NET runtime and is ~90 MB deployed.

## Reusable package (Cr1140.Avalonia)

The keypad input backend and reusable UI controls have been extracted from the demo
into a standalone **NuGet package** (`Cr1140.Avalonia`, version **0.3.0**) under
`cr1140-avalonia/`. It is dual-licensed **GPL-3.0-only** (for open source) or
**commercial** (contact UpTux UG <info@uptux.de>). The package targets .NET 8.0 and
depends on Avalonia 11.3.20 + Avalonia.LinuxFramebuffer 11.3.20 only.

Version 0.3.0 adds the **`SoftKeyFooter`** control (namespace
`Cr1140.Avalonia.Controls`), a 6-key soft-key footer with two layout modes:
**Physical** (default: F6 F4 F2 · d-pad · F1 F3 F5, matching the CR1140 keypad so
each label sits over its physical button) and **Natural** (`F1 F2 F3 · d-pad · F4 F5 F6`
— reading order with the d-pad kept centred). Because Natural no longer matches the
physical buttons, remap incoming keys with `SoftKeyLayoutMap.ToLogical(key, layout)`
(identity in Physical) so presses line up with the labels — the demo does this in
`MainViewModel.OnKeyPressed`. The demo wires the control in `Views/MainView.axaml`,
and the Settings screen's F2 soft-key ('Footer') toggles the mode live; both modes
verified on device.
### API (namespace `Cr1140.Avalonia.Input`)

- **`enum KeypadKey`**: `F1`, `F2`, `F3`, `F4`, `F5`, `F6`, `Up`, `Down`, `Left`,
  `Right`, `Enter`.
- **`sealed class EvdevKeypadInput : IInputBackend, IDisposable`**:
  - Constructors: `EvdevKeypadInput(string devicePath)` (e.g. `"/dev/input/event1"`) and
    `EvdevKeypadInput(string devicePath, KeyGestureOptions?)` for custom gesture timing.
  - Events (all `Action<KeypadKey>?`, raised on background threads): `KeyPressed` (key-down),
    `KeyReleased` (key-up), `KeyTapped` (short press), `KeyDoubleTapped` (two quick taps),
    `KeyHeld` (long-press, one-shot), `KeyHolding` (press-and-hold repeat).
  - Methods: `Initialize(IScreenInfoProvider, Action<RawInputEventArgs>)` (starts evdev
    reader thread + gesture timer), `SetInputRoot(IInputRoot)`, `Dispose()` (stops both).
- **`sealed class KeyGestureDetector`** / **`sealed class KeyGestureOptions`**: the pure,
  host-testable gesture state machine and its timing knobs (`HoldThreshold` 500 ms,
  `HoldRepeatInterval` 150 ms, `DoubleTapWindow` 300 ms).

Reads 24-byte `input_event` records from the evdev node, raises `KeyPressed`/`KeyReleased`
for `EV_KEY` values 1/0 (auto-repeat value 2 ignored; holding is timer-driven), and maps
codes 59..64, 103, 105, 106, 108, 28 to `KeypadKey`.
Verified on CR1140/CR1141 (aarch64 glibc 2.35, gpio-keys keypad). See
[`cr1140-avalonia/README.md`](../cr1140-avalonia/README.md) for the full
`SoftKeyFooter` API (styling properties, XAML usage).

### Display rotation (mount in any orientation)

`Cr1140.Avalonia` v0.6.0 adds display rotation (namespace `Cr1140.Avalonia.Output`) so the
panel can be mounted in any of the four orientations. `RotatingFbdevOutput` is a
LinuxFramebuffer output backend that renders Avalonia at the logical (rotated) size and
rotate-blits each frame onto `/dev/fb0`; `StartLinuxFbDevRotated` is the drop-in rotated
counterpart of `StartLinuxFbDev`:

```csharp
using Cr1140.Avalonia.Output;

BuildAvaloniaApp()
    .StartLinuxFbDevRotated(args, DisplayRotation.Clockwise90, "/dev/fb0", 1.0, keypad);
```

`DisplayRotation` (`None` / `Clockwise90` / `Clockwise180` / `Clockwise270`) is the clockwise
angle the image is turned before it reaches the panel; 90°/270° swap the surface to portrait
(800×480 → 480×800). Rotation transforms the output only — pointer/touch coordinates are not
remapped (irrelevant for the keypad-only SKU). The pure `FramebufferRotator` is unit-tested
pixel-exact for all four angles; on device, `none`/`90`/`270` each produce a distinct frame.
The demo selects rotation via `--rotate=90|180|270` or the `CR1140_ROTATE` env var.

### DRM output (tear-free)

`Cr1140.Avalonia` v0.7.0 adds a DRM/KMS output backend (`RotatingDrmOutput`, namespace
`Cr1140.Avalonia.Output`) as the **tear-free** alternative to the single-buffered fbdev
backend. It presents through the Linux DRM stack (`/dev/dri/card0`) using double-buffered
DUMB buffers and a page-flip, while still rendering with **software Skia** (the i.MX 8M
Nano has no usable GL driver, so Avalonia's GL-based `DrmOutput` is not an option). It
supports the same `DisplayRotation` values as the fbdev backend. `StartLinuxDrmRotated` is
the DRM counterpart of `StartLinuxFbDevRotated`:

```csharp
using Cr1140.Avalonia.Output;

BuildAvaloniaApp()
    .StartLinuxDrmRotated(args, DisplayRotation.None, "/dev/dri/card0", 1.0, keypad);
```

The demo selects the output backend at startup: **DRM by default**, or the fbdev backend via
the `--fbdev` flag or `CR1140_OUTPUT=fbdev` (DRM node override: `--card=…` or `CR1140_CARD`).
If DRM init fails (no device / not master) it logs to stderr and falls back to fbdev. Either
path needs exclusive display ownership (DRM master) — the install script already masks
`app-launcher`/`ifm-local-setup`/CODESYS.

### Usage

Install from NuGet (once published):

```sh
dotnet add package Cr1140.Avalonia
```

Or reference locally (as the demo does):

```xml
<ProjectReference Include="../cr1140-avalonia/Cr1140.Avalonia.csproj" />
```

Wire into Avalonia's LinuxFramebuffer startup:

```csharp
using Cr1140.Avalonia.Input;

var keypad = new EvdevKeypadInput("/dev/input/event1");
keypad.KeyPressed += key => { /* handle key */ };

AppBuilder.Configure<App>()
    .StartLinuxFbDev(args, "/dev/fb0", 1.0, keypad);
```

Marshal `KeyPressed` callbacks to the UI thread with `Dispatcher.UIThread.Post` (the
event fires on the evdev reader thread). See `cr1140-avalonia-demo/Program.cs` and
`ViewModels/MainViewModel.cs` for a full example, or read
[`cr1140-avalonia/README.md`](../cr1140-avalonia/README.md) for API usage and wiring.

### Packing and publishing

The root `justfile` includes a `pack-avalonia` recipe:

```sh
just pack-avalonia
```

This runs `dotnet pack -c Release` and emits `dist/nuget/Cr1140.Avalonia.0.3.0.nupkg` (plus a `.snupkg` symbols package).

To publish to NuGet.org:

```sh
just push-nuget $NUGET_API_KEY
# equivalently:
dotnet nuget push "dist/nuget/*.nupkg" \
    --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
```

Or use the GitHub Actions workflow (`.github/workflows/nuget.yml`) triggered by a
version tag (`avalonia-v*`):

```sh
git tag avalonia-v0.3.0
git push origin avalonia-v0.3.0
```

The workflow builds, packs, and publishes to NuGet.org automatically (requires
`NUGET_API_KEY` secret configured in the repo).

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

The demo now renders through the DRM/KMS backend **by default** (`RotatingDrmOutput`,
double-buffered DUMB + page-flip), which is tear-free — so tearing should not occur out of
the box. If you forced the single-buffered fbdev backend (`--fbdev` / `CR1140_OUTPUT=fbdev`),
large redraws can tear; drop the flag to return to DRM. See the "DRM output (tear-free)"
section above and `cr1140-avalonia/CONTEXT.md` §Glossary (DRM / KMS, DUMB buffer, page-flip).

### Restore to stock

To remove the demo and restore the stock CODESYS launcher:

```sh
just restore-avalonia
```

This unmasks `codesys.service` (leaving it disabled, per stock), `ifm-retain-srv`,
`app-launcher.service`, and `cr1140-app.service`; removes `cr1140-avalonia.service`;
and daemon-reloads. The ifm setup screen returns on next boot.

## Next steps

- **Tear-free rendering**: **done and default** — the demo renders via the DRM/KMS output
  path (`RotatingDrmOutput`, DUMB buffer + page-flip, software Skia); `--fbdev` opts back out.
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
