# cr1140-hal DX gaps — design

Date: 2026-05-29
Status: approved, ready for plan

Closes developer-experience gaps in `cr1140-hal` so that app authors stop
re-implementing the fiddly parts and stop carrying device-specific knowledge in
their own code. Scope (agreed): **`display`, `input`, `sys`**, plus a
cross-cutting **`HalError`** type and a **`prelude`**. CAN is out of scope and
stays on `std::io::Result` (Linux-only, not selected); a `From<io::Error>` keeps
it composable with `HalError` for future migration.

Device ground truth used here is from [`docs/device-facts.md`](../../device-facts.md):
fb is 800×480, 32 bpp, stride 3200, xRGB8888; keypad is `ifm-keypad` on
`/dev/input/event1` (gpio-keys); LEDs `*:status` (max 1) and `*:kbd_backlight`
(max 255); backlight `backlight` (max 400); SoC thermal zone used as zone 0; and
`ifm-local-setup` **races** on `/dev/fb0`, which is why double-buffering /
continuous redraw is a real robustness need, not a nicety.

## Cross-cutting: `HalError` + `prelude`

New `src/error.rs`:

```rust
#[derive(Debug, thiserror::Error)]
pub enum HalError {
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    #[error("device not found: {0}")]
    DeviceNotFound(String),
    #[error("unsupported framebuffer format: {0}")]
    UnsupportedFormat(String),
    #[error("value out of range: {0}")]
    OutOfRange(String),
    #[error("parse error: {0}")]
    Parse(String),
}

pub type HalResult<T> = Result<T, HalError>;
```

- Add `thiserror` to `cr1140-hal/Cargo.toml`.
- `From<io::Error>` (via `#[from]`) means existing `?` on `std::fs` / `nix`
  calls keeps working as signatures migrate `io::Result<T>` → `HalResult<T>`.
- Public fns in `display`, `input`, `sys` return `HalResult`. `sys::parse`
  helpers stay pure (`Option`). CAN unchanged.

New `src/prelude.rs` (re-export module, `pub mod prelude`):
`FbDisplay, Surface, Button, ButtonEvent, ButtonReader, Led, HalError, HalResult`,
and `CanBus` under `#[cfg(target_os = "linux")]`.

## `display`

### Format validation (`fbdev.rs`)
After `FBIOGET_VSCREENINFO`, reject anything that isn't the panel's xRGB8888:
require `bits_per_pixel == 32` and channel offsets `red.offset == 16`,
`green.offset == 8`, `blue.offset == 0`. On mismatch return
`HalError::UnsupportedFormat` with the observed bpp/offsets, instead of mapping a
buffer that will render as garbage.

Extract the decision as a pure fn for tests:
```rust
fn check_xrgb8888(bpp: u32, r_off: u32, g_off: u32, b_off: u32) -> Result<(), String>;
```

### Stride-aware blit (`surface.rs`)
Move the demo's row-copy into the HAL:
```rust
impl Surface<'_> {
    /// Copy a tightly/loosely packed source (same xRGB8888 byte order) into this
    /// surface, honouring both strides. Copies `min(self.width, src_width)` px/row.
    pub fn copy_from(&mut self, src_bytes: &[u8], src_stride: u32);
}
```
Pure and host-testable. The demo keeps only the `Xrgb8888 → &[u8]` reinterpret
(that `unsafe` belongs on the Slint side); the stride logic lives here once.

### Double-buffering (Approach A — additive + graceful degrade)
- `FbDisplay::open(path)` — unchanged behaviour: single buffer, `surface()`
  draws are immediately visible. Existing examples keep compiling.
- `FbDisplay::open_double_buffered(path)` — request `yres_virtual = 2 * yres`
  via `FBIOPUT_VSCREENINFO`, map both buffers. `surface()` returns the **back**
  buffer; `present()` issues `FBIOPAN_DISPLAY` to the back buffer's `yoffset`,
  then swaps front/back. If the driver rejects the larger `yres_virtual`, fall
  back to single-buffer: `buffer_count() == 1` and `present()` is a no-op
  (draws already landed in the one visible buffer).
- `buffer_count(&self) -> u32`.
- Pure, testable helpers:
  ```rust
  fn buffer_byte_offset(index: u32, yres: u32, stride: u32) -> usize; // index * yres * stride
  fn pan_yoffset(index: u32, yres: u32) -> u32;                       // index * yres
  ```

### `FBIOBLANK`
```rust
impl FbDisplay { pub fn blank(&self, on: bool) -> HalResult<()>; }
```
`FB_BLANK_POWERDOWN` (4) when `on`, `FB_BLANK_UNBLANK` (0) otherwise.

## `input`

### By-name discovery (`reader.rs` + small new helper)
```rust
pub const KEYPAD_NAME: &str = "ifm-keypad";

/// Scan `<class_dir>/event*/device/name`; return `/dev/input/eventN` for the
/// first whose name (trimmed) equals `target`. `class_dir` is a param so this
/// is host-testable against a temp dir; callers use `/sys/class/input`.
pub fn find_input_by_name(class_dir: &str, target: &str) -> HalResult<String>;
```
`DeviceNotFound(target)` when nothing matches.

```rust
impl ButtonReader {
    pub fn open_keypad() -> HalResult<Self>;             // find_input_by_name + open
    pub fn open_keypad_nonblocking() -> HalResult<Self>;
}
```
The name-trim/compare is unit-tested; the directory walk is tested against a
constructed temp tree.

### Fd accessor
`impl AsFd for ButtonReader` and `impl AsRawFd for ButtonReader`, delegating to
the inner `File`, so apps can register the keypad in an `epoll`/`select` loop
alongside CAN and a timer instead of `sleep`-polling.

## `sys`

### Typed LEDs
```rust
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Led { StatusRed, StatusGreen, StatusBlue, KbdRed, KbdGreen, KbdBlue }

impl Led {
    pub fn name(self) -> &'static str; // "red:status" .. "blue:kbd_backlight"
    pub fn max(self) -> u32;           // status = 1, kbd = 255
}
```
`set_led`/`read_led` accept `&str` (unchanged surface for arbitrary names); add
`Led`-typed convenience wrappers. Constants: `BACKLIGHT = "backlight"`,
`BACKLIGHT_MAX_HINT: u32 = 400`, `SOC_THERMAL_ZONE: u32 = 0`.

### Read-back (for save/restore)
```rust
pub fn read_led(name: &str) -> HalResult<u32>;
pub fn read_backlight(name: &str) -> HalResult<u32>;
```
Parse with the existing `parse::parse_brightness`.

### Enumeration
```rust
pub fn list_leds() -> HalResult<Vec<String>>;        // /sys/class/leds
pub fn list_backlights() -> HalResult<Vec<String>>;  // /sys/class/backlight
pub fn list_thermal_zones() -> HalResult<Vec<u32>>;  // thermal_zoneN
```

## Testing

Follow the repo's existing split — pure logic is host-tested, ioctl/fs wrappers
stay thin:

- `check_xrgb8888` (accept the device combo; reject 16 bpp / wrong offsets).
- `Surface::copy_from` (stride mismatch both directions; narrower/ wider src).
- `buffer_byte_offset` / `pan_yoffset`.
- `find_input_by_name` against a temp `/sys/class/input` tree (match, trim
  trailing newline, no-match → `DeviceNotFound`).
- `Led::name`/`Led::max` table.
- `HalError` `Display` strings and `From<io::Error>`.

ioctl paths (`open_double_buffered`, `present`, `blank`) and sysfs readers can
only be smoke-tested on the device; keep them thin over the pure helpers.

## Compatibility

- `open`, `surface`, `set_led`, `set_backlight`, `read_temp_c`, `backlight_max`,
  `ButtonReader::open[_nonblocking]`, all examples: **unchanged behaviour**.
- Return-type change `io::Result` → `HalResult` is source-compatible for callers
  using `?` (via `From<io::Error>`); callers that named `std::io::Result`
  explicitly must adjust. Examples in-repo are updated.
- The demo (`cr1140-slint-demo`) is updated to use `Surface::copy_from`,
  `ButtonReader::open_keypad_nonblocking`, and the `Led`/zone constants, removing
  its hand-rolled blit and magic strings.

## Out of scope (follow-ups)

- CAN completeness (stub/nonblocking/Frame/filters) — deferred.
- Drawing primitives (line/rect) on `Surface`.
- SDK-side `App`/loop harness and restore-on-exit guard.
