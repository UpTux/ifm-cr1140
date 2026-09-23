# CR1102 — Device Facts

Ground truth for the CR1102 (ecomatDisplay/10"/Touch) Avalonia library support. Facts are tagged `[datasheet ✓]` (confirmed from the ifm datasheet/product page + operating-instructions manual) or `[pending live]` (requires on-device confirmation; never stated as verified).

**Sources:** ifm CR1102 datasheet/product page, operating-instructions manual (manualslib id 3253040).

> **Note:** The CR1102 is supported **only in the Avalonia library** (`Cr1140.Avalonia` / `cr1140-avalonia-demo`). The Rust workspace (`cr1140-hal` / `cr1140-sdk` / Slint demos) remains CR1140/CR1141-only.

## Identity  [datasheet ✓]

- **Product name:** ifm ecomatDisplay/10"/Touch
- **Article label:** `ecomatDisplay/10"/Touch`
- **Model:** CR1102
- **Form factor:** 10" panel-mount touchscreen operator terminal

## Display  [datasheet ✓]

- **Panel:** 10" TFT LCD, **native landscape 1280×800** (16:10 aspect ratio), **PCAP (projected-capacitive) touchscreen**.
- **Backlight:** LED, brightness **< 1000 cd/m²**, adjustable **0–100 % in 1 % steps**.
- **Rotation:** Display rotation **4×90°** (software-configurable; the `Cr1140.Avalonia.Output.RotatingFbdevOutput` and `RotatingDrmOutput` backends implement this as clockwise rotation applied to each frame, so the panel can be mounted in any physical orientation).

**Contrast with CR1140/CR1141:** The CR1140/CR1141 are **4.3"** **800×480** keypad-only (no touchscreen) displays. The CR1102 is **10"** **1280×800** with **PCAP touch** plus a physical **8-key function keypad (F1–F8) + navigation key** (see Operating elements below).

## SoC & OS

- **Platform:** ifm **PDM3 10"** on a Xilinx **Zynq UltraScale+ (ZynqMP)** [datasheet ✓]
- **SoC:** ARM **quad-core Cortex-A53 64-bit 1.2 GHz** + **Mali-400 GPU** [live ✓ 2026-09-21]
- **RAM:** 1 GB [datasheet ✓]
- **Flash:** 8 GB [datasheet ✓]
- **OS:** Embedded Linux **kernel 5.10.127** [live ✓ 2026-09-21]  
  (The datasheet states "Linux 4.14" — this is **incorrect**; the device runs kernel **5.10.127**.)
- **Init:** BusyBox sysvinit (no systemd) [live ✓ 2026-09-21]
- **Display stack:** **Weston (Wayland compositor)** started via `/etc/init.d/weston` → `weston-start` → `weston-launch`/`weston`/`simple-weston-client` [live ✓ 2026-09-21]
- **Runtimes:** CODESYS 3.5 / Qt (under `/opt`) [datasheet ✓]; glibc **2.31** [live ✓ 2026-09-21]
- **Device-tree identity:** model `pdm3_10_001-2`; compatible `ifm,pdm3_10_001-2 ifm,pdm3-zynqmp xlnx,zynqmp` [live ✓ 2026-09-21]

**Contrast with CR1140/CR1141:** The CR1140/CR1141 use the NXP **i.MX 8M Nano** (dual Cortex-A53, **no usable GL**). The CR1102 has a **quad-core Cortex-A53 + Mali-400 GPU** SoC. The Mali-400 is exposed via the **lima** kernel driver + **Mesa DRI** (`/usr/lib/dri/lima_dri.so`), with `libEGL.so.1` + `libGLESv2.so.2` + `libgbm` present [live ✓ 2026-09-21] → the platform is GLES2-capable. Whether Avalonia can use the GPU for hardware-accelerated rendering is **[pending live]** (the library currently uses **software Skia** on all three devices, proven working on the CR1102).

## Operating elements  [datasheet ✓]

- **Function keys:** 8 freely-programmable **RGB backlit** function keys (F1–F8).
- **Navigation key:** 1 **RGB backlit** navigation key with 4-way directional input (Up/Down/Left/Right) + pushbutton (Enter).
- **Status LED:** RGB status LED.

**Input layout:** The CR1102 has **both touch and a physical keypad** (8 function keys + 4-way nav + Enter), unlike the CR1140/CR1141 keypad-only SKU. Touch input is delivered by Avalonia's stock `LinuxFramebuffer` pointer input (no custom backend needed); the custom `Cr1140.Avalonia.Input.EvdevKeypadInput` is only required for the **physical function/nav keys**.

## Input

### Touch  [live ✓ 2026-09-21]

- **Touch device:** **Atmel maXTouch Touchscreen** on `/dev/input/event0` (PROP direct-touch, i2c). Confirmed on-device 2026-09-21.
- **Avalonia integration:** Handled by Avalonia's stock `EvDevBackend` (touchscreen input backend), composed with the physical keypad via the `Cr1140.Avalonia.Input.CompositeInputBackend`. The library does **not** provide a custom touch backend (Avalonia's stock backend auto-detected the Atmel maXTouch device). Touch coordinate accuracy and extended touch UX under load remain **[pending live]**.

### Physical keypad  [live ✓ 2026-09-21]

- **Keypad device:** Keys are injected by the **`ifm_service_keyboard` daemon** via **uinput devices** named **"PDM3 virtual keyboard"** (there are 4 identical nodes, event1–event4, on the CR1102). The library's `EvdevKeypadInput.DiscoverByName("PDM3 virtual keyboard")` auto-discovers all matching nodes and reads them thread-safely.
- **D-Bus interface:** The keyboard daemon exposes `com.ifm.Keyboard` on the system bus. Methods include `GetMapping(kbd_id: uint)` (HW key-id→evdev-keycode map), `GetHardwareConfig(kbd) → au` (the evdev *input* key IDs: `0–7, 11–15` — **NOT** the LED IDs; see Keypad-button backlights below), `SetLedColor(kbd, au IDs, u rgb)` / `SetKeyboardLedColors(kbd, au rgbValues)` (set the key backlights; `rgb` packed `0x00RRGGBB`), and `ResetLeds()`. Keyboard 0 is the keypad. The library's `Cr1140.Avalonia.Leds.IfmKeyboardLeds` wraps the LED methods (via `gdbus`) to drive the key backlights (function keys + D-pad, **LED IDs 0–11**) `[live ✓ 2026-09-21]`.
- **Evdev keycodes:** **CONFIRMED [live ✓ 2026-09-21]** via `com.ifm.Keyboard.GetMapping(0)`:
  - F1=59, F2=60, F3=61, F4=62, F5=63, F6=64, F7=65, F8=66
  - Up=103, Down=108, Left=105, Right=106, Enter=28
  
  This is the **same standard ifm gpio-keys layout** as the CR1140/CR1141. The library's `EvdevKeypadInput` keycode map is correct for all three devices.

- **HW key-id→keycode mapping (for reference):** HW key IDs 0–7 map to F4,F5,F3,F6,F2,F7,F1,F8 respectively (the internal MCU wiring). The **physical silk-screen order** in native **landscape** is a single vertical column on the right bezel — **top→bottom: F1 F2 F3 F4 · d-pad · F5 F6 F7 F8** (sequential) `[live ✓ 2026-09-21]`. This is the order `SoftKeyLayoutMap.PhysicalOrderFor(8)` renders.

**Status:** The evdev keycode map, device-discovery mechanism, and touch device identity are **[live ✓ 2026-09-21]**. The library's `DeviceProfile.KeypadDeviceName` for the CR1102 is set to `"PDM3 virtual keyboard"`; `DeviceProfile.TouchDeviceName` is `"Atmel maXTouch Touchscreen"`; `DeviceProfile.TouchDevicePath` is `"/dev/input/event0"`.

## Backlight & LEDs  [live ✓ 2026-09-21]

- **Backlight sysfs node:** `/sys/class/backlight/a00e0400.panel` (type `raw`, `max_brightness` **255**). Confirmed on-device 2026-09-21: write to `brightness` succeeded, value read back, restored. The `Cr1140.Avalonia.Display.Backlight` API reads `max_brightness` at runtime to scale percentage values (0–100 %), so the `DeviceProfile.BacklightNode` is `"a00e0400.panel"` and `BacklightMaxHint` is `255`.

- **RGB LEDs (primary + secondary):** **Two PWM RGB LEDs** under `/sys/class/leds/`:
  - **Primary LED:** `a0080000.rgbled:red:pri`, `a0080000.rgbled:green:pri`, `a0080000.rgbled:blue:pri` (each max `255`)
  - **Secondary LED:** `a0080000.rgbled:red:sec`, `a0080000.rgbled:green:sec`, `a0080000.rgbled:blue:sec` (each max `255`)
  
  Confirmed on-device 2026-09-21: set primary blue LED, read back, restored. The `Cr1140.Avalonia.Devices.RgbLed` record and `LedRole` enum model these; the `DeviceProfile.Leds` property is an `IReadOnlyList<RgbLed>` with two entries (Primary, Secondary).

- **Keypad-button backlights (function keys + D-pad):** The **physical function-key and navigation-key (D-pad) backlights** are **NOT exposed via sysfs** — they are owned by the **keyboard MCU** and driven over the `com.ifm.Keyboard` D-Bus interface (`SetLedColor(0, [ledIds], 0x00RRGGBB)` / `ResetLeds`). The MCU exposes **12 backlight LEDs addressed by LED ID: F1–F8 = IDs 0–7, the D-pad (nav) cluster = IDs 8–11** — the valid LED-ID range is `[0,11]` (any higher ID aborts the entire call). **⚠ These LED IDs are distinct from the evdev input key IDs:** `GetHardwareConfig(0)` / `GetMapping(0)` report the nav *input* keys as **11–15** (Up/Down/Left/Right/Enter), but the nav *LEDs* are **8–11**. Confirmed on-device 2026-09-21: `SetLedColor(0, [0,1,2,3,4,5,6,7], red)` lit the function keys and `SetLedColor(0, [8,9,10,11], green)` lit the D-pad (both verified visually); passing the input key IDs 12–15 fails with `Invalid key ID passed: 12.  Valid range is [0,11]`, which is why an earlier revision — reusing the input key IDs `0–7,11–15` for LEDs — left the D-pad dark (the invalid `12–15` aborted the call). The library's **`Cr1140.Avalonia.Leds.IfmKeyboardLeds`** drives LED IDs `0–11` (fire-and-forget over `gdbus`, **solid** colors, no-op off-device); the sysfs `LedSysfs` API does **not** (they are not sysfs LEDs). The CR1102's `DeviceProfile.HasKeypadBacklight` is **false** (no *sysfs* keypad backlight), but `DeviceProfile.KeypadBacklightViaDbus` is **true** `[live ✓ 2026-09-21]`.

- **There are NO `*:status` or `*:kbd_backlight` sysfs LED nodes** on the CR1102 (unlike the CR1140/CR1141, which have `/sys/class/leds/*:status` binary LEDs and `*:kbd_backlight` PWM LEDs). The CR1102 has **only** the two `pri`/`sec` RGB LEDs in sysfs.

**Contrast with CR1140/CR1141:** The CR1140/CR1141 expose `*:status` (binary, max 1) and `*:kbd_backlight` (PWM, max 255) sysfs LEDs. The CR1102 has **two PWM RGB LEDs (`pri`/`sec`)** with no `*:status` or `*:kbd_backlight` nodes. The backlight node name is also **different** (`a00e0400.panel` vs. `backlight`), and the max is **255** (vs. 400 on CR1140/CR1141).

## Temperature (SoC core + mainboard)  [live ✓ 2026-09-21]

- **SoC & board temperature (D-Bus):** `/sys/class/thermal/` and `/sys/class/hwmon/` are both **empty** on the CR1102 (no thermal-zone or hwmon sensors), but **both temperatures are available** over the **`com.ifm.Io.Temperature`** D-Bus interface (the `ifm_service_io` MCU) — the same source the CODESYS `ifmDevice_ecomatDisplay` library exposes as `rCore0`/`rBoard`: `GetTemperatureCore0` (processor core) and `GetTemperatureBoard` (mainboard), both signed integers in whole °C. Confirmed on-device 2026-09-21: Core0 ≈ 42 °C, Board ≈ 39 °C. `DeviceProfile.TemperatureViaDbus` is **true** for the CR1102 and the library reads them via `Cr1140.Avalonia.Telemetry.IfmSystemTemperatures` (fire-and-forget `gdbus`, null off-device) `[live ✓ 2026-09-21]`. **Fallback:** the ZynqMP also exposes an on-chip **System Monitor** via IIO (`/sys/bus/iio/devices/iio:device0`, `xilinx-system-monitor`, `in_temp0_{raw,offset,scale}` → °C = `(raw+offset)×scale/1000`, ~43 °C), which `SystemTelemetry` uses for the SoC temp **only if** the D-Bus source is unavailable (`ProcFs.ReadXilinxSysmonTempC()`); `DeviceProfile.SocThermalZone` is **null**.

**Contrast with CR1140/CR1141:** The CR1140/CR1141 expose `/sys/class/thermal/thermal_zone0` (SoC) and an `lm75` hwmon sensor (board temp). The CR1102 exposes **neither** — both its core (`rCore0`) and mainboard (`rBoard`) temperatures come from the ifm IO MCU over **`com.ifm.Io.Temperature`** D-Bus instead (with the ZynqMP **IIO sysmon** as a SoC-temp fallback).

## Display output  [live ✓ 2026-09-21]

### Framebuffer  [live ✓ 2026-09-21]

- **Framebuffer device:** `/dev/fb0`, driver `ifm-dcdrmfb`, mode **1280×800**, 32 bpp, stride 5120, **double-buffered** (virtual_size 1280,1600). Auto-detected by the fbdev backend.
- **Framebuffer backend (`RotatingFbdevOutput`)**: The library's fbdev backend opens `/dev/fb0`, reads the current mode (resolution/stride/bpp), and renders at that size. It does **not** change the display mode. The CR1102's **1280×800** native resolution is auto-detected at runtime. Confirmed on-device 2026-09-21: the demo app rendered crisply at 1280×800 via fbdev (framebuffer captured).

### DRM/KMS  [live ✓ 2026-09-21]

- **DRM cards:** **Two DRM cards** on the CR1102:
  - **`/dev/dri/card0`** — driver **`lima`** (Mali-400 GPU, **render-only**, no connectors; `DRM_IOCTL_MODE_GETRESOURCES` returns errno 95). Render node `/dev/dri/renderD128`.
  - **`/dev/dri/card1`** — driver **`ifm_dc`** (KMS display controller). Connector `card1-Unknown-1` **connected**, mode **1280×800**.
  
  The **KMS display is card1, not card0** (card0 is render-only lima, which has no connectors).

- **DRM backend (`RotatingDrmOutput`)**: The library's DRM/KMS backend auto-detects the connected KMS card: it scans `/dev/dri/card*`, skips render-only cards (lima card0, which has no connectors), and selects the connected display (ifm_dc card1). Reads the connector's preferred mode (1280×800), allocates DUMB scanout buffers at that size, and page-flips for tear-free presentation. Confirmed on-device 2026-09-21: the DRM backend auto-detected card1, initialized the tear-free page-flip path, and rendered at 1280×800 (no fallback).

### Rendering  [live ✓ 2026-09-21 for software Skia; GPU-for-Avalonia pending]

- **Current rendering:** Both backends use **software Skia** (no GPU dependency). Proven working on-device 2026-09-21.
- **GPU availability:** The CR1102 has a **Mali-400 GPU** exposed via the **lima** kernel driver, **Mesa DRI** (`/usr/lib/dri/lima_dri.so`), and **EGL/GLESv2** libraries (`libEGL.so.1`, `libGLESv2.so.2`, `libgbm`). The platform is **GLES2-capable**. However, whether **Avalonia** can use the GPU for **hardware-accelerated rendering** (instead of software Skia) is **[pending live]** — the library's current software-Skia path works, but GPU-accelerated Avalonia rendering on the CR1102 remains unverified.

## Avalonia support status

### Verified on-device (2026-09-21)

- **Output at 1280×800:** The `RotatingFbdevOutput` (fbdev) and `RotatingDrmOutput` (DRM/KMS) backends auto-detect the panel mode at runtime and render at 1280×800. Confirmed on-device: both backends rendered crisply at the correct resolution; the DRM backend auto-detected card1 (ifm_dc KMS display) and initialized the tear-free page-flip path (no fallback). **[live ✓ 2026-09-21]**
- **Touch input:** Avalonia's stock `EvDevBackend` (touchscreen input) auto-detected the **Atmel maXTouch Touchscreen** on `/dev/input/event0` and received touch events. The `CompositeInputBackend` composed touch + keypad successfully. Touch coordinate accuracy and extended touch UX under load remain **[pending live]**, but basic touch wiring is confirmed. **[live ✓ 2026-09-21]**
- **Physical keypad input:** The `Cr1140.Avalonia.Input.EvdevKeypadInput.DiscoverByName("PDM3 virtual keyboard")` auto-discovery found the uinput devices (event1–event4), and the evdev keycode map (F1–F8=59–66, arrows, Enter) is **confirmed** via `com.ifm.Keyboard.GetMapping(0)`. **[live ✓ 2026-09-21]**
- **Backlight:** `/sys/class/backlight/a00e0400.panel` (type raw, max_brightness 255) is writable; the `Backlight.SetPercent` API wrote, read back, and restored the brightness. **[live ✓ 2026-09-21]**
- **LEDs:** The two PWM RGB LEDs (`a0080000.rgbled:*:pri` and `*:sec`, each max 255) are writable; the `LedSysfs.SetRgb` API wrote primary blue, read back, and restored. **[live ✓ 2026-09-21]**
- **Device profile:** `Cr1140.Avalonia.Devices.DeviceProfiles.Cr1102` is the single source of truth for the CR1102's panel size (1280×800), touch/keypad presence, function-key/nav count, keypad/touch device names, backlight node/max, SoC thermal zone (null), and LED configuration. Consuming code (emulator/input factories) uses the profile.
- **Emulator preset:** `Cr1140.Avalonia.Emulator.EmulatorOptions.ForDevice(DeviceProfiles.Cr1102)` returns an `EmulatorOptions` pre-configured for 1280×800 **and an 8-key on-screen keypad (F1–F8)**. The desktop emulator bezel matches the CR1102 panel size and key count. **[live ✓ 2026-09-21]**
- **Input model:** The `KeypadKey` enum, the evdev keycode map (`EvdevKeypadInput`), and the desktop keyboard map (`WindowKeypadInput`) all cover **F1–F8** + Up/Down/Left/Right/Enter. The `SoftKeyFooter` control supports **6-key (CR1140/CR1141) or 8-key (CR1102)** layouts via `FunctionKeyCount`; it renders F7/F8 when the profile has 8 keys. On the CR1102 the eight keys are a **vertical column on the right bezel** in confirmed sequential order **top→bottom F1 F2 F3 F4 · nav/OK · F5 F6 F7 F8**, so `DeviceProfile.SoftKeyEdge=Right` and the demo docks the footer as a **vertical column on the right edge** (`SoftKeyFooter.Orientation=Vertical`), each label beside its physical key `[live ✓ 2026-09-21]`.
- **Demo device selector:** The reference demo (`cr1140-avalonia-demo`) accepts `--device=cr1102|cr1140|cr1141` (or `CR1140_DEVICE` env var) to select the device profile. `--device=cr1102` runs the demo in a **1280×800 touch emulator bezel** (desktop) or drives the real CR1102 hardware (on-device).

### What is pending on-device confirmation

- **GPU-accelerated Avalonia rendering:** The CR1102's Mali-400 GPU is exposed via lima + Mesa DRI + EGL/GLESv2, so the platform is GLES2-capable. Whether **Avalonia** can use the GPU for hardware-accelerated rendering (instead of software Skia) is **[pending live]**. The current software-Skia path works.
- **Touch coordinate calibration/accuracy under load:** Basic touch wiring is confirmed, but extended touch UX (multi-touch, coordinate accuracy, touch under CPU/GPU load) remains **[pending live]**.

## Device-profile API

The `Cr1140.Avalonia.Devices` namespace provides a **device-profile abstraction** — a single source of truth for per-device constants (panel size, touch/keypad presence, function-key/nav count, keypad/touch device names, backlight node/max hint, SoC thermal zone, LED configuration). See [`../cr1140-avalonia/CONTEXT.md`](../cr1140-avalonia/CONTEXT.md) §Devices for the full API.

- **`DeviceProfile`:** Immutable record with `required` properties: `Name`, `Article`, `Summary`, `PanelWidth`/`PanelHeight` (native landscape px), `HasTouch`, `HasKeypad`, `FunctionKeyCount`, `HasNavKey`, `KeypadDeviceName`, `TouchDeviceName`, `TouchDevicePath`, `BacklightNode`, `BacklightMaxHint`, `SocThermalZone` (uint?, null = no zone), `Leds` (IReadOnlyList<RgbLed>), `HasKeypadBacklight` (bool, true = sysfs-exposed keypad backlight), plus the non-required `SoftKeyEdge` (enum `SoftKeyEdge` { Bottom / Top / Left / Right }, default `Bottom`; `Right` for the CR1102), `KeypadBacklightViaDbus` (bool, default `false`; `true` for the CR1102 — key backlights driven over `com.ifm.Keyboard` D-Bus, not sysfs), and `TemperatureViaDbus` (bool, default `false`; `true` for the CR1102 — SoC-core + mainboard temps read over `com.ifm.Io.Temperature` D-Bus, not thermal-zone/hwmon).
- **`RgbLed` record:** `Name` (e.g. "Primary"/"Secondary"/"Status"/"Keypad"), `Role` (enum `LedRole`: `Status`, `KeypadBacklight`, `Primary`, `Secondary`), `RedLeaf`/`GreenLeaf`/`BlueLeaf` (per-channel sysfs leaf names under `/sys/class/leds/`, e.g. `"a0080000.rgbled:red:pri"`), `Max` (per-channel max brightness: 255 for PWM, 1 for binary), and `IsBinary` (true when `Max <= 1`).
- **`DeviceProfiles`:** Static class with `Cr1140`, `Cr1141`, `Cr1102` pre-defined profiles, `All` (read-only list), and `ByName(string)` (case-insensitive lookup, accepts "cr1102"/"1102"/"CR1102").
- **CR1102 profile values [live ✓ 2026-09-21]:** `PanelWidth=1280`, `PanelHeight=800`, `HasTouch=true`, `HasKeypad=true`, `FunctionKeyCount=8`, `HasNavKey=true`, `SoftKeyEdge=SoftKeyEdge.Right` (keys are a vertical column on the right bezel), `KeypadDeviceName="PDM3 virtual keyboard"`, `TouchDeviceName="Atmel maXTouch Touchscreen"`, `TouchDevicePath="/dev/input/event0"`, `BacklightNode="a00e0400.panel"`, `BacklightMaxHint=255`, `SocThermalZone=null` (no thermal zone), `Leds=[ RgbLed{Name="Primary", Role=Primary, RedLeaf="a0080000.rgbled:red:pri", GreenLeaf="…:green:pri", BlueLeaf="…:blue:pri", Max=255}, RgbLed{Name="Secondary", …:red/green/blue:sec, Max=255} ]`, `HasKeypadBacklight=false` (keypad button backlights are driven by the keyboard MCU over D-Bus, not sysfs), `KeypadBacklightViaDbus=true` (drive them via `IfmKeyboardLeds`), `TemperatureViaDbus=true` (SoC-core + mainboard temps via `com.ifm.Io.Temperature` D-Bus — read via `IfmSystemTemperatures`).

**Contrast with CR1140/CR1141:** The CR1140/CR1141 profiles have `SocThermalZone=0`, `Leds=[ RgbLed{Name="Status", Role=Status, leaves `{red,green,blue}:status`, Max=1 (binary)}, RgbLed{Name="Keypad", Role=KeypadBacklight, leaves `{red,green,blue}:kbd_backlight`, Max=255 (PWM)} ]`, `HasKeypadBacklight=true`, `BacklightNode="backlight"`, `BacklightMaxHint=400`. The CR1102 has **different sysfs node names, a null thermal zone, and two PWM RGB LEDs instead of status+kbd_backlight**.

**Important:** These constants are **hints/defaults**. Runtime reads (`Backlight.Max()`, `SystemTelemetry.Sample()`, output mode detection, LED discovery via `LedSysfs`) remain authoritative. The profile documents and centralizes per-device defaults; it does not override runtime reads.

## Conventions

- **Fact tagging:** Every CR1102-specific fact is tagged `[datasheet ✓]` (confirmed from ifm documentation) or `[pending live]` (requires on-device verification). **Never state an unverified fact as confirmed.**
- **Shared standard keycode map:** The evdev keycode map (F1–F8=59–66, arrows, Enter) is **assumed** the same standard ifm gpio-keys layout as the CR1140/CR1141. This is **not** duplicated per profile — the map is shared by all three devices. The `KeypadDevicePath` (device node) is per-profile.
- **Runtime reads are authoritative:** The device profile holds **defaults**; the library reads real hardware at runtime (`Backlight.Max()`, `SystemTelemetry` thermal zones, output backends reading the panel mode). A profile mismatch degrades that field to `null` or a fallback, not a hard error.
- **Package identity:** The library remains `Cr1140.Avalonia` (additive support for the CR1102; no package rename). Existing CR1140/CR1141 code is unchanged.


## On-device verification (2026-09-21)

The CR1102 Avalonia library support was **live-verified on the physical device** on 2026-09-21. Key findings:

- **Self-contained app:** A self-contained .NET linux-arm64 Avalonia app (the demo HMI) ran on-device and rendered **crisply at 1280×800** via the fbdev backend (framebuffer captured).
- **DRM/KMS auto-detection:** The `RotatingDrmOutput` backend auto-detected the connected KMS card (card1, ifm_dc driver), skipped the render-only lima card0, and initialized the tear-free page-flip backend (no fallback to fbdev). The demo rendered at 1280×800 via DRM page-flipping.
- **Backlight & LED sysfs round-trips:** Wrote to `/sys/class/backlight/a00e0400.panel/brightness` (type raw, max 255), read back, restored. Set primary blue LED (`a0080000.rgbled:blue:pri`), read back, restored. Sysfs writes confirmed working.
- **Emulator bezel:** The desktop emulator with `--device=cr1102` renders a **1280×800 (16:10 aspect ratio) bezel** with an **8-key function keypad (F1–F8)** + 4-way nav + OK, matching the CR1102's confirmed physical layout (vertical right column, top→bottom F1 F2 F3 F4 · nav · F5 F6 F7 F8 `[live ✓ 2026-09-21]`).
- **Device identity:** The device-tree model is `pdm3_10_001-2`, compatible `ifm,pdm3_10_001-2 ifm,pdm3-zynqmp xlnx,zynqmp`. OS kernel is **5.10.127** (NOT 4.14 as the datasheet states). Init = BusyBox sysvinit; display stack = Weston (Wayland); glibc 2.31.

**Status:** The library's CR1102 support is **production-ready** for software-Skia rendering at 1280×800 via fbdev/DRM, with confirmed keypad/touch input, backlight/LED control, and null thermal zone handling. GPU-accelerated Avalonia rendering remains unverified.
## Next steps (remaining on-device recon)

**Completed 2026-09-21:**

1. ✓ **Evdev keycode map:** Confirmed via `com.ifm.Keyboard.GetMapping(0)` — F1–F8=59–66, arrows=103/108/105/106, Enter=28 (same as CR1140/CR1141). `EvdevKeypadInput` keycode map is correct for all three devices.
2. ✓ **Sysfs node names:** Backlight node `a00e0400.panel` (type raw, max 255), LEDs `a0080000.rgbled:*:pri` and `*:sec` (PWM, max 255), thermal zone **none** (empty `/sys/class/thermal/`). `DeviceProfiles.Cr1102` updated with live values.
3. ✓ **Output mode detection:** fbdev backend reported 1280×800 from `/dev/fb0` (ifm-dcdrmfb, stride 5120, 32bpp, double-buffered). DRM backend auto-detected card1 (ifm_dc KMS display, connector connected, mode 1280×800), skipped card0 (lima render-only).
4. ✓ **Touch input:** Atmel maXTouch Touchscreen on `/dev/input/event0` auto-detected by Avalonia's stock `EvDevBackend`; `CompositeInputBackend` composed touch + keypad successfully.
5. ✓ **Keypad device discovery:** `ifm_service_keyboard` daemon injects keys on uinput devices named "PDM3 virtual keyboard" (event1–event4). `EvdevKeypadInput.DiscoverByName` auto-discovers and reads all matching nodes.
6. ✓ **Physical silk-screen key order:** Confirmed in native landscape — a single vertical column on the right bezel, top→bottom **F1 F2 F3 F4 · d-pad · F5 F6 F7 F8** (sequential, **not** the CR1140/CR1141 interleave). `SoftKeyLayoutMap.PhysicalOrderFor(8)` returns this order.

**Still pending:**

1. **GPU/GL for Avalonia:** The CR1102 has Mali-400 + lima + Mesa DRI + EGL/GLESv2. Confirm if **Avalonia** can use hardware-accelerated rendering (the library's software Skia works; GPU-for-Avalonia is unverified).
2. **Touch coordinate calibration/extended UX:** Basic touch wiring is confirmed, but coordinate accuracy and multi-touch behavior under load are unverified.

**Fact tag update:** All verified items in this document now carry `[live ✓ 2026-09-21]`. Remaining `[pending live]` items are GPU-for-Avalonia and extended touch UX.

## See also

- [`device-facts.md`](device-facts.md) — CR1140/CR1141 device facts (the original HAL ground truth)
- [`../cr1140-avalonia/CONTEXT.md`](../cr1140-avalonia/CONTEXT.md) — Avalonia library API + conventions
- [`../cr1140-avalonia-demo/CONTEXT.md`](../cr1140-avalonia-demo/CONTEXT.md) — Reference demo (including `--device` flag for emulator device selection)
