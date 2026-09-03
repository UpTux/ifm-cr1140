# Context: cr1140-avalonia

## Responsibility

A **.NET class library** (`Cr1140.Avalonia`) providing **CR1140 Avalonia LinuxFramebuffer support**: (1) a custom **input backend** for keypad-only embedded panels — thin, typed wrapper over Linux evdev that maps the CR1140/CR1141 gpio-keys device (`/dev/input/event1`) to a managed `KeypadKey` enum and raises `KeyPressed`/`KeyReleased` plus derived gesture events (`KeyTapped`, `KeyDoubleTapped`, `KeyHeld`, `KeyHolding`) for application-driven navigation; (2) a **`SoftKeyFooter` control** — a 6-key soft-key footer with two layout modes (Physical and Natural) for operator-panel UIs; (3) a **`SystemTelemetry`** collector plus **`DeviceInfo`** — a readable, framework-agnostic system-telemetry API (CPU, memory, SoC/board temperature, uptime, load) and device/network identity read from Linux `/proc` and sysfs, exposed as plain data (**not** a control); and (4) **display rotation** — a `RotatingFbdevOutput` LinuxFramebuffer backend (with the `StartLinuxFbDevRotated` startup helper and the pure `FramebufferRotator`) that rotates the rendered frame 0/90/180/270° so the panel can be mounted in any orientation.

**Bounded scope**: CR1140 Avalonia LinuxFramebuffer support (keypad input + soft-key control + display rotation) plus a small, readable system-telemetry API. This is NOT a full device SDK — it solves the gaps a keypad-only panel hits (keypad input, soft-key footer UI, display orientation, and read-only system telemetry) in Avalonia's LinuxFramebuffer platform support. Rendering, layout, MVVM, and application logic are the consuming app's responsibility (see `cr1140-avalonia-demo` for a reference implementation). The telemetry API is a deliberate mirror of the Rust `cr1140-sdk` `metrics` + `device` modules, so a .NET app gets the same data out of the box.

Hardware/OS ground truth: [`../docs/device-facts.md`](../docs/device-facts.md).

## API

**Namespace**: `Cr1140.Avalonia.Input`

| Type | Role |
|------|------|
| `enum KeypadKey` | 11-member enum: `F1`, `F2`, `F3`, `F4`, `F5`, `F6`, `Up`, `Down`, `Left`, `Right`, `Enter`. |
| `sealed class EvdevKeypadInput : IInputBackend, IDisposable` | Custom Avalonia input backend. Constructors: `EvdevKeypadInput(string devicePath)` (default `/dev/input/event1`) and `EvdevKeypadInput(string devicePath, KeyGestureOptions?)`. Raises, on background threads: `KeyPressed` (key-down, `EV_KEY` value `1`), `KeyReleased` (key-up, value `0`), and the derived gestures `KeyTapped`, `KeyDoubleTapped`, `KeyHeld` (long-press, one-shot), `KeyHolding` (press-and-hold repeat) — all `event Action<KeypadKey>?`. Auto-repeat (value `2`) is ignored; holding is timer-driven. Implements `Initialize(IScreenInfoProvider, Action<RawInputEventArgs>)` (starts the evdev reader thread + a gesture timer) and `SetInputRoot(IInputRoot)`. `Dispose()` stops the reader thread and timer. |
| `sealed class KeyGestureDetector` | Pure, allocation-free gesture state machine (no Avalonia dependency, host-testable). Consumes `Down(key, nowMs)`/`Up(key, nowMs)` plus a periodic `Tick(nowMs)` with caller-supplied monotonic milliseconds; raises `Tapped`, `DoubleTapped`, `Held`, `Holding`. Per-key state in a fixed array; `IsIdle` tells the owner when it can stop ticking. **Not** thread-safe — the owner (`EvdevKeypadInput`) serializes access. |
| `sealed class KeyGestureOptions` | Gesture timing (all `TimeSpan`): `HoldThreshold` (default 500 ms), `HoldRepeatInterval` (default 150 ms), `DoubleTapWindow` (default 300 ms). Passed to `KeyGestureDetector` and the `EvdevKeypadInput` gesture ctor. |

**Namespace**: `Cr1140.Avalonia.Controls`

| Type | Role |
|------|------|
| `enum SoftKeyFooterLayout` | Layout mode for `SoftKeyFooter`: `Physical` (keypad-ordered: F6 F4 F2 · d-pad · F1 F3 F5) or `Natural` (F1–F6 left-to-right). |
| `class SoftKeyFooter : Border` | A 6-key soft-key footer control. Properties: `Layout` (SoftKeyFooterLayout, default Physical), `ShowDPad` (bool, default true; centre d-pad in **both** layouts), `F1`–`F6` (string? per-key labels), styling brushes (`DividerBrush`, `KeyForeground`, `LabelForeground`, `DPadForeground`, `DPadBackground`), d-pad text (`DPadLine1`, `DPadLine2`). Derives from `Border`, so `Background`, `BorderBrush`, `BorderThickness`, `Height` style the footer strip. Dark defaults suit an operator panel. |
| `static class SoftKeyLayoutMap` | Single source of truth for the keypad's physical F-key order (`PhysicalOrder` = F6 F4 F2 F1 F3 F5) and the hardware→logical remap `ToLogical(KeypadKey, SoftKeyFooterLayout)` — identity in Physical; physical-position based in Natural (e.g. hardware F6 → logical F1). Arrow/Enter keys pass through. |

**Namespace**: `Cr1140.Avalonia.Telemetry`

| Type | Role |
|------|------|
| `sealed class SystemTelemetry` | Pull-based telemetry collector. Default ctor reads SoC thermal zone `DefaultSocThermalZone` (=0); `SystemTelemetry(uint zone)` overrides it. `Sample()` returns a `TelemetrySnapshot`. Holds one `CpuSampler` internally — reuse the instance; **no threads, no timers** (the app drives the cadence). |
| `readonly struct TelemetrySnapshot` | One point-in-time read: `CpuPercent`, `Memory` (`MemInfo?`), `SocTempC`, `BoardTempC`, `UptimeSeconds`, `Load1` — each independently optional (`double?`), so a missing `/proc` file or thermal zone degrades that field to `null` without failing the sample. |
| `readonly struct MemInfo` | `TotalKb`, `AvailableKb`, and computed `UsedPercent` (0..100). |
| `sealed class CpuSampler` | Standalone busy-% sampler between two `/proc/stat` reads; `Sample()` reads the file, `Update(idle, total)` is the pure delta (host-testable). First call primes → 0%. |
| `static class ProcFs` | Pure parsers (`ParseStat`, `ParseMeminfo`, `MemUsedPercent`, `ParseUptime`, `ParseLoadavg`, `ParseMillidegrees`, `FormatUptime`) and thin `/proc` + `/sys/class/thermal` readers (`ReadMeminfo`, `ReadUptime`, `ReadLoadavg`, `ReadTempC(zone)`). Read a single metric yourself, or unit-test the parsers without a filesystem. |
| `static class DeviceInfo` | Device/OS identity + network state: `Hostname()`, `OsRelease(key)` / `OsReleaseValue(content, key)`, `ReadBoardTempC()` (hwmon lm75), `OperState(iface)`, `IPv4(iface)`. |

The telemetry types are **pure BCL** (no Avalonia dependency) and offered as **data, not a control** — an app reads them and binds/renders as it likes.

**Namespace**: `Cr1140.Avalonia.Output`

| Type | Role |
|------|------|
| `enum DisplayRotation` | Clockwise rotation applied to every frame before it reaches the framebuffer, so the panel can be mounted in any orientation: `None` (0°), `Clockwise90`, `Clockwise180`, `Clockwise270`. Values are the angle in degrees. 90°/270° swap the logical resolution (native 800×480 landscape → 480×800 portrait). |
| `sealed class RotatingFbdevOutput : IOutputBackend, IFramebufferPlatformSurface, IDisposable` | A rotating Linux-framebuffer output backend. Opens the fbdev node (`fileName` / `$FRAMEBUFFER` / `/dev/fb0`), `mmap`s it, reports a **logical (rotated)** `PixelSize` to Avalonia, has Skia render into a private back buffer at that size, and rotate-blits it onto the framebuffer each frame via `FramebufferRotator` (best-effort `FBIO_WAITFORVSYNC`). Pass to `StartLinuxDirect`. Uses the framebuffer's current mode (32 bpp Bgra/Rgba8888 or 16 bpp Rgb565); does not change the display mode. **Single-buffered — can tear on large redraws; use `RotatingDrmOutput` for tear-free output.** |
| `sealed class RotatingDrmOutput : IOutputBackend, IFramebufferPlatformSurface, IDisposable` | The **DRM/KMS** sibling of `RotatingFbdevOutput` — **tear-free**. Opens the DRM primary node (`card` / `/dev/dri/card0`), becomes DRM master, modesets the first connected connector's preferred mode, allocates **two** XRGB8888 (`Bgra8888`) DUMB scanout buffers, reports a **logical (rotated)** `PixelSize`, has Skia render into a private back buffer, then each frame rotate-blits (via `FramebufferRotator`) into the buffer **not** being scanned out and issues a `DRM_IOCTL_MODE_PAGE_FLIP`, blocking on the flip-complete event (vsync throttle). Falls back to `SETCRTC` if the driver rejects page-flip. Still **software Skia** — the i.MX 8M Nano has no usable GL, so Avalonia's GL-based `DrmOutput` is not an option. Pass to `StartLinuxDirect`. **32 bpp only.** |
| `static class FramebufferRotator` | Pure, allocation-free pixel rotator (`Rotate(src, srcStride, dst, dstStride, dstW, dstH, bytesPerPixel, rotation)`). No Avalonia dependency, so host-testable with plain byte buffers; handles 4 bpp and 2 bpp, moving whole pixels (no format conversion or scaling). |
| `static class RotatingFramebufferPlatformExtensions` | `AppBuilder.StartLinuxFbDevRotated(args, rotation, fbdev?, scaling, inputBackend?)` — the rotated counterpart of the stock `StartLinuxFbDev`; constructs a `RotatingFbdevOutput` and hands it to `StartLinuxDirect`. |
| `static class RotatingDrmPlatformExtensions` | `AppBuilder.StartLinuxDrmRotated(args, rotation, card?, scaling, inputBackend?)` — the DRM/KMS counterpart of `StartLinuxFbDevRotated`; constructs a `RotatingDrmOutput` and hands it to `StartLinuxDirect`. |

The `Output` types are the display counterpart of the input backend: `FramebufferRotator` is pure BCL (host-testable like `CpuSampler`/`KeyGestureDetector`); `RotatingFbdevOutput` (single-buffered fbdev) and `RotatingDrmOutput` (tear-free DRM/KMS double-buffer) are the two output-backend wirings — same rotation model, same software-Skia rendering, different present path.

### Evdev keycode → KeypadKey mapping

Hard-coded for the CR1140/CR1141 gpio-keys layout:

| Evdev Code | KeypadKey |
|-----------|-----------|
| 59        | `F1`      |
| 60        | `F2`      |
| 61        | `F3`      |
| 62        | `F4`      |
| 63        | `F5`      |
| 64        | `F6`      |
| 103       | `Up`      |
| 108       | `Down`    |
| 105       | `Left`    |
| 106       | `Right`   |
| 28        | `Enter`   |

24-byte `input_event` record layout (64-bit aarch64, glibc 2.35) is assumed. Only `EV_KEY` (type `1`) events with value `1` (key-down) are processed; key-up and repeat events are ignored.

## Why this exists

Avalonia's built-in **LinuxFramebuffer input** (`Avalonia.LinuxFramebuffer.Input.LibInputBackend` and `EvDevBackend`) handles **touch and pointer input only** — no keyboard or keypad. The stock backends enumerate `/dev/input/event*` nodes and filter for devices with `INPUT_PROP_POINTER` or `INPUT_PROP_DIRECT`; a gpio-keys keypad (which sets neither) is silently skipped.

The CR1140/CR1141 is available as a **keypad-only SKU** (no touchscreen hardware), so a headless-framebuffer Avalonia app cannot receive input using the stock platform. This library provides the missing piece: a custom `IInputBackend` that directly polls the keypad evdev node and exposes a typed event.

The library targets **Avalonia 11.3.20** and **.NET 8.0** (matching the reference demo's platform).

## Conventions / decisions

- **Managed event pattern**: `EvdevKeypadInput` raises key events on background threads (the evdev reader thread for `KeyPressed`/`KeyReleased`; a gesture timer thread for `KeyTapped`/`KeyDoubleTapped`/`KeyHeld`/`KeyHolding`). The consuming app subscribes and marshals to the UI thread (`Dispatcher.UIThread.Post`). This is the **app-driven navigation pattern** — the app's view-model or FSM drives screen transitions and state changes in response to keys.
- **No Avalonia key injection**: The library does **not** inject Avalonia `KeyDown` events or manipulate focus. This is an intentional design decision to keep the input backend simple, explicit, and debuggable. Apps that need routed key events can extend `EvdevKeypadInput` to call `inputSink.Input(new RawKeyEventArgs(...))` in the `Initialize` method.
- **Evdev read loop**: The reader thread blocks on synchronous reads of `/dev/input/event*` (no `epoll`, no async I/O). Evdev nodes are character devices and block until an event is available; the synchronous read is simpler and sufficient for a single low-rate input source.
- **Down + up + gestures**: Key-down (`value 1`) raises `KeyPressed`; key-up (`value 0`) raises `KeyReleased`. Kernel auto-repeat (`value 2`) is **ignored** — holding is derived by the library's own timer so it works regardless of the gpio-keys autorepeat setting. On top of the raw down/up, a pure `KeyGestureDetector` derives `KeyTapped` (short press, no second tap in the window), `KeyDoubleTapped` (two taps within `DoubleTapWindow`), `KeyHeld` (one-shot when `HoldThreshold` is crossed), and `KeyHolding` (repeats every `HoldRepeatInterval` while held). Gesture logic is a separate, allocation-free, host-testable state machine (mirrors the pure `CpuSampler`/`ProcFs` ethos); `EvdevKeypadInput` owns the lock and the 25 ms tick timer that only runs while a key is active.
- **NuGet package**: `Cr1140.Avalonia` (PackageId), version `0.7.0`. Package README is this file (`PackageReadmeFile`); license is `GPL-3.0-only` (dual-licensed GPL-3.0-only OR commercial, same as the workspace).
- **SoftKeyFooter layout modes**: **Physical** (default) orders cells `F6 F4 F2 · d-pad · F1 F3 F5` to match the CR1140 keypad — each label sits over the button that triggers it, so no key remap is needed. **Natural** (`F1 F2 F3 · d-pad · F4 F5 F6`) is a conventional reading order; because it no longer matches the physical buttons, callers must remap incoming keys with `SoftKeyLayoutMap.ToLogical` so presses line up with the labels. Layout is an **app-facing UI choice**; the keycode map (`EvdevKeypadInput`) stays CR1140-specific.
- **Telemetry is data, not a control**: `SystemTelemetry` is a **pull-based, framework-agnostic collector** (no Avalonia dependency, no threads, no timers). The app owns the cadence — sample on a `DispatcherTimer` or whatever loop it likes — and every field is independently optional so a missing `/proc` node degrades to `null` rather than throwing. This is the .NET counterpart of the Rust `cr1140-sdk` `metrics::Telemetry`/`Snapshot` + `device` modules; the parsers in `ProcFs`/`DeviceInfo` are pure and unit-tested against the same fixtures.
- **Display rotation is an output backend, not a visual transform**: `RotatingFbdevOutput` rotates at the framebuffer boundary — Avalonia lays out and Skia renders at the **logical (rotated) size**, then the frame is rotate-blitted onto the panel. This keeps layout, hit-testing, and DPI correct for the orientation (no `RenderTransform`/`LayoutTransform` clipping games) and is the natural place to compensate for how the panel is physically mounted. The rotation math (`FramebufferRotator`) is pure and host-tested pixel-exact for all four angles (32/16 bpp, padded strides, round-trips); on-device, `none`/`90`/`270` each produce a distinct framebuffer. Unlike the keycode map, this is **not** CR1140-specific — it works for any single fbdev.
- **Two output backends, one rotation model, software Skia**: The package ships both `RotatingFbdevOutput` (single-buffered `/dev/fb0`) and `RotatingDrmOutput` (DRM/KMS DUMB double-buffer + page-flip on `/dev/dri/card0`). Both implement `IOutputBackend` + `IFramebufferPlatformSurface`, report the same **logical (rotated)** size, render with **software Skia** into a private back buffer, and reuse the pure `FramebufferRotator` for the rotate-blit — they differ only in the present path. DRM is the **tear-free** upgrade: Skia renders a full frame, it is rotate-blitted into the DUMB buffer **not** being scanned out, then a `DRM_IOCTL_MODE_PAGE_FLIP` presents it and the render thread blocks on the flip-complete event (which also vsync-throttles the loop). GL is deliberately **not** used — the i.MX 8M Nano has no working GL driver, so Avalonia's GL-based `DrmOutput` cannot render here; keeping Skia on the CPU is the whole point. If a driver rejects legacy page-flip, `RotatingDrmOutput` transparently falls back to a per-frame `SETCRTC` (tearing, but working).

## Glossary

| Term | Meaning |
|------|---------|
| `IInputBackend` | Avalonia platform interface for input sources. Implements `Initialize`, `SetInputRoot`. The LinuxFramebuffer platform calls `Initialize` with a raw-event sink; the backend is responsible for reading hardware and calling the sink. |
| `RawInputEventArgs` | Avalonia's low-level input event type (base class for `RawKeyEventArgs`, `RawPointerEventArgs`, etc.). The stock backends inject these; `EvdevKeypadInput` does NOT. |
| `EV_KEY` | Linux input subsystem event type for key/button events (type code `1`). |
| `input_event` | Linux kernel struct for evdev events. 24 bytes on aarch64 (`timeval` is 16 bytes on 64-bit; `type`, `code`, `value` are `u16`, `u16`, `i32`). |
| App-driven navigation | Pattern where the app subscribes to a managed event (`KeyPressed`) and drives view-model state transitions, rather than relying on Avalonia's routed `KeyDown` events. Explicit and testable. |
| `SystemTelemetry` / `TelemetrySnapshot` | Pull-based telemetry collector and its point-in-time sample (CPU, memory, SoC/board temp, uptime, load). Mirrors the Rust SDK's `metrics::Telemetry` / `Snapshot`. |
| procfs | The Linux `/proc` virtual filesystem the telemetry readers parse (`/proc/stat`, `/proc/meminfo`, `/proc/uptime`, `/proc/loadavg`) alongside `/sys/class/thermal` and `/sys/class/hwmon`. |
| `IOutputBackend` / `IFramebufferPlatformSurface` | Avalonia platform interfaces for a display target. `RotatingFbdevOutput` and `RotatingDrmOutput` implement both: the former reports the surface size/scaling, the latter hands Skia a `LockedFramebuffer` (private back buffer) and presents it on unlock. |
| DRM / KMS | Direct Rendering Manager / Kernel Mode Setting — the Linux display stack (`/dev/dri/card0`). `RotatingDrmOutput` uses its modeset + page-flip ioctls directly (no libdrm) for tear-free present. |
| DUMB buffer | A driver-agnostic, CPU-mappable KMS scanout buffer (`DRM_IOCTL_MODE_CREATE_DUMB` + `MAP_DUMB`). `RotatingDrmOutput` allocates two and flips between them — no GPU/GEM/GBM needed, so it works without a GL driver. |
| Page-flip | `DRM_IOCTL_MODE_PAGE_FLIP` — atomically swaps the scanned-out buffer at vblank. Tear-free, and the flip-complete event (read from the DRM fd) throttles the render loop to the panel's refresh. |

## Caveats

### Keycode map is CR1140-specific

The evdev keycode → `KeypadKey` mapping is **hard-coded for the CR1140/CR1141 gpio-keys layout**. If the device's keypad layout changes (different evdev codes, additional keys, or a different key count), the enum and mapping table must be updated. The library does **not** auto-detect or configure the keycode map at runtime.

### Requires `/dev/input/event1` read access

The process must have **read permission** on the evdev device node. On the CR1140/CR1141, the keypad is `/dev/input/event1`, owned by `root:input`, mode `0660`. Run the app as root or add the user to the `input` group (`usermod -aG input <user>`).

### LinuxFramebuffer or LinuxDrm platform only

This backend is only compatible with Avalonia's **LinuxFramebuffer** (`StartLinuxFbDev`) or **LinuxDrm** (`StartLinuxDrm`) platforms. It does **not** work with X11, Wayland, or desktop platforms — the `IInputBackend` interface is specific to the headless-framebuffer code path.

### Display rotation does not transform pointer/touch input

`RotatingFbdevOutput` / `RotatingDrmOutput` rotate the **rendered output** only. Pointer/touch coordinates from Avalonia's stock LinuxFramebuffer input backends are delivered in **physical** framebuffer space and are **not** rotated to match, so a rotated UI driven by touch would have mismatched coordinates. This is fine for the keypad-only SKU this package targets (`EvdevKeypadInput` keys carry no screen coordinates), but a touch SKU using rotation would need a coordinate transform in the input path. `RenderDirectlyToMappedMemory` is not applicable — a private back buffer is always used because the frame must be rotated (and, for DRM, page-flipped) before it reaches the panel.

### DRM output needs `/dev/dri/card0` + master (exclusive display)

`RotatingDrmOutput` opens the DRM primary node (default `/dev/dri/card0`), becomes **DRM master**, and modesets the panel. Only one process can be DRM master at a time, so — exactly like the fbdev path — the app must own the display exclusively: mask `app-launcher`/`ifm-local-setup`, CODESYS, and any other display client first (see `deploy/install.sh`). It presents at **32 bpp XRGB8888** (`Bgra8888`), matching the CR1140/CR1141 panel; there is no 16 bpp path. It picks the **first connected connector** and its preferred mode — sufficient for the single-panel device, not a general multi-head selector. Off-device (no DRM node) the ctor throws — the demo makes DRM the **default** and catches this to fall back to `RotatingFbdevOutput` (opt out entirely with `--fbdev` / `CR1140_OUTPUT=fbdev`).

### No error recovery on device disconnect

If the evdev device node is removed (USB keypad unplugged, driver unloaded), the reader thread will throw an `IOException` and terminate. The exception is **not** caught; the app will see the `KeyPressed` event stream stop. A production implementation might add a reconnection loop or raise a `DeviceDisconnected` event.

### Telemetry reads Linux `/proc` + sysfs (null off-device)

`SystemTelemetry`, `ProcFs`, and `DeviceInfo` read Linux `/proc`, `/sys/class/thermal`, `/sys/class/hwmon`, and `/sys/class/net`. On a non-Linux host (or when a node is absent) every reader degrades to `null` (`"?"` for the string helpers) instead of throwing — safe to reference from cross-platform tooling, but it means **all fields being `null` is indistinguishable from "not on the device."** The SoC thermal zone and the `hwmon0` board sensor are CR1140-specific; pass a different zone to the `SystemTelemetry(uint)` ctor for other hardware.

### ⚠ Caveat: licensing

This library is **dual-licensed**: **GPL-3.0-only OR Commercial**.

- **Open source**: Free under the [GNU GPL v3.0](https://www.gnu.org/licenses/gpl-3.0.html) if your project is GPL-compatible.
- **Commercial**: For closed-source or proprietary use, contact **UpTux UG (haftungsbeschränkt)** at **<info@uptux.de>** to arrange a commercial license.

See [`../LICENSING.md`](../LICENSING.md) for full details.

- _(Record further decisions in `docs/adr/`.)_
