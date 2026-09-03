# Cr1140.Avalonia

Custom Avalonia components for keypad-only embedded panels: evdev input backend, soft-key footer control, fbdev **and** tear-free DRM/KMS output backends with display rotation, status-LED and keypad-backlight control, and a readable system-telemetry API.

## What & Why

Avalonia's built-in LinuxFramebuffer input (`LibInput` / `EvDev`) provides **touch and pointer input only** — no keyboard or keypad support. The ifm CR1140/CR1141 ecomatDisplay (4.3", i.MX 8M Nano, 800×480 fbdev) is available as a **keypad-only SKU** (no touchscreen), which means a headless-framebuffer Avalonia UI cannot receive input from the device's gpio-keys keypad using the stock input backend.

**Cr1140.Avalonia** (v0.6.0) provides a custom `IInputBackend` implementation that directly reads the keypad from `/dev/input/event1` via Linux evdev, maps the raw keycodes to a typed `KeypadKey` enum (F1–F6, arrow keys, Enter), and raises managed events for application-driven navigation: `KeyPressed` and `KeyReleased` for raw down/up, plus the derived gestures `KeyTapped`, `KeyDoubleTapped`, `KeyHeld` (long-press), and `KeyHolding` (press-and-hold auto-repeat). It also includes a **`SoftKeyFooter`** control — a 6-key soft-key footer with two layout modes (Physical and Natural) for operator-panel UIs — **display rotation** (`RotatingFbdevOutput` / `StartLinuxFbDevRotated`) so the panel can be mounted in any of the four orientations, and a framework-agnostic **`SystemTelemetry`** collector for CPU / memory / temperature / uptime / load and **`DeviceInfo`** for OS identity and network state. The input, soft-key, and rotation components have been **verified on real CR1140 hardware** rendering to `/dev/fb0` and receiving physical keypad input.

## Install

```bash
dotnet add package Cr1140.Avalonia
```

**Dependencies** (automatically resolved):
- `Avalonia` 11.3.20
- `Avalonia.LinuxFramebuffer` 11.3.20

## Requirements

- **Framebuffer device**: `/dev/fb0` or another fbdev node (800×480 on the CR1140/CR1141).
- **DRM device** (for the tear-free DRM path): `/dev/dri/card0`. The process must be able to become DRM master (own the display exclusively).
- **Evdev keypad node**: `/dev/input/event1` (or another evdev node; path is configurable).
- **Permissions**: The process must have **read access** to the evdev node. Run as root or add the user to the `input` group.
- **Platform**: The Avalonia app must start via `StartLinuxFbDev(...)` or `StartLinuxDrm(...)` — a display server (X11/Wayland) is not used.

## Usage

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.LinuxFramebuffer;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Cr1140.Avalonia.Input;

namespace MyKeypadApp;

internal static class Program
{
    public static void Main(string[] args)
    {
        // Construct the keypad input backend
        var keypad = new EvdevKeypadInput("/dev/input/event1");

        // Subscribe to key presses and marshal to the UI thread
        keypad.KeyPressed += key =>
        {
            Dispatcher.UIThread.Post(() => HandleKey(key));
        };

        // Start Avalonia on the Linux framebuffer with the custom input backend
        BuildAvaloniaApp()
            .StartLinuxFbDev(args, "/dev/fb0", scaling: 1.0, inputBackend: keypad);
    }

    private static void HandleKey(KeypadKey key)
    {
        // Drive your navigation FSM or view-model from here
        switch (key)
        {
            case KeypadKey.Up:
                // Navigate up
                break;
            case KeypadKey.Down:
                // Navigate down
                break;
            case KeypadKey.Enter:
                // Confirm selection
                break;
            case KeypadKey.F6:
                // Back to menu
                break;
            // ... handle other keys
        }
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseReactiveUI();
}
```

See the [`cr1140-avalonia-demo`](https://github.com/UpTux/ifm-cr1140/tree/main/cr1140-avalonia-demo) reference application for a complete working example with MVVM navigation, screens, and soft-key footer.

## Keycode Mapping

The evdev keycodes from the CR1140/CR1141 gpio-keys device are mapped as follows:

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

`KeyPressed` fires on **key-down** (`EV_KEY`, value `1`) and `KeyReleased` on **key-up** (value `0`); kernel auto-repeat (value `2`) is ignored. See **Key events & gestures** below for the higher-level events built on top of these.

## Key events & gestures

`EvdevKeypadInput` exposes six `event Action<KeypadKey>?` events. All fire on background threads — marshal to the UI thread with `Dispatcher.UIThread.Post`.

| Event | Fires when |
|-------|-----------|
| `KeyPressed` | A key goes down (evdev value `1`). |
| `KeyReleased` | A key comes up (evdev value `0`). |
| `KeyTapped` | A short press-and-release completes with no second tap inside the double-tap window. |
| `KeyDoubleTapped` | Two taps of the same key complete within `DoubleTapWindow`. |
| `KeyHeld` | A key stays down past `HoldThreshold` (fires once — long-press). |
| `KeyHolding` | Repeats every `HoldRepeatInterval` while the key stays down after `KeyHeld` (press-and-hold auto-repeat). |

Gesture timing is configurable via `KeyGestureOptions` (defaults: `HoldThreshold` 500 ms, `HoldRepeatInterval` 150 ms, `DoubleTapWindow` 300 ms):

```csharp
var keypad = new EvdevKeypadInput("/dev/input/event1", new KeyGestureOptions
{
    HoldThreshold = TimeSpan.FromMilliseconds(400),
    HoldRepeatInterval = TimeSpan.FromMilliseconds(120),
    DoubleTapWindow = TimeSpan.FromMilliseconds(250),
});

keypad.KeyTapped       += k => Dispatcher.UIThread.Post(() => OnTap(k));
keypad.KeyDoubleTapped += k => Dispatcher.UIThread.Post(() => OnDoubleTap(k));
keypad.KeyHeld         += k => Dispatcher.UIThread.Post(() => OnHoldStart(k));
keypad.KeyHolding      += k => Dispatcher.UIThread.Post(() => OnHoldRepeat(k)); // e.g. increment a value
keypad.KeyReleased     += k => Dispatcher.UIThread.Post(() => OnRelease(k));
```

The gesture logic lives in `KeyGestureDetector` — a pure, allocation-free, host-testable state machine driven by monotonic timestamps (`Down`/`Up`/`Tick`). Auto-repeat is derived by the library's own timer, so **holding works whether or not the gpio-keys kernel autorepeat is enabled**.


## SoftKeyFooter control

**`SoftKeyFooter`** is a 6-key soft-key footer control that displays labels above the CR1140's physical F1–F6 keys. It supports two layout modes:

- **Physical** (default): Cells are ordered `F6 F4 F2 · d-pad · F1 F3 F5` to match the CR1140 keypad — each label sits directly over the button that triggers it. The d-pad cluster (Enter + arrows) sits in the middle between F2 and F1.
- **Natural**: Cells run left-to-right `F1 F2 F3 · d-pad · F4 F5 F6` — a conventional reading order, with the d-pad kept centred.

### XAML Usage

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:cr="using:Cr1140.Avalonia.Controls"
             x:Class="MyApp.Views.MyView">
  <Grid RowDefinitions="*, Auto">
    <!-- Main content -->
    <TextBlock Grid.Row="0" Text="Dashboard" />

    <!-- Soft-key footer -->
    <cr:SoftKeyFooter Grid.Row="1"
                      Layout="Physical"
                      F1="Start"
                      F2="Stop"
                      F3="Info"
                      F6="Back" />
  </Grid>
</UserControl>
```

Or bind labels from a view model:

```xml
<cr:SoftKeyFooter Layout="{Binding FooterLayout}"
                  F1="{Binding SoftKeys[0].Label}"
                  F2="{Binding SoftKeys[1].Label}"
                  F3="{Binding SoftKeys[2].Label}"
                  F4="{Binding SoftKeys[3].Label}"
                  F5="{Binding SoftKeys[4].Label}"
                  F6="{Binding SoftKeys[5].Label}" />
```

### Natural layout & key remapping

In **Physical** mode each label sits over its real button, so no key remapping is
needed. In **Natural** mode the on-screen order no longer matches the physical
buttons, so remap each incoming key-press with `SoftKeyLayoutMap.ToLogical` before
acting on it — the button in physical position *i* then triggers the logical key
shown there (e.g. hardware `F6` acts as `F1`). Arrow/Enter keys are never remapped.

```csharp
using Cr1140.Avalonia.Controls;

keypad.KeyPressed += hw =>
{
    // identity in Physical; physical-position remap in Natural
    var key = SoftKeyLayoutMap.ToLogical(hw, footerLayout);
    Dispatcher.UIThread.Post(() => Handle(key));
};
```

`SoftKeyLayoutMap.PhysicalOrder` is the single source of truth for the keypad's
left-to-right F-key order; the `SoftKeyFooter` control uses it too.

### Properties

**Namespace**: `Cr1140.Avalonia.Controls`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Layout` | `SoftKeyFooterLayout` | `Physical` | Layout mode: `Physical` (keypad-ordered) or `Natural` (F1–F6 left-to-right) |
| `ShowDPad` | `bool` | `true` | Show the centre d-pad cell (applies to both layouts; set `false` for a plain 6-cell footer) |
| `F1` – `F6` | `string?` | `null` | Per-key label text; empty or `null` renders a blank cell |
| `DividerBrush` | `IBrush` | `#333333` | Brush for cell dividers |
| `KeyForeground` | `IBrush` | `#888888` | Brush for key names (F1, F2, etc.) |
| `LabelForeground` | `IBrush` | `#00AAFF` | Brush for label text |
| `DPadForeground` | `IBrush` | `#6A7B8A` | Brush for d-pad glyph text |
| `DPadBackground` | `IBrush` | `#141414` | Brush for d-pad cell background |
| `DPadLine1` | `string` | `"▲ ▼"` | First line of d-pad cell text |
| `DPadLine2` | `string` | `"◀ OK ▶"` | Second line of d-pad cell text |

Because `SoftKeyFooter` derives from Avalonia's `Border`, you can also set:
- `Background` (default `#1A1A1A`) — footer strip background
- `BorderBrush` (default `#333333`) — footer strip border
- `BorderThickness` (default `0,2,0,0`) — top border line
- `Height` (default `64`) — footer strip height

All styling properties are overridable to match your application's theme. The defaults are dark colors suited for an operator panel UI.

**Verified on real CR1140 hardware** with both Physical and Natural layout modes.

## Telemetry (system metrics)

`SystemTelemetry` is a **plain, framework-agnostic collector** — not a control. Hold
one instance and call `Sample()` on whatever cadence you like (a 1 Hz
`DispatcherTimer` is typical); each call returns a `TelemetrySnapshot`. Every field
is independently optional (`double?` / `MemInfo?`), so a missing `/proc` file or
thermal zone never throws — it just yields `null` for that field. Because it keeps
CPU-sampler state between calls, **reuse the same instance** rather than
constructing one per sample; the first call primes the CPU baseline and reports 0%.

```csharp
using Cr1140.Avalonia.Telemetry;

var telemetry = new SystemTelemetry();      // SoC thermal zone 0 by default

// ...on a 1 Hz timer, on the thread of your choice:
TelemetrySnapshot s = telemetry.Sample();

string cpu = s.CpuPercent is double c ? $"{c:F0} %" : "—";
string mem = s.Memory is MemInfo m ? $"{m.UsedPercent:F0} % of {m.TotalKb / 1024} MB" : "—";
string soc = s.SocTempC is double t ? $"{t:F1} °C" : "—";
string up  = s.UptimeSeconds is double u ? ProcFs.FormatUptime(u) : "—";
```

### Namespace: `Cr1140.Avalonia.Telemetry`

| Type | Role |
|------|------|
| `SystemTelemetry` | Pull-based collector; `Sample()` → `TelemetrySnapshot`. Ctor takes an optional SoC thermal-zone number (`DefaultSocThermalZone` = 0). Holds CPU-sampler state; no threads, no timers. |
| `TelemetrySnapshot` | One point-in-time read: `CpuPercent`, `Memory`, `SocTempC`, `BoardTempC`, `UptimeSeconds`, `Load1` — each independently optional. |
| `MemInfo` | `TotalKb`, `AvailableKb`, and the computed `UsedPercent` (0..100). |
| `CpuSampler` | Standalone CPU-usage sampler (busy % between two `/proc/stat` reads); reusable on its own. |
| `ProcFs` | Pure parsers (`ParseStat`, `ParseMeminfo`, `ParseUptime`, `ParseLoadavg`, `ParseMillidegrees`) plus thin readers and `FormatUptime` — read a single metric yourself, or unit-test the parsers without a filesystem. |
| `DeviceInfo` | Device/OS identity and network state: `Hostname()`, `OsRelease(key)`, `ReadBoardTempC()`, `OperState(iface)`, `IPv4(iface)`. |

All telemetry types are **pure BCL** (no Avalonia dependency) and read from Linux
`/proc` and `/sys`; on a non-Linux host every reader degrades to `null` / `"?"`
rather than throwing, so the same code is safe to reference from cross-platform
tooling. This mirrors the Rust SDK's `cr1140-sdk` `metrics` + `device` modules.

## Status LED & keypad backlight

The CR1140/CR1141 has an **RGB status light** and an **RGB backlight behind the
keypad buttons**, both exposed by the kernel under `/sys/class/leds/`. The
`Cr1140.Avalonia.Leds` namespace mirrors the Rust framework: `LedSysfs` is the thin
sysfs primitive (`cr1140-hal`), and `LedMode`/`LedAnimation`/`LedDriver` are the
animation layer (`cr1140-sdk`). Writes need write access to the `brightness` nodes
(run as root or add a udev rule); off-device every call is a safe no-op — writes
return `false`, reads return `null`.

```csharp
using Cr1140.Avalonia.Leds;

// Status light: three binary channels (max 1). Green = ready, amber = warning.
LedSysfs.SetTyped(Led.StatusRed, 0);
LedSysfs.SetTyped(Led.StatusGreen, 1);
LedSysfs.SetTyped(Led.StatusBlue, 0);

// Keypad button backlight: one RGB color from three PWM channels (0–255).
LedSysfs.SetKbdBacklight(255, 90, 0);       // orange

// Animated keypad backlight — drive Tick() from a timer (e.g. a DispatcherTimer).
var led = new LedDriver();
led.SetColor((0, 128, 255));                 // base color
led.SetMode(LedMode.Pulse);                  // 2 s breathe

// ...on a ~30–60 Hz timer for a smooth pulse (1 Hz is enough for Blink/Solid):
led.Tick();   // samples the curve, writes sysfs only when the value changes
```

### Namespace: `Cr1140.Avalonia.Leds`

| Type | Role |
|------|------|
| `enum Led` | The six LED channels: `StatusRed/Green/Blue` (binary RGB status light, `Max` = 1) and `KbdRed/Green/Blue` (PWM RGB keypad backlight, `Max` = 255). |
| `LedSysfs` | Typed sysfs read/write: `Name(Led)`, `Max(Led)`, `Set`/`Read` (raw name), `SetTyped(Led, value)` (clamps to `Max`), `SetKbdBacklight(r, g, b)`, `ListLeds()`. Writes → `bool`, reads → `uint?`. |
| `enum LedMode` | Animation curve: `Solid`, `Dim` (50%), `Pulse` (2 s breathe), `Blink` (1 Hz), `Flash` (~4 Hz strobe), `Heartbeat` (double-beat). |
| `LedAnimation` | Pure math (host-testable, no hardware): `Name(mode)`, `Level(mode, t)`, `Scale((r,g,b), level)`. |
| `LedDriver` | Holds a base color + `LedMode`; `SetColor`/`SetMode`; `Tick()` writes the keypad backlight only when the computed color changes. New driver is off/`Solid`, no write until the first `Tick`. |

`LedAnimation` is **pure BCL** (host-testable like `CpuSampler`); `LedSysfs`/`LedDriver`
are the sysfs wirings. This mirrors the Rust `cr1140-hal` `sys` LED functions and the
`cr1140-sdk` `led` module. The status light is set per-channel (`SetTyped`); the keypad
backlight is one RGB color (`SetKbdBacklight` / `LedDriver`).

## Display rotation

Mount the panel in any orientation. `RotatingFbdevOutput` (namespace
`Cr1140.Avalonia.Output`) is a LinuxFramebuffer output backend that renders Avalonia at
the **logical (rotated) size** and rotate-blits each frame onto `/dev/fb0`, so layout,
DPI, and hit-testing stay correct for the chosen orientation. Rotating 90°/270° swaps the
surface to portrait (the native 800×480 landscape framebuffer becomes 480×800).

Start with the `StartLinuxFbDevRotated` helper — the rotated counterpart of
`StartLinuxFbDev`:

```csharp
using Avalonia;
using Cr1140.Avalonia.Input;
using Cr1140.Avalonia.Output;

var keypad = new EvdevKeypadInput("/dev/input/event1");

BuildAvaloniaApp().StartLinuxFbDevRotated(
    args,
    DisplayRotation.Clockwise90,   // None / Clockwise90 / Clockwise180 / Clockwise270
    "/dev/fb0",
    scaling: 1.0,
    inputBackend: keypad);
```

`DisplayRotation` is the clockwise angle the rendered image is turned before it reaches the
panel — pick the value that makes the UI upright for how the display is mounted. Or drive a
`RotatingFbdevOutput` yourself and pass it to `StartLinuxDirect`:

```csharp
var output = new RotatingFbdevOutput("/dev/fb0", DisplayRotation.Clockwise270, scaling: 1.0);
BuildAvaloniaApp().StartLinuxDirect(args, output, keypad);
```

The rotation math lives in the pure, dependency-free `FramebufferRotator`
(`Rotate(src, srcStride, dst, dstStride, dstWidth, dstHeight, bytesPerPixel, rotation)`),
which handles 32 bpp (Bgra/Rgba8888) and 16 bpp (Rgb565) and is unit-tested pixel-exact for
all four angles. `RotatingFbdevOutput` uses the framebuffer's current mode (it does not
change the display mode).

> **Note:** rotation transforms the **output** only. Pointer/touch coordinates are not
> rotated, which is fine for the keypad-only SKU (keys carry no screen coordinates); a touch
> SKU using rotation would need a matching coordinate transform in the input path.

The `cr1140-avalonia-demo` reads rotation from `--rotate=90|180|270` or the `CR1140_ROTATE`
environment variable (default: no rotation).

## DRM output (tear-free)

The fbdev backend (`RotatingFbdevOutput`) is **single-buffered** and can tear during
large redraws. `RotatingDrmOutput` is the tear-free alternative: it presents through the
Linux **DRM/KMS** stack (`/dev/dri/card0`) using **double-buffered DUMB buffers** and a
**page-flip**, while still rendering with **software Skia** — no GL required, which matters
because the i.MX 8M Nano has no usable GL driver. It supports the same `DisplayRotation`
values as the fbdev backend and reuses the same pure `FramebufferRotator`.

Start with the `StartLinuxDrmRotated` helper — the DRM counterpart of
`StartLinuxFbDevRotated`:

```csharp
using Avalonia;
using Cr1140.Avalonia.Input;
using Cr1140.Avalonia.Output;

var keypad = new EvdevKeypadInput("/dev/input/event1");

BuildAvaloniaApp().StartLinuxDrmRotated(
    args,
    DisplayRotation.None,          // None / Clockwise90 / Clockwise180 / Clockwise270
    "/dev/dri/card0",              // or null for the default node
    scaling: 1.0,
    inputBackend: keypad);
```

Or drive a `RotatingDrmOutput` yourself and pass it to `StartLinuxDirect`:

```csharp
var output = new RotatingDrmOutput("/dev/dri/card0", DisplayRotation.None, scaling: 1.0);
BuildAvaloniaApp().StartLinuxDirect(args, output, keypad);
```

**Requirements & limitations:**

- The process must be **DRM master** — own the display exclusively (mask
  `app-launcher` / `ifm-local-setup` / CODESYS first, as for the fbdev path).
- Presents at **32 bpp XRGB8888** (`Bgra8888`); there is no 16 bpp path.
- Picks the **first connected connector** and its preferred mode.
- If the driver rejects legacy page-flip, it falls back to a per-frame `SETCRTC`
  (tearing, but working).

The `cr1140-avalonia-demo` uses this DRM path **by default**; opt out with `--fbdev` (or
`CR1140_OUTPUT=fbdev`), and override the node with `--card=…` / `CR1140_CARD`. If DRM init
fails it logs and falls back to the fbdev backend.

## Design Note

`EvdevKeypadInput` raises a **managed `KeyPressed` event** on a background reader thread. Your application code subscribes to this event and drives navigation, view-model state, or an FSM — the **app-driven pattern**. 

The library does **not** currently inject Avalonia `KeyDown` events or manipulate focus — this is an intentional design decision to keep the input backend simple and explicit. If you need Avalonia's routed key-event system, you can extend `EvdevKeypadInput` to call `inputSink.Input(new RawKeyEventArgs(...))` in the `Initialize` method.

## Reference Application

See **[`cr1140-avalonia-demo`](https://github.com/UpTux/ifm-cr1140/tree/main/cr1140-avalonia-demo)** in the repository for a complete reference implementation:
- Menu-driven navigation (Up/Down/Enter)
- Multiple screens (Dashboard, Bale Counter, Knives, Wrapping, Telemetry, Settings)
- Soft-key footer driven by F1–F6
- MVVM with `INotifyPropertyChanged` and compiled XAML bindings
- A Telemetry screen driven by `SystemTelemetry` + `DeviceInfo` (live CPU/memory/temperature/uptime/load and eth0/can0 state, refreshed at 1 Hz)
- Verified running on the physical CR1140 device

## License

**Dual-licensed**: **GPL-3.0-only** OR **Commercial**.

- **Open source**: Free under the [GNU GPL v3.0](https://www.gnu.org/licenses/gpl-3.0.html) if your project is GPL-compatible.
- **Commercial**: For closed-source or proprietary use, contact **UpTux UG (haftungsbeschränkt)** at **<info@uptux.de>** to arrange a commercial license.

See [`LICENSING.md`](https://github.com/UpTux/ifm-cr1140/blob/main/LICENSING.md) in the repository for full details.

---

**Author**: Patrick Dahlke  
**Company**: UpTux UG (haftungsbeschränkt)  
**Repository**: <https://github.com/UpTux/ifm-cr1140>
