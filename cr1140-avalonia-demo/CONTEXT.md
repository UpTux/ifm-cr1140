# Context: cr1140-avalonia-demo

## Responsibility

A .NET/Avalonia 11.3.20 **reference application** for the CR1140/CR1141, demonstrating
how to build an operator-panel UI in C# that renders directly to the Linux framebuffer
(`/dev/fb0`) via Avalonia's LinuxFramebuffer backend (software Skia) and captures
keypad input via the **`Cr1140.Avalonia` package** (a reusable evdev backend; see
`../cr1140-avalonia/CONTEXT.md`). It mirrors the UX and feature set of the
Rust `cr1140-baler-demo` — menu-driven navigation, bale counter, knives IN/OUT,
wrapping cycle — but sits entirely outside the Cargo workspace (separate .NET toolchain).

Unlike the Slint-based demos (which are GPL-3.0-only due to Slint's licensing), this
demo is built on **Avalonia (MIT-licensed)** and **demonstrates that the device can
host a copyleft-free operator-panel app** when that is a customer requirement. The
demo itself remains GPL-3.0-only per the repo license, but the approach is usable
for proprietary apps.

## Architecture (how the slices fit)

The app is **embedded single-view** (`ISingleViewApplicationLifetime`) with a
`MainView` (`UserControl`) as the root surface. It uses **compiled XAML bindings**
(`x:DataType` on every view + `AvaloniaUseCompiledBindingsByDefault=true`) and
hand-rolled MVVM (`ViewModelBase : INotifyPropertyChanged` with a `SetField` helper)
— no reflection `ViewLocator`, no CommunityToolkit.Mvvm or extra NuGet packages
beyond the minimum for input and font.

| Module | Role |
|--------|------|
| `Cr1140.Avalonia` (package) | Keypad input backend and UI controls: `KeypadKey` enum (11 members: `F1`..`F6`, `Up`, `Down`, `Left`, `Right`, `Enter`), `EvdevKeypadInput : IInputBackend` (polls `/dev/input/event1` for EV_KEY events, maps evdev codes 59..64, 103, 105, 106, 108, 28 to `KeypadKey`, raises `KeyPressed` event), and `SoftKeyFooter : Border` (a 6-key soft-key footer with two layout modes: Physical—default, F6 F4 F2 · d-pad · F1 F3 F5 matching the CR1140 keypad—and Natural—F1..F6 left-to-right). Namespace `Cr1140.Avalonia.Input` (input) and `Cr1140.Avalonia.Controls` (UI). Referenced via `<ProjectReference Include="../cr1140-avalonia/Cr1140.Avalonia.csproj" />`. See `../cr1140-avalonia/CONTEXT.md` for details. Avalonia's built-in LinuxFramebuffer input (`LibInput` / `EvDev`) handles touch/pointer only; this SKU is keypad-only, so the package provides the missing backend. |
| `ViewModels/NavigationController.cs` | Pure FSM over `KeypadKey`: on Menu, Up/Down/Enter navigate items; on sub-screens F6 = Back → Menu; on Telemetry, Up/Down scroll the readout via `TelemetryViewModel.ScrollUp()/ScrollDown()`. Updates `MainViewModel.CurrentContent/Title/SoftKeys` and the screen VMs. |
| `ViewModels/MainViewModel.cs` | Root VM: `string Title`, `object? CurrentContent` (bound by ContentControl + DataTemplates), `SoftKeyFooterLayout FooterLayout` (Physical/Natural toggle), `ToggleFooterLayout()`. Subscribes to `EvdevKeypadInput.KeyPressed`; `OnKeyPressed` remaps the hardware key via `SoftKeyLayoutMap.ToLogical(key, FooterLayout)` (so Natural's on-screen order matches the physical buttons), then dispatches to the `NavigationController` on the UI thread (`Dispatcher.UIThread.Post`). Also subscribes to the full keypad event set (`KeyPressed`, `KeyReleased`, `KeyTapped`, `KeyDoubleTapped`, `KeyHeld`, `KeyHolding`) and forwards each to `NavigationController.KeyEvents.Record(...)` for the Key Events demo screen. |
| `ViewModels/<Screen>ViewModel.cs` | Per-screen VM (Menu, Dashboard, BaleCounter, Knives, Wrapping, Telemetry, Settings, KeyEvents, Leds, Brightness). Each exposes bindable properties and is mapped to its `Views/<Screen>View.axaml` via App-level `DataTemplate`s. |
| `ViewModels/KeyEventsViewModel.cs` | **Key Events demo** VM: live showcase of the `Cr1140.Avalonia` event model. Holds six `KeyEventRow` cards (Pressed, Released, Tapped, Double Tap, Held, Holding) each with accent colour, last key, hit count, and an `IsRecent` highlight, plus a rolling `Log`. `Record(KeyEventKind, KeypadKey)` (called on the UI thread from `MainViewModel`) bumps the matching card and prepends the log; `Reset()` clears on screen entry. `Views/KeyEventsView.axaml` lays the cards out 3×2 (via `UniformGrid`) over the log; `Views/Converters.cs` adds `HexToBrushConverter` + `BoolToRecentThicknessConverter`. |
| `ViewModels/TelemetryViewModel.cs` | Telemetry screen VM. Owns a `Cr1140.Avalonia.Telemetry.SystemTelemetry` collector and a 1 Hz `DispatcherTimer`; each tick calls `Sample()` (+ `DeviceInfo.OperState`/`IPv4`) and refreshes live rows: CAN/eth0 state, SoC + board temp, CPU %, memory, load, uptime. `TelemetryRow.Value` is a notifying property so the `ItemsControl` updates in place. Exposes `ScrollUp()/ScrollDown()` which raise `ScrollRequested`; `TelemetryView` handles it by moving its (height-bounded) `ScrollViewer` — required because the keypad injects no Avalonia key events, so the `ScrollViewer` cannot scroll itself. **No mocked values.** |
| `ViewModels/LedsViewModel.cs` | **LEDs demo** VM: live showcase of the `Cr1140.Avalonia.Leds` API driving real hardware. Cycles the RGB **status light** (`LedSysfs.SetTyped(Led.Status*, …)`, binary channels) with F1, the RGB **keypad button backlight** base colour (`LedDriver.SetColor`) with F2, and the animation mode (`LedDriver.SetMode`, `LedMode` Solid/Dim/Pulse/Blink/Flash/Heartbeat) with F3. A ~30 Hz `DispatcherTimer` calls `LedDriver.Tick()` **only while the screen is open** (`Activate()`/`Deactivate()` from `NavigationController.OpenScreen`; `Deactivate` stops the timer and turns the keypad backlight off — the status light persists). The VM's ctor asserts status=green at app start, so the status light signals "running" before the screen is ever opened. `Views/LedsView.axaml` shows two colour swatches (status \| backlight) via `HexToBrushConverter`, the mode name, and a sysfs-access hint. |
| `ViewModels/BrightnessViewModel.cs` | **Display Brightness demo** VM: live showcase of the `Cr1140.Avalonia.Display` API adjusting the panel's real LCD backlight (`Backlight.SetPercent`/`ReadPercent` over `/sys/class/backlight/backlight`). Up / F2 brighten and Down / F1 dim in 10 % steps (`Increase()`/`Decrease()`); `Activate()` re-reads the live backlight on screen entry so the readout matches hardware. A 10 % **safety floor** keeps the keypad-only panel from being dimmed to an unrecoverable black. `Views/BrightnessView.axaml` shows a big % readout, a `ProgressBar`, the raw `n / 400 counts`, and a hardware-access hint (off-device the writes are no-ops and it previews the value). |
| `Views/MainView.axaml` | Root layout: header (`Title`), body (`ContentControl Content="{Binding CurrentContent}"`), footer (`<cr:SoftKeyFooter>` control from Cr1140.Avalonia package with `Layout="{Binding FooterLayout}"` and `F1`..`F6` bindings; default Physical order = F6 F4 F2 · d-pad · F1 F3 F5 matching the keypad). Settings screen's F2 soft-key ('Footer') calls `ToggleFooterLayout()` to switch Physical/Natural at runtime. |
| `Views/<Screen>View.axaml` | Per-screen view (XAML UserControl) with `x:DataType="vm:<Screen>ViewModel"` and compiled bindings. |
| `App.axaml` | FluentTheme Dark, Inter font (`.WithInterFont()`), and the `<Application.DataTemplates>` map (each screen VM → its View). |
| `Program.cs` | Avalonia startup: picks the output backend, constructs it, and calls `AppBuilder.Configure<App>().StartLinuxDirect(...)` with our `EvdevKeypadInput` and scaling=1. **DRM/KMS (`RotatingDrmOutput`) is the default** — tear-free double-buffer + page-flip on `/dev/dri/card0` (overridable with `--card=` / `CR1140_CARD`); force the single-buffered fbdev backend with `--fbdev` or `CR1140_OUTPUT=fbdev`. If DRM init fails (no device / not DRM master) it logs to stderr and falls back to fbdev so the panel still comes up. Rotation comes from `--rotate=90\|180\|270` or the `CR1140_ROTATE` env var (default: none), so the panel can be mounted in any orientation. Does NOT call `.UsePlatformDetect()` (we choose the platform). |

**Performance overlay**: The demo attaches the diagnostics HUD (from `Cr1140.Avalonia.Diagnostics`) on the main view's `OverlayLayer`, hidden by default, toggled by **F5 double-tap**. A `--perf` CLI arg or `CR1140_PERF=1` environment variable makes it start visible (for verification). The HUD shows real FPS and frame timing (Render/Present split, V-Sync state) sourced from the output backends.

**Render-rate cap**: The demo caps the compositor's render/present rate via `--fps=<n>` or the `CR1140_FPS` env var (default 60; the deployed `cr1140-avalonia.service` sets `Environment=CR1140_FPS=24`), applied as `AppBuilder.With(new LinuxFramebufferPlatformOptions { Fps })` before `StartLinuxDirect`. Software Skia renders every full-frame present on the CPU, so a lower fps trades animation smoothness for CPU headroom **while the panel is actively redrawing** — no effect at idle, since Avalonia is retained-mode. On-device (DRM, 2×A53): steady-idle ~5 % of one core regardless of fps; continuous redraw ~69 % @60 vs ~48 % @24 vs ~31 % @15. See `cr1140-avalonia/CONTEXT.md` §Conventions ("Fixed-FPS render cap").

## Glossary

| Term | Meaning |
|------|---------|
| Software rendering | Skia CPU rasterization; Avalonia's LinuxFramebuffer backend does NOT use OpenGL/EGL. The device has no working GL drivers for the Lima GPU on i.MX 8M Nano. |
| DRM output path | Tear-free rendering via `RotatingDrmOutput` / `StartLinuxDrmRotated` (`Cr1140.Avalonia` v0.7.0): DRM/KMS DUMB double-buffer + page-flip on `/dev/dri/card0`, still software Skia. The fbdev backend's single-buffer writes can tear; DRM is the fix and is the **default** output (opt out with `--fbdev`). Verified on-device (page-flip alternation + tear-free), CPU/RAM on par with fbdev. |
| Compiled bindings | Avalonia XAML bindings resolved at compile-time (`x:DataType`, `{Binding Prop}`), not reflection. Faster and type-safe. |
| `ISingleViewApplicationLifetime` | Embedded app mode (no `Window` chrome, just a `UserControl` that fills the surface). Appropriate for fullscreen panel UIs. |
| Cross-publish from macOS | `dotnet publish -r linux-arm64` produces aarch64 glibc binaries on macOS. NativeAOT is NOT possible (requires a Linux builder). |
| InvariantGlobalization | Build-time switch (`-p:InvariantGlobalization=true` + env `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`) that drops libicu dependency, shrinking the publish footprint. |
| Self-contained publish | `--self-contained true` bundles the .NET runtime; the device doesn't ship dotnet. |

## Conventions / decisions

- **Avalonia 11.3.20, .NET 10**: version is pinned; `<TargetFramework>net10.0</TargetFramework>`.
- **Software Skia only**: no GL (the i.MX 8M Nano has no usable GL driver). Both output backends render with CPU Skia — the tear-free `RotatingDrmOutput` (DRM/KMS DUMB double-buffer + page-flip, **default**) and `RotatingFbdevOutput` (fbdev, opt-in via `--fbdev`). Avalonia's GL-based `DrmOutput` is deliberately unused. On-device A/B (identical build, idle Menu) showed both at ~4% of one core and ~90 MB RSS, so DRM is default for its tear-free output at no measurable cost.
- **Custom evdev input backend**: Avalonia's stock LinuxFramebuffer input is touch/pointer only. This SKU is keypad-only (no touch), so `EvdevKeypadInput` polls `/dev/input/event1` and raises `KeyPressed` events. The 24-byte `input_event` layout is verified on-device (`sizeof(struct input_event)` on aarch64 glibc 2.35).
- **Cross-published from macOS**: `just publish-avalonia` runs `dotnet publish` on macOS, targeting `linux-arm64`. NativeAOT is skipped (requires a Linux builder; standard self-contained publish is sufficient for this demo).
- **Compiled XAML bindings**: every `.axaml` file sets `x:DataType` and uses `{Binding ...}` (not `{ReflectionBinding}`); the csproj sets `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`. No reflection `ViewLocator`.
- **Hand-rolled MVVM**: `ViewModelBase : INotifyPropertyChanged` with `SetField<T>`. No CommunityToolkit.Mvvm (keeps NuGet deps minimal).
- **FluentTheme Dark + Inter font**: App.axaml sets `RequestedThemeVariant="Dark"` and calls `.WithInterFont()` in Program.cs.
- **Deployed to `/home/cds-apps/cr1140-avalonia-demo`**: persists via the p2 overlay (survives reboot but NOT `.swu` reflash).
- **Autostart via systemd**: `cr1140-avalonia.service` runs the app on boot (masks CODESYS + app-launcher + cr1140-app to own `/dev/fb0` exclusively).
- **Watchdog / liveness supervision**: the unit is `Type=notify` with `WatchdogSec=30s`; the app runs `Cr1140.Avalonia.Systemd.SystemdWatchdog` from `App.OnFrameworkInitializationCompleted` — it sends `READY=1` and a UI-thread `WATCHDOG=1` heartbeat, so a wedged UI thread is restarted in place (`StartLimitBurst=5/60s`, **not** CODESYS's `reboot-force`). `install.sh` also arms the imx2-wdt **hardware** watchdog (`RuntimeWatchdogSec=60` drop-in) as a backstop if systemd itself hangs; `restore.sh` removes it.

### ⚠ Caveat: CODESYS watchdog + reboot-force

`codesys.service` has `WatchdogSec=2s` and `StartLimitAction=reboot-force`. Killing
or repeatedly crashing the CODESYS runtime **force-reboots the device**. The install
script (`deploy/install.sh`) disables **and** masks `codesys.service` — never just
`kill` it. See `docs/device-facts.md` §CODESYS launch.

### ⚠ Caveat: licensing

- **Avalonia** is **MIT-licensed** (no copyleft); using it does NOT impose GPL on a
  downstream commercial app. The OSS license for AvaloniaUI and its dependencies is
  permissive (MIT/Apache-2.0).
- **This demo's own code** remains **GPL-3.0-only** per the repo `LICENSE`. A
  commercial operator-panel app built on Avalonia + this approach can be proprietary
  (no GPL obligation from the UI toolkit), unlike Slint (GPL/royalty/commercial license).
- **SPDX identifier for this crate**: `GPL-3.0-only` (repo license).

### ⚠ Caveat: NativeAOT is blocked from macOS

`dotnet publish -r linux-arm64 -p:PublishAot=true` on macOS fails (NativeAOT's
cross-compilation toolchain requires a Linux builder for Linux targets). The
self-contained publish (`--self-contained true`) is sufficient for this demo and
bundles the .NET runtime. A real CI pipeline targeting minimal footprint would run
the NativeAOT build on a Linux aarch64 runner.

- _(Record further decisions in `docs/adr/`.)_
