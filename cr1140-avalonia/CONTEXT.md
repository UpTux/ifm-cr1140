# Context: cr1140-avalonia

## Responsibility

A **.NET class library** (`Cr1140.Avalonia`) providing **CR1140 Avalonia LinuxFramebuffer support**: (1) a custom **input backend** for keypad-only embedded panels — thin, typed wrapper over Linux evdev that maps the CR1140/CR1141 gpio-keys device (`/dev/input/event1`) to a managed `KeypadKey` enum and raises `KeyPressed` events for application-driven navigation; and (2) a **`SoftKeyFooter` control** — a 6-key soft-key footer with two layout modes (Physical and Natural) for operator-panel UIs.

**Bounded scope**: CR1140 Avalonia LinuxFramebuffer support (input + soft-key control). This is NOT a full device SDK — it solves two gaps (keypad input and soft-key footer UI) in Avalonia's LinuxFramebuffer platform support. Rendering, layout, MVVM, and application logic are the consuming app's responsibility (see `cr1140-avalonia-demo` for a reference implementation).

Hardware/OS ground truth: [`../docs/device-facts.md`](../docs/device-facts.md).

## API

**Namespace**: `Cr1140.Avalonia.Input`

| Type | Role |
|------|------|
| `enum KeypadKey` | 11-member enum: `F1`, `F2`, `F3`, `F4`, `F5`, `F6`, `Up`, `Down`, `Left`, `Right`, `Enter`. |
| `sealed class EvdevKeypadInput : IInputBackend, IDisposable` | Custom Avalonia input backend. Constructor takes a `devicePath` string (default `/dev/input/event1`). Raises `event Action<KeypadKey>? KeyPressed` on a background reader thread when a key-down event (`EV_KEY`, value `1`) is received. Implements `Initialize(IScreenInfoProvider, Action<RawInputEventArgs>)` (starts the evdev reader thread) and `SetInputRoot(IInputRoot)`. `Dispose()` stops the reader thread. |

**Namespace**: `Cr1140.Avalonia.Controls`

| Type | Role |
|------|------|
| `enum SoftKeyFooterLayout` | Layout mode for `SoftKeyFooter`: `Physical` (keypad-ordered: F6 F4 F2 · d-pad · F1 F3 F5) or `Natural` (F1–F6 left-to-right). |
| `class SoftKeyFooter : Border` | A 6-key soft-key footer control. Properties: `Layout` (SoftKeyFooterLayout, default Physical), `ShowDPad` (bool, default true; centre d-pad in **both** layouts), `F1`–`F6` (string? per-key labels), styling brushes (`DividerBrush`, `KeyForeground`, `LabelForeground`, `DPadForeground`, `DPadBackground`), d-pad text (`DPadLine1`, `DPadLine2`). Derives from `Border`, so `Background`, `BorderBrush`, `BorderThickness`, `Height` style the footer strip. Dark defaults suit an operator panel. |
| `static class SoftKeyLayoutMap` | Single source of truth for the keypad's physical F-key order (`PhysicalOrder` = F6 F4 F2 F1 F3 F5) and the hardware→logical remap `ToLogical(KeypadKey, SoftKeyFooterLayout)` — identity in Physical; physical-position based in Natural (e.g. hardware F6 → logical F1). Arrow/Enter keys pass through. |

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

- **Managed event pattern**: `EvdevKeypadInput` raises a `KeyPressed` event on a background thread. The consuming app subscribes and marshals to the UI thread (`Dispatcher.UIThread.Post`). This is the **app-driven navigation pattern** — the app's view-model or FSM drives screen transitions and state changes in response to keys.
- **No Avalonia key injection**: The library does **not** inject Avalonia `KeyDown` events or manipulate focus. This is an intentional design decision to keep the input backend simple, explicit, and debuggable. Apps that need routed key events can extend `EvdevKeypadInput` to call `inputSink.Input(new RawKeyEventArgs(...))` in the `Initialize` method.
- **Evdev read loop**: The reader thread blocks on synchronous reads of `/dev/input/event*` (no `epoll`, no async I/O). Evdev nodes are character devices and block until an event is available; the synchronous read is simpler and sufficient for a single low-rate input source.
- **Key-down only**: Key-up (`value 0`) and repeat (`value 2`) events are silently ignored. Only key-down (`value 1`) raises `KeyPressed`. This matches the typical operator-panel UX: a button press triggers an action; release and repeat are not navigation events.
- **NuGet package**: `Cr1140.Avalonia` (PackageId), version `0.3.0`. Package README is this file (`PackageReadmeFile`); license is `GPL-3.0-only` (dual-licensed GPL-3.0-only OR commercial, same as the workspace).
- **SoftKeyFooter layout modes**: **Physical** (default) orders cells `F6 F4 F2 · d-pad · F1 F3 F5` to match the CR1140 keypad — each label sits over the button that triggers it, so no key remap is needed. **Natural** (`F1 F2 F3 · d-pad · F4 F5 F6`) is a conventional reading order; because it no longer matches the physical buttons, callers must remap incoming keys with `SoftKeyLayoutMap.ToLogical` so presses line up with the labels. Layout is an **app-facing UI choice**; the keycode map (`EvdevKeypadInput`) stays CR1140-specific.

## Glossary

| Term | Meaning |
|------|---------|
| `IInputBackend` | Avalonia platform interface for input sources. Implements `Initialize`, `SetInputRoot`. The LinuxFramebuffer platform calls `Initialize` with a raw-event sink; the backend is responsible for reading hardware and calling the sink. |
| `RawInputEventArgs` | Avalonia's low-level input event type (base class for `RawKeyEventArgs`, `RawPointerEventArgs`, etc.). The stock backends inject these; `EvdevKeypadInput` does NOT. |
| `EV_KEY` | Linux input subsystem event type for key/button events (type code `1`). |
| `input_event` | Linux kernel struct for evdev events. 24 bytes on aarch64 (`timeval` is 16 bytes on 64-bit; `type`, `code`, `value` are `u16`, `u16`, `i32`). |
| App-driven navigation | Pattern where the app subscribes to a managed event (`KeyPressed`) and drives view-model state transitions, rather than relying on Avalonia's routed `KeyDown` events. Explicit and testable. |

## Caveats

### Keycode map is CR1140-specific

The evdev keycode → `KeypadKey` mapping is **hard-coded for the CR1140/CR1141 gpio-keys layout**. If the device's keypad layout changes (different evdev codes, additional keys, or a different key count), the enum and mapping table must be updated. The library does **not** auto-detect or configure the keycode map at runtime.

### Requires `/dev/input/event1` read access

The process must have **read permission** on the evdev device node. On the CR1140/CR1141, the keypad is `/dev/input/event1`, owned by `root:input`, mode `0660`. Run the app as root or add the user to the `input` group (`usermod -aG input <user>`).

### LinuxFramebuffer or LinuxDrm platform only

This backend is only compatible with Avalonia's **LinuxFramebuffer** (`StartLinuxFbDev`) or **LinuxDrm** (`StartLinuxDrm`) platforms. It does **not** work with X11, Wayland, or desktop platforms — the `IInputBackend` interface is specific to the headless-framebuffer code path.

### No error recovery on device disconnect

If the evdev device node is removed (USB keypad unplugged, driver unloaded), the reader thread will throw an `IOException` and terminate. The exception is **not** caught; the app will see the `KeyPressed` event stream stop. A production implementation might add a reconnection loop or raise a `DeviceDisconnected` event.

### ⚠ Caveat: licensing

This library is **dual-licensed**: **GPL-3.0-only OR Commercial**.

- **Open source**: Free under the [GNU GPL v3.0](https://www.gnu.org/licenses/gpl-3.0.html) if your project is GPL-compatible.
- **Commercial**: For closed-source or proprietary use, contact **UpTux UG (haftungsbeschränkt)** at **<info@uptux.de>** to arrange a commercial license.

See [`../LICENSING.md`](../LICENSING.md) for full details.

- _(Record further decisions in `docs/adr/`.)_
