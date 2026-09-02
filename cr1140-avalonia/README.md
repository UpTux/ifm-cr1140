# Cr1140.Avalonia

Custom Avalonia LinuxFramebuffer input backend for keypad-only embedded panels.

## What & Why

Avalonia's built-in LinuxFramebuffer input (`LibInput` / `EvDev`) provides **touch and pointer input only** — no keyboard or keypad support. The ifm CR1140/CR1141 ecomatDisplay (4.3", i.MX 8M Nano, 800×480 fbdev) is available as a **keypad-only SKU** (no touchscreen), which means a headless-framebuffer Avalonia UI cannot receive input from the device's gpio-keys keypad using the stock input backend.

**Cr1140.Avalonia** provides a custom `IInputBackend` implementation that directly reads the keypad from `/dev/input/event1` via Linux evdev, maps the raw keycodes to a typed `KeypadKey` enum (F1–F6, arrow keys, Enter), and raises a managed `KeyPressed` event for application-driven navigation. This library has been **verified on real CR1140 hardware** rendering to `/dev/fb0` and receiving physical keypad input.

## Install

```bash
dotnet add package Cr1140.Avalonia
```

**Dependencies** (automatically resolved):
- `Avalonia` 11.3.20
- `Avalonia.LinuxFramebuffer` 11.3.20

## Requirements

- **Framebuffer device**: `/dev/fb0` or another fbdev node (800×480 on the CR1140/CR1141).
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

Only **key-down** events (`EV_KEY`, value `1`) are raised; key-up events are ignored.

## Design Note

`EvdevKeypadInput` raises a **managed `KeyPressed` event** on a background reader thread. Your application code subscribes to this event and drives navigation, view-model state, or an FSM — the **app-driven pattern**. 

The library does **not** currently inject Avalonia `KeyDown` events or manipulate focus — this is an intentional design decision to keep the input backend simple and explicit. If you need Avalonia's routed key-event system, you can extend `EvdevKeypadInput` to call `inputSink.Input(new RawKeyEventArgs(...))` in the `Initialize` method.

## Reference Application

See **[`cr1140-avalonia-demo`](https://github.com/UpTux/ifm-cr1140/tree/main/cr1140-avalonia-demo)** in the repository for a complete reference implementation:
- Menu-driven navigation (Up/Down/Enter)
- Multiple screens (Dashboard, Bale Counter, Knives, Wrapping, Telemetry, Settings)
- Soft-key footer driven by F1–F6
- MVVM with `INotifyPropertyChanged` and compiled XAML bindings
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
