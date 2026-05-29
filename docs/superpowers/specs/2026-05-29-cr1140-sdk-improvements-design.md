# cr1140-sdk improvements — design

Date: 2026-05-29
Status: approved, ready for plan

Closes the SDK-layer gaps so app authors stop re-implementing telemetry plumbing,
leaking past the abstraction, and leaving hardware in a dirty state on exit. Adds
the persistence helper the README advertises (writable p2 overlay /
`/home/cds-apps`). Scope (agreed): **`metrics`** (reader/parser consistency +
aggregated snapshot), a new **`guard`** (RAII restore + opt-in signals), a new
**`config`** (atomic persistence), a cross-cutting **`SdkError`**, and **`tracing`**
facade integration. The `cr1140-slint-demo` is updated to use all of it.

## Framing: the SDK is a guest

The headline constraint: this SDK frequently runs **alongside / on top of** a host
that owns the process lifecycle — Taktora (github.com/patdhlk/taktora), ROS 2,
Apex.AI Apex.Grace, Apex.Ida, or similar executors/middleware. Each brings its own
logging stack and its own signal/shutdown handling. Three rules follow:

1. **Never init a global logging subscriber.** Emit through the `tracing` facade
   only; the host owns the subscriber. If none is installed, our events are
   dropped silently.
2. **Never grab process-global signals by default.** The shutdown guard restores
   on `Drop`; an *opt-in* call installs a SIGINT/SIGTERM handler, for standalone
   binaries only.
3. **Keep the lean path lean.** The heavier pieces sit behind default-on Cargo
   features (`config`, `signals`) so an embedder can take just
   `metrics`/`device`/`led` with `default-features = false`.

Device ground truth (from [`docs/device-facts.md`](../../device-facts.md)): `/` is a
writable overlay whose upper lives on `/dev/mmcblk0p2`; files under `/home/cds-apps`
persist across reboots; the overlay can lose power mid-write, so config writes must
be atomic. Backlight `backlight` (max 400); keypad LED = three PWM channels
(`{red,green,blue}:kbd_backlight`, 0–255). The HAL (`cr1140-hal`) already exposes
`read_backlight`, `read_led`, `set_backlight`, `set_kbd_backlight`, `read_temp_c`,
`SOC_THERMAL_ZONE`, and re-exports `HalError`/`HalResult` at its crate root.

## Cross-cutting: `SdkError` (`src/error.rs`)

Mirrors the HAL's thiserror pattern so callers — including ROS 2 / Apex nodes — can
match on the cause.

```rust
#[derive(Debug, thiserror::Error)]
pub enum SdkError {
    #[error("hal: {0}")]
    Hal(#[from] cr1140_hal::HalError),
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    #[cfg(feature = "config")]
    #[error("config decode: {0}")]
    Decode(#[from] toml::de::Error),
    #[cfg(feature = "config")]
    #[error("config encode: {0}")]
    Encode(#[from] toml::ser::Error),
}

pub type SdkResult<T> = Result<T, SdkError>;
```

Re-export `SdkError`/`SdkResult` from `lib.rs`. `led`/`guard`/`config` return
`SdkResult`. **`metrics` parsers and readers stay `Option`** — a missing `/proc`
line is absence, not an error. `device` keeps its current `Option`/`String`
surface unchanged.

## `metrics` — close the reader/parser leak + aggregated snapshot

### Every metric gets a reader
Today `CpuSampler::sample` and `read_loadavg` bundle read+parse, but `parse_meminfo`
and `parse_uptime` are parse-only, forcing the demo back to raw
`std::fs::read_to_string("/proc/meminfo")`. Add the missing readers (pure parsers
stay, host-tested):

```rust
pub fn read_meminfo() -> Option<(u64, u64)>;   // (MemTotal_kb, MemAvailable_kb)
pub fn read_uptime()  -> Option<f64>;          // seconds since boot
```

`CpuSampler` stays the one stateful sampler; `parse_stat`, `parse_meminfo`,
`mem_used_percent`, `parse_uptime`, `format_uptime`, `parse_loadavg`,
`read_loadavg` all stay.

### Aggregated snapshot
Because CPU% needs cross-call state, the snapshot is produced by a small struct
holding the sampler:

```rust
pub struct MemInfo { pub total_kb: u64, pub avail_kb: u64 }
impl MemInfo { pub fn used_percent(&self) -> f32; }   // wraps mem_used_percent

pub struct Snapshot {
    pub cpu_percent:  Option<f32>,
    pub mem:          Option<MemInfo>,
    pub soc_temp_c:   Option<f32>,   // cr1140_hal::sys::read_temp_c(soc_zone)
    pub board_temp_c: Option<f32>,   // device::read_board_temp_c (lm75 hwmon)
    pub uptime_secs:  Option<f64>,
    pub load1:        Option<f32>,
}

pub struct Telemetry { /* cpu: CpuSampler, soc_zone: u32 */ }
impl Telemetry {
    pub fn new() -> Self;                 // soc_zone = SOC_THERMAL_ZONE
    pub fn with_soc_zone(zone: u32) -> Self;
    pub fn sample(&mut self) -> Snapshot; // never errors; each field degrades to None
}
```

`sample()` collapses the demo's ~30-line 1 Hz block into one call. Network state
(eth0/can0) is deliberately **excluded** from `Snapshot` — it's a different concern
(`device`) with a different refresh cadence and stays where it is.

## `guard` (`src/guard.rs`) — RAII restore + opt-in signals

```rust
pub struct ShutdownGuard {
    // saved: backlight (name, value) + kbd RGB (r,g,b); flag: Arc<AtomicBool>
}

impl ShutdownGuard {
    /// Snapshot the current backlight (BACKLIGHT) + kbd LED so they can be restored.
    pub fn capture() -> SdkResult<Self>;
    /// Same, for a non-default backlight node name.
    pub fn capture_for(backlight_name: &str) -> SdkResult<Self>;

    /// Opt-in: install a SIGINT+SIGTERM handler that flips the shutdown flag.
    /// Standalone binaries only — do NOT call when a host executor owns signals.
    #[cfg(feature = "signals")]
    pub fn install_signal_handler(&self) -> SdkResult<()>;

    /// True once a registered signal has fired; the app loop polls this.
    pub fn should_shutdown(&self) -> bool;
}

impl Drop for ShutdownGuard {
    // Best-effort restore of backlight + kbd LED via the HAL.
    // On failure: tracing::warn!(...); never panics in drop.
}
```

- **Restore on `Drop`** covers normal scope exit, `?`-propagated errors, panic
  unwind, and host-driven shutdown (the host drops our objects) — all without any
  signal handling.
- **Signal handling is async-signal-safe**: the handler (via `signal-hook`) only
  sets an `AtomicBool`. No sysfs writes happen in signal context. The app loop sees
  `should_shutdown()`, breaks, and the guard's `Drop` does the sysfs restore on the
  main thread.
- Captured kbd RGB is read via `cr1140_hal::sys::read_led` on the three
  `*:kbd_backlight` channels; backlight via `read_backlight`. Restore via
  `set_kbd_backlight` / `set_backlight`.

## `config` (`src/config.rs`) — atomic persistence (feature `config`)

```rust
/// Persistent app dir on the writable p2 overlay; survives reboots.
pub const DEFAULT_APP_DIR: &str = "/home/cds-apps";

pub struct Store { /* path: PathBuf */ }
impl Store {
    pub fn at(path: impl Into<PathBuf>) -> Self;

    /// Ok(None) if the file does not exist; Err on decode failure.
    pub fn load<T: serde::de::DeserializeOwned>(&self) -> SdkResult<Option<T>>;

    /// Convenience: missing file -> T::default().
    pub fn load_or_default<T: serde::de::DeserializeOwned + Default>(&self) -> SdkResult<T>;

    /// Atomic save: write `<path>.tmp` in the same dir, fsync it, rename over
    /// `<path>`, then fsync the parent dir. A power cut mid-write leaves the
    /// previous file intact — never a half-written one.
    pub fn save<T: serde::Serialize>(&self, value: &T) -> SdkResult<()>;
}
```

- Format: **TOML** (`serde` + `toml`).
- The app owns the schema (its own `#[derive(Serialize, Deserialize, Default)]`
  struct); the SDK is generic over `T`.
- The caller supplies the path (defaulting to `DEFAULT_APP_DIR`), keeping the SDK
  free of assumptions about where a co-resident host wants state to live.
- Same-filesystem rename guarantees atomicity on the overlayfs upper.

## Logging — `tracing` facade

Add `tracing` (facade only; **no `tracing-subscriber` in the library**). Use it for
genuinely diagnostic events with structured fields, e.g.:

```rust
tracing::warn!(error = %e, "shutdown guard: backlight restore failed");
```

Not a blanket `println!` replacement for normal UI/status output. The host
(ROS 2 / Apex / Taktora) — or, for standalone runs, the demo — owns the subscriber.

## Cargo features (`cr1140-sdk/Cargo.toml`)

```toml
[features]
default = ["config", "signals"]
config  = ["dep:serde", "dep:toml"]
signals = ["dep:signal-hook"]

[dependencies]
cr1140-hal = { path = "../cr1140-hal" }
nix        = { version = "0.29", features = ["net"] }
tracing    = "0.1"
thiserror  = "2"                                        # matches cr1140-hal
serde       = { version = "1", features = ["derive"], optional = true }
toml        = { version = "0.9", optional = true }
signal-hook = { version = "0.3", optional = true }
```

- Core (`metrics`/`device`/`led`/`error` + `tracing`) is always present.
- A lean embedder inside a larger system can use
  `default-features = false` to drop config + signal-hook.

## Demo updates (`cr1140-slint-demo`)

- Replace the 1 Hz metrics block with `Telemetry::sample()` and read fields off the
  returned `Snapshot` (eth0/can0 still read via `device`).
- Wrap startup in `ShutdownGuard::capture()?` and (standalone binary) call
  `install_signal_handler()`. Change the bare `loop {}` to
  `while !guard.should_shutdown()` so exit is clean and the guard's `Drop` restores
  the panel + LED.
- `tracing_subscriber::fmt::init()` once at startup (the demo is standalone, so it
  owns the subscriber); convert the existing `println!` status lines to `tracing`.
- Add a `DemoConfig { backlight: u32, color_idx: usize, led_mode: u8 }` persisted
  via `Store::at(format!("{DEFAULT_APP_DIR}/cr1140-demo.toml"))`: `load_or_default`
  at start to restore the last backlight + LED color/mode, `save` whenever the user
  changes them. `led_mode` is the F-key index (0=Solid … 5=Heartbeat); the demo owns
  the `u8 ↔ LedMode` mapping, so `led.rs` stays serde-free. This exercises the
  headline persistence feature end to end.
- Add `cr1140-slint-demo` deps: `tracing`, `tracing-subscriber`, `serde` (derive).

## Testing

Follow the repo split — pure logic host-tested, sysfs/ioctl wrappers thin.

Host-testable:
- `MemInfo::used_percent` (complement of available; zero-total guard).
- `Store` round-trip in a temp dir: save→load equals input; `load` of a missing
  file → `Ok(None)`; `load_or_default` → default; after `save`, no leftover `.tmp`
  in the dir; a decode error on malformed TOML → `SdkError::Decode`.
- `SdkError` `Display` strings and `From` conversions (`HalError`, `io::Error`,
  toml de/ser).
- The shutdown-flag logic: `should_shutdown()` is false initially and true after the
  `AtomicBool` is set (test the flag plumbing without raising a real signal).

Device-only smoke (kept thin over the HAL): `ShutdownGuard::capture`/restore against
real sysfs, `read_meminfo`/`read_uptime`, `Telemetry::sample`.

## Compatibility

- `metrics` parsers, `CpuSampler`, `read_loadavg`, `format_uptime`,
  `mem_used_percent`, all of `device`, all of `led`: **unchanged**. New items are
  additive.
- New crate-root re-exports: `SdkError`, `SdkResult`, `Telemetry`, `Snapshot`,
  `MemInfo`, `ShutdownGuard`, and (feature `config`) `Store`, `DEFAULT_APP_DIR`.
- New deps: `tracing`, `thiserror` (always); `serde`+`toml` (feature `config`);
  `signal-hook` (feature `signals`).

## Out of scope (follow-ups)

- A full `App`/super-loop harness wrapping window + input + telemetry + guard.
- Network telemetry in `Snapshot` (kept in `device`).
- Multiple config profiles / migration/versioning of the persisted schema.
- An `epoll`-driven event loop (the HAL already exposes `AsFd` for the keypad).
