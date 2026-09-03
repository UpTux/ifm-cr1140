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
| `static class DeviceInfo` | Device/OS identity + network state: `Hostname()`, `OsRelease(key)` / `OsReleaseValue(content, key)`, `ReadBoardTempC()` (hwmon lm75), `OperState(iface)`, `IPv4(iface)`, `CpuModel()`, `CpuCount()`. |

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

**Namespace**: `Cr1140.Avalonia.Leds`

| Type | Role |
|------|------|
| `enum Led` | The six onboard LED channels under `/sys/class/leds/`: the RGB **status** light (`StatusRed`/`StatusGreen`/`StatusBlue` = `*:status`, binary, `max == 1`) and the RGB **keypad button backlight** (`KbdRed`/`KbdGreen`/`KbdBlue` = `*:kbd_backlight`, 8-bit PWM, `max == 255`). Mirrors the Rust `cr1140-hal` `sys::Led`. |
| `static class LedSysfs` | Thin, typed sysfs reader/writer — the .NET counterpart of the Rust `cr1140-hal` `sys` LED functions: `Name(Led)`, `Max(Led)`, `Set(name, value)` / `Read(name)` (raw `/sys/class/leds/<name>/brightness`), `SetTyped(Led, value)` (clamps to `Max`), `SetKbdBacklight(r, g, b)` (the three PWM channels as one RGB color), `ListLeds()`. Writes return `bool` and reads return `uint?` — every call degrades off-device (missing node / no permission) instead of throwing, like `ProcFs`. |
| `enum LedMode` | Keypad-LED animation mode / brightness curve: `Solid`, `Dim` (50%), `Pulse` (2 s breathe), `Blink` (1 Hz), `Flash` (~4 Hz strobe), `Heartbeat` (double-beat, ~1.2 s). Mirrors the Rust `cr1140-sdk` `led::LedMode`. |
| `static class LedAnimation` | Pure animation math (no hardware, no Avalonia — host-testable like `FramebufferRotator`): `Name(LedMode)`, `Level(LedMode, t)` (brightness multiplier `0.0..=1.0` at `t` seconds), `Scale((r,g,b), level)` (scale a color by a level, rounding half away from zero). Mirrors the Rust `led::LedMode::name` / `level` / `scale`. |
| `sealed class LedDriver` | Drives the RGB keypad backlight from a base color + `LedMode`. `SetColor((r,g,b))` / `SetMode(mode)`; call `Tick()` once per frame — it samples the mode's curve, scales the color, and writes the three sysfs channels **only when the value changes** (a steady color costs nothing after the first write). New drivers are off/`Solid`; no write until the first `Tick`. Mirrors the Rust `cr1140-sdk` `led::LedDriver`. |

The `Leds` types are the LED counterpart of the telemetry API — a deliberate mirror of the Rust `cr1140-hal` `sys` LED primitives (`LedSysfs`) and the `cr1140-sdk` `led` animation layer (`LedMode`/`LedAnimation`/`LedDriver`). `LedAnimation` is pure BCL (host-testable); `LedSysfs`/`LedDriver` are the sysfs wirings. Unlike the keycode map, the LED leaf names are CR1140/CR1141-specific.

**Namespace**: `Cr1140.Avalonia.Diagnostics`

| Type | Role |
|------|------|
| `readonly struct FrameSample` | One presented frame: `TotalMs`, `RenderMs`, `PresentMs`, `VSync` (bool). Fed by the output backends to `FrameStatsRecorder`. |
| `readonly struct FrameMetric` | Aggregated timing for one metric kind: `Average`, `Best`, `Worst`, `Last`. |
| `enum FrameMetricKind` | Metric category: `Total`, `Render`, `Present`. |
| `sealed class FrameStats` | Pure host-testable ring buffer (no clock, no lock). Capacity-bounded (default 240 samples = 4 s at 60 FPS). `Fps`, `Mspf` (ms per frame), `FrameCount`, `Metric(kind)` return aggregates. `CopyHistory(...)` dumps the ring for sparklines. |
| `sealed class FrameStatsRecorder` | Thin clock+lock wiring over `FrameStats`. The output backends call `BeginRender()`, `BeginPresent()`, `EndFrame(vsync)`, and `SetPresentInfo(backend, rotation, viewport)` once per presented frame. `Snapshot()` freezes a `FrameStatsSnapshot`. Ctor takes an optional capacity (default 240). |
| `readonly struct FrameStatsSnapshot` | One point-in-time read: `Fps`, `Mspf`, `FrameCount`, `VSync`, and `FrameMetric` for Total/Render/Present. |
| `class PerfOverlay : Control` | Non-interactive diagnostics HUD. Custom-drawn overlay showing FPS, frame-time rows (Render/Present split, GPU row repurposed to Present on software Skia), sparkline graphs, and an optional system-info block. Attached to a `TopLevel`'s `OverlayLayer`. |
| `sealed class PerfOverlayOptions` | Configuration: `ShowSystemInfo`, `Telemetry` (optional `SystemTelemetry`), `Corner` (screen corner), `FontScale`, `NumericUpdateInterval`, `RedrawInterval`, `StartVisible`, `ToggleKeypad` (optional `EvdevKeypadInput`), `ToggleKey`, `ToggleGesture`. |
| `enum PerfOverlayCorner` | Screen corner: `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`. |
| `enum PerfOverlayToggleGesture` | Keypad gesture to toggle visibility: `Tapped`, `DoubleTapped`, `Held`. |
| `static class PerfOverlayExtensions` | `TopLevel.AttachPerfOverlay(FrameStatsRecorder, PerfOverlayOptions?)` — wires the overlay onto the `TopLevel`'s `OverlayLayer`. |

The `Diagnostics` types split pure-core + thin-wiring: `FrameStats` is pure BCL (host-testable like `CpuSampler`); `FrameStatsRecorder` is the clock+lock wiring the backends feed; `PerfOverlay` is the non-interactive HUD control. Timing is sourced at the backend present boundary (render = Skia rasterize, present = rotate-blit + page-flip/vsync wait). The GPU row is repurposed to Present on software Skia (no GPU on i.MX 8M Nano).

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
- **NuGet package**: `Cr1140.Avalonia` (PackageId), version `0.8.0`. Package README is this file (`PackageReadmeFile`); license is `GPL-3.0-only` (dual-licensed GPL-3.0-only OR commercial, same as the workspace).
- **SoftKeyFooter layout modes**: **Physical** (default) orders cells `F6 F4 F2 · d-pad · F1 F3 F5` to match the CR1140 keypad — each label sits over the button that triggers it, so no key remap is needed. **Natural** (`F1 F2 F3 · d-pad · F4 F5 F6`) is a conventional reading order; because it no longer matches the physical buttons, callers must remap incoming keys with `SoftKeyLayoutMap.ToLogical` so presses line up with the labels. Layout is an **app-facing UI choice**; the keycode map (`EvdevKeypadInput`) stays CR1140-specific.
- **Telemetry is data, not a control**: `SystemTelemetry` is a **pull-based, framework-agnostic collector** (no Avalonia dependency, no threads, no timers). The app owns the cadence — sample on a `DispatcherTimer` or whatever loop it likes — and every field is independently optional so a missing `/proc` node degrades to `null` rather than throwing. This is the .NET counterpart of the Rust `cr1140-sdk` `metrics::Telemetry`/`Snapshot` + `device` modules; the parsers in `ProcFs`/`DeviceInfo` are pure and unit-tested against the same fixtures.
- **Display rotation is an output backend, not a visual transform**: `RotatingFbdevOutput` rotates at the framebuffer boundary — Avalonia lays out and Skia renders at the **logical (rotated) size**, then the frame is rotate-blitted onto the panel. This keeps layout, hit-testing, and DPI correct for the orientation (no `RenderTransform`/`LayoutTransform` clipping games) and is the natural place to compensate for how the panel is physically mounted. The rotation math (`FramebufferRotator`) is pure and host-tested pixel-exact for all four angles (32/16 bpp, padded strides, round-trips); on-device, `none`/`90`/`270` each produce a distinct framebuffer. Unlike the keycode map, this is **not** CR1140-specific — it works for any single fbdev.
- **Two output backends, one rotation model, software Skia**: The package ships both `RotatingFbdevOutput` (single-buffered `/dev/fb0`) and `RotatingDrmOutput` (DRM/KMS DUMB double-buffer + page-flip on `/dev/dri/card0`). Both implement `IOutputBackend` + `IFramebufferPlatformSurface`, report the same **logical (rotated)** size, render with **software Skia** into a private back buffer, and reuse the pure `FramebufferRotator` for the rotate-blit — they differ only in the present path. DRM is the **tear-free** upgrade: Skia renders a full frame, it is rotate-blitted into the DUMB buffer **not** being scanned out, then a `DRM_IOCTL_MODE_PAGE_FLIP` presents it and the render thread blocks on the flip-complete event (which also vsync-throttles the loop). GL is deliberately **not** used — the i.MX 8M Nano has no working GL driver, so Avalonia's GL-based `DrmOutput` cannot render here; keeping Skia on the CPU is the whole point. If a driver rejects legacy page-flip, `RotatingDrmOutput` transparently falls back to a per-frame `SETCRTC` (tearing, but working).
- **LEDs mirror the Rust framework, split HAL/SDK**: The `Leds` namespace is a deliberate two-layer mirror. `LedSysfs` (+ the `Led` enum) is the HAL primitive — typed `/sys/class/leds/` reads/writes, one-to-one with `cr1140-hal` `sys` (`set_led`/`read_led`/`set_led_typed`/`set_kbd_backlight`/`list_leds`); it returns `bool`/`uint?` and degrades off-device instead of throwing, like `ProcFs`. `LedMode`/`LedAnimation`/`LedDriver` are the SDK animation layer, one-to-one with `cr1140-sdk` `led`: `LedAnimation` is pure, host-testable curve math (`Level`/`Scale`), and `LedDriver` holds a base color + mode and writes the three keypad-backlight channels **only when the computed value changes** (a steady color is free after the first `Tick`). Like telemetry, the LED types are offered as **primitives, not a control** — the app owns the tick cadence (a `DispatcherTimer`). The status LED is set per-channel via `SetTyped(Led.Status*, …)` (binary); the keypad backlight is one RGB color via `SetKbdBacklight`/`LedDriver`.
- **Performance overlay design (pure-core + thin-wiring)**: Timing sourced at the output-backend present boundary — the backends call `FrameStatsRecorder.BeginRender()` / `BeginPresent()` / `EndFrame(vsync)` once per presented frame. `FrameStats` is the pure host-testable ring buffer (Fps/Mspf/Average/Best/Worst/Last for Total/Render/Present); `FrameStatsRecorder` is the thin clock+lock wiring; `PerfOverlay` is the non-interactive custom-drawn HUD `Control`. Render = Skia rasterize, Present = rotate-blit + page-flip/vsync wait, Total = present cadence, VSync = real DRM page-flip / fbdev `FBIO_WAITFORVSYNC` success. On software Skia (no GPU), the typical 'GPU' row is repurposed to Present. Backend instrumentation is opt-in (null recorder = no overhead). Retained-mode FPS honesty: Avalonia only redraws when something changes, so FPS is meaningful only while the overlay drives redraw (via `RedrawInterval` > 0); otherwise the panel idles and shows whatever the app's own activity produces.

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
| `/sys/class/leds` | The Linux LEDs sysfs class the `Leds` types read/write. Each LED is a leaf with a `brightness` node; the CR1140 exposes `red`/`green`/`blue:status` (binary RGB status light) and `red`/`green`/`blue:kbd_backlight` (PWM RGB keypad button backlight). |
| `LedDriver` / `LedMode` | Keypad-backlight animation driver and its brightness-curve modes (`Solid`/`Dim`/`Pulse`/`Blink`/`Flash`/`Heartbeat`). Mirrors the Rust SDK's `led::LedDriver` / `LedMode`. |

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

### LED writes need `/sys/class/leds` write access (silent no-op off-device)

`LedSysfs`/`LedDriver` write the `brightness` node under `/sys/class/leds/<name>/`. On the CR1140/CR1141 these are owned by `root` (mode `0644`), so the app must run as root or a udev rule must grant its group write access. Off-device (or without permission) every write returns `false` and reads/`ListLeds()` return `null` instead of throwing — safe to reference cross-platform, but a `false` from `LedDriver.Tick()` means the color was **not** applied (and is not cached, so the next `Tick` retries). The LED leaf names (`*:status`, `*:kbd_backlight`) are CR1140/CR1141-specific.

### Performance overlay self-perturbation and requires TopLevel OverlayLayer

The `PerfOverlay` HUD **self-perturbs** the frame time it measures — drawing the overlay itself consumes CPU and elongates the frame. This is inherent to any on-panel diagnostics HUD and is kept light (the overlay is custom-drawn and text-only; no heavy controls). FPS is meaningful **only while the overlay is driving redraw** (via `RedrawInterval` > 0 in `PerfOverlayOptions`); Avalonia is **retained-mode** — the UI only redraws when something changes, so without the overlay's own redraw driver the panel idles and shows whatever FPS the app's own activity produces. `AttachPerfOverlay` requires a `TopLevel` with an `OverlayLayer` (the standard `Window` and embedded `TopLevel` both provide one).

### ⚠ Caveat: licensing

This library is **dual-licensed**: **GPL-3.0-only OR Commercial**.

- **Open source**: Free under the [GNU GPL v3.0](https://www.gnu.org/licenses/gpl-3.0.html) if your project is GPL-compatible.
- **Commercial**: For closed-source or proprietary use, contact **UpTux UG (haftungsbeschränkt)** at **<info@uptux.de>** to arrange a commercial license.

See [`../LICENSING.md`](../LICENSING.md) for full details.

- _(Record further decisions in `docs/adr/`.)_
