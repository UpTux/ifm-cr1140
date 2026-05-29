# cr1140-sdk Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the cr1140-sdk gaps — consistent metric readers + an aggregated telemetry snapshot, an RAII shutdown/restore guard with opt-in signals, atomic config persistence for the p2 overlay, a unified `SdkError`, and `tracing`-facade logging — then wire the demo to use all of it.

**Architecture:** The SDK is a *guest* under host executors (ROS 2 / Apex / Taktora): it never installs a logging subscriber, never grabs signals by default, and gates the heavier pieces behind default-on Cargo features (`config`, `signals`) so a lean embedder can drop them. New modules: `error` (always), `guard` (always), `config` (feature `config`). `metrics` gains readers + a `Telemetry`/`Snapshot`/`MemInfo` API. The HAL already provides `read_backlight`/`read_led`/`set_backlight`/`set_kbd_backlight`/`read_temp_c` and re-exports `HalError`.

**Tech Stack:** Rust 2021, `thiserror` 2, `tracing` 0.1 (facade), `serde` 1 + `toml` 0.9 (feature `config`), `signal-hook` 0.3 (feature `signals`). Host-testable pure logic via `cargo test -p cr1140-sdk`; device-only paths kept thin.

**Spec:** [`docs/superpowers/specs/2026-05-29-cr1140-sdk-improvements-design.md`](../specs/2026-05-29-cr1140-sdk-improvements-design.md)

**Conventions (match the existing repo):**
- Every file starts with `// SPDX-License-Identifier: GPL-3.0-only`.
- Pure parsers/readers in `metrics` return `Option`; fallible hardware/IO returns `SdkResult`.
- Host tests live in a `#[cfg(test)] mod tests` block at the bottom of each module file.
- Run host tests with `cargo test -p cr1140-sdk`. Verify the device demo compiles with `just build-slint` (cross-builds `aarch64-unknown-linux-musl`; the demo's real code is `#[cfg(target_os = "linux")]`, so a plain host `cargo build` does **not** type-check it).
- Commits: this repo signs commits via 1Password; if `git commit` fails with `1Password: failed to fill whole buffer`, the signing key needs an interactive unlock — pause and tell the user rather than disabling signing.

---

## File Structure

- `cr1140-sdk/Cargo.toml` — **Modify**: add deps + `[features]`.
- `cr1140-sdk/src/error.rs` — **Create**: `SdkError` + `SdkResult`.
- `cr1140-sdk/src/metrics.rs` — **Modify**: add `read_meminfo`/`read_uptime`, `MemInfo`, `Snapshot`, `Telemetry`.
- `cr1140-sdk/src/guard.rs` — **Create**: `ShutdownFlag`, `ShutdownGuard`.
- `cr1140-sdk/src/config.rs` — **Create**: `Store`, `DEFAULT_APP_DIR` (feature `config`).
- `cr1140-sdk/src/lib.rs` — **Modify**: declare modules + crate-root re-exports.
- `cr1140-slint-demo/Cargo.toml` — **Modify**: add `tracing`, `tracing-subscriber`, `serde`.
- `cr1140-slint-demo/src/main.rs` — **Modify**: use `Telemetry`, `ShutdownGuard`, `Store`, `tracing`.

---

## Task 1: Add deps and feature flags to `cr1140-sdk/Cargo.toml`

**Files:**
- Modify: `cr1140-sdk/Cargo.toml`

- [ ] **Step 1: Add `[features]` and dependencies**

Replace the existing `[dependencies]` section (currently just `cr1140-hal` and `nix`) so the file reads:

```toml
[dependencies]
cr1140-hal = { path = "../cr1140-hal" }
nix = { version = "0.29", features = ["net"] } # getifaddrs for interface IPs
tracing = "0.1"                                # facade only; no subscriber in the lib
thiserror = "2"                                # matches cr1140-hal's major
serde = { version = "1", features = ["derive"], optional = true }
toml = { version = "0.9", optional = true }
signal-hook = { version = "0.3", optional = true }

[features]
default = ["config", "signals"]
config = ["dep:serde", "dep:toml"]
signals = ["dep:signal-hook"]
```

- [ ] **Step 2: Verify it resolves**

Run: `cargo build -p cr1140-sdk`
Expected: PASS (no code uses the new deps yet; this just confirms versions resolve against `Cargo.lock`).

- [ ] **Step 3: Commit**

```bash
git add cr1140-sdk/Cargo.toml Cargo.lock
git commit -m "build(sdk): add tracing/thiserror/serde/toml/signal-hook deps + features"
```

---

## Task 2: `SdkError` and `SdkResult`

**Files:**
- Create: `cr1140-sdk/src/error.rs`
- Modify: `cr1140-sdk/src/lib.rs`
- Test: in `cr1140-sdk/src/error.rs`

- [ ] **Step 1: Write the failing test**

Create `cr1140-sdk/src/error.rs` with the test module first (the types come in Step 3):

```rust
// SPDX-License-Identifier: GPL-3.0-only
//! Unified SDK error type. Mirrors `cr1140_hal::HalError`'s thiserror pattern so
//! callers — including ROS 2 / Apex nodes — can match on the cause. Metrics
//! parsers/readers stay `Option`; `led`/`guard`/`config` return [`SdkResult`].

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn from_io_error_displays_with_prefix() {
        let io = std::io::Error::new(std::io::ErrorKind::NotFound, "nope");
        let e: SdkError = io.into();
        assert!(e.to_string().starts_with("io: "), "got {e}");
    }

    #[test]
    fn from_hal_error_is_wrapped() {
        let hal = cr1140_hal::HalError::DeviceNotFound("kbd".into());
        let e: SdkError = hal.into();
        assert!(matches!(e, SdkError::Hal(_)));
        assert!(e.to_string().starts_with("hal: "), "got {e}");
    }

    #[cfg(feature = "config")]
    #[test]
    fn from_toml_decode_error_is_wrapped() {
        // `= 1` with no key is invalid TOML.
        let err = toml::from_str::<toml::Table>("= 1").unwrap_err();
        let e: SdkError = err.into();
        assert!(matches!(e, SdkError::Decode(_)));
    }
}
```

- [ ] **Step 2: Add the module to `lib.rs` and run the test to verify it fails**

In `cr1140-sdk/src/lib.rs`, add after the existing `pub mod` lines:

```rust
pub mod error;
```

Run: `cargo test -p cr1140-sdk error::`
Expected: FAIL — compile error, `cannot find type SdkError in this scope`.

- [ ] **Step 3: Implement the error type**

In `cr1140-sdk/src/error.rs`, insert above the `#[cfg(test)]` block:

```rust
/// Errors from fallible SDK operations (hardware restore, config IO).
#[derive(Debug, thiserror::Error)]
pub enum SdkError {
    /// An underlying HAL operation failed (sysfs read/write, parse).
    #[error("hal: {0}")]
    Hal(#[from] cr1140_hal::HalError),
    /// Filesystem IO failed.
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    /// A config file could not be decoded from TOML.
    #[cfg(feature = "config")]
    #[error("config decode: {0}")]
    Decode(#[from] toml::de::Error),
    /// A config value could not be encoded to TOML.
    #[cfg(feature = "config")]
    #[error("config encode: {0}")]
    Encode(#[from] toml::ser::Error),
}

/// Result alias for SDK operations.
pub type SdkResult<T> = Result<T, SdkError>;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cargo test -p cr1140-sdk error::`
Expected: PASS (3 tests).

- [ ] **Step 5: Re-export from the crate root**

In `cr1140-sdk/src/lib.rs`, add below the `pub mod` lines:

```rust
pub use error::{SdkError, SdkResult};
```

Run: `cargo build -p cr1140-sdk`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add cr1140-sdk/src/error.rs cr1140-sdk/src/lib.rs
git commit -m "feat(sdk): add SdkError/SdkResult with HAL/io/toml conversions"
```

---

## Task 3: `metrics` readers — `read_meminfo`, `read_uptime`

**Files:**
- Modify: `cr1140-sdk/src/metrics.rs`
- Test: in `cr1140-sdk/src/metrics.rs`

These give every metric a reader (closing the parse-only leak). The readers are thin over the already-tested pure parsers, so the new tests assert the *shape* (they read the real `/proc` on a Linux host; on macOS they return `None`, which is still a valid assertion).

- [ ] **Step 1: Write the failing test**

In `cr1140-sdk/src/metrics.rs`, inside the existing `#[cfg(test)] mod tests` block, add:

```rust
    #[test]
    fn read_meminfo_shape_is_total_ge_avail_when_present() {
        // On Linux this reads /proc; on non-Linux hosts it's None. Either is OK,
        // but if present, total must be >= available.
        if let Some((total, avail)) = read_meminfo() {
            assert!(total >= avail, "total {total} < avail {avail}");
            assert!(total > 0);
        }
    }

    #[test]
    fn read_uptime_is_nonnegative_when_present() {
        if let Some(secs) = read_uptime() {
            assert!(secs >= 0.0);
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cargo test -p cr1140-sdk metrics::tests::read_`
Expected: FAIL — `cannot find function read_meminfo` / `read_uptime`.

- [ ] **Step 3: Implement the readers**

In `cr1140-sdk/src/metrics.rs`, add right after the `parse_meminfo` function:

```rust
/// Read `(MemTotal, MemAvailable)` in kB straight from `/proc/meminfo`.
pub fn read_meminfo() -> Option<(u64, u64)> {
    parse_meminfo(&fs::read_to_string("/proc/meminfo").ok()?)
}
```

And add right after the `parse_uptime` function:

```rust
/// Read seconds since boot straight from `/proc/uptime`.
pub fn read_uptime() -> Option<f64> {
    parse_uptime(&fs::read_to_string("/proc/uptime").ok()?)
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cargo test -p cr1140-sdk metrics::tests::read_`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add cr1140-sdk/src/metrics.rs
git commit -m "feat(sdk): add read_meminfo/read_uptime so every metric has a reader"
```

---

## Task 4: `metrics` aggregated snapshot — `MemInfo`, `Snapshot`, `Telemetry`

**Files:**
- Modify: `cr1140-sdk/src/metrics.rs`
- Modify: `cr1140-sdk/src/lib.rs`
- Test: in `cr1140-sdk/src/metrics.rs`

- [ ] **Step 1: Write the failing test**

In `cr1140-sdk/src/metrics.rs`, inside `#[cfg(test)] mod tests`, add:

```rust
    #[test]
    fn meminfo_used_percent_matches_helper() {
        let m = MemInfo { total_kb: 1000, avail_kb: 250 };
        assert!((m.used_percent() - 75.0).abs() < 0.001);
        let zero = MemInfo { total_kb: 0, avail_kb: 0 };
        assert_eq!(zero.used_percent(), 0.0);
    }

    #[test]
    fn telemetry_sample_first_cpu_is_zero_then_populates() {
        let mut t = Telemetry::new();
        let first = t.sample();
        // First CPU sample primes the baseline and reports 0% (when present).
        if let Some(p) = first.cpu_percent {
            assert_eq!(p, 0.0);
        }
        // The struct exposes all six fields; just touch them so the shape is fixed.
        let _ = (first.mem, first.soc_temp_c, first.board_temp_c, first.uptime_secs, first.load1);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cargo test -p cr1140-sdk metrics::tests::meminfo_used_percent_matches_helper`
Expected: FAIL — `cannot find type MemInfo` / `Telemetry`.

- [ ] **Step 3: Implement `MemInfo`, `Snapshot`, `Telemetry`**

In `cr1140-sdk/src/metrics.rs`, add near the top after the `use std::fs;` line:

```rust
use cr1140_hal::sys::{read_temp_c, SOC_THERMAL_ZONE};
```

Then add (above the `#[cfg(test)]` block):

```rust
/// Memory totals in kB, with a convenience for the used fraction.
#[derive(Clone, Copy, Debug)]
pub struct MemInfo {
    pub total_kb: u64,
    pub avail_kb: u64,
}

impl MemInfo {
    /// Used memory as a percentage (`0.0..=100.0`).
    pub fn used_percent(&self) -> f32 {
        mem_used_percent(self.total_kb, self.avail_kb)
    }
}

/// A single point-in-time read of all telemetry the dashboard shows. Every field
/// degrades to `None` independently, so a missing `/proc` file or thermal zone
/// never fails the whole sample. Network state (eth0/can0) is intentionally not
/// here — it lives in [`crate::device`] with a different refresh cadence.
#[derive(Clone, Copy, Debug, Default)]
pub struct Snapshot {
    pub cpu_percent: Option<f32>,
    pub mem: Option<MemInfo>,
    pub soc_temp_c: Option<f32>,
    pub board_temp_c: Option<f32>,
    pub uptime_secs: Option<f64>,
    pub load1: Option<f32>,
}

/// Holds the per-call CPU state so one [`sample`](Telemetry::sample) call yields a
/// whole [`Snapshot`]. Replaces a hand-rolled ~30-line 1 Hz block in apps.
pub struct Telemetry {
    cpu: CpuSampler,
    soc_zone: u32,
}

impl Telemetry {
    /// New collector reading the default SoC thermal zone ([`SOC_THERMAL_ZONE`]).
    pub fn new() -> Self {
        Self { cpu: CpuSampler::new(), soc_zone: SOC_THERMAL_ZONE }
    }

    /// New collector reading a specific thermal zone for the SoC temperature.
    pub fn with_soc_zone(zone: u32) -> Self {
        Self { cpu: CpuSampler::new(), soc_zone: zone }
    }

    /// Sample every metric now. The first call primes CPU% and reports 0%.
    pub fn sample(&mut self) -> Snapshot {
        Snapshot {
            cpu_percent: self.cpu.sample(),
            mem: read_meminfo().map(|(total_kb, avail_kb)| MemInfo { total_kb, avail_kb }),
            soc_temp_c: read_temp_c(self.soc_zone).ok(),
            board_temp_c: crate::device::read_board_temp_c(),
            uptime_secs: read_uptime(),
            load1: read_loadavg(),
        }
    }
}

impl Default for Telemetry {
    fn default() -> Self {
        Self::new()
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cargo test -p cr1140-sdk metrics::`
Expected: PASS (all metrics tests, including the 2 new ones).

- [ ] **Step 5: Re-export from the crate root**

In `cr1140-sdk/src/lib.rs`, add below the existing re-export:

```rust
pub use metrics::{MemInfo, Snapshot, Telemetry};
```

Run: `cargo build -p cr1140-sdk`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add cr1140-sdk/src/metrics.rs cr1140-sdk/src/lib.rs
git commit -m "feat(sdk): add Telemetry::sample -> Snapshot aggregating all metrics"
```

---

## Task 5: `guard` — `ShutdownFlag` (testable signal flag)

**Files:**
- Create: `cr1140-sdk/src/guard.rs`
- Modify: `cr1140-sdk/src/lib.rs`
- Test: in `cr1140-sdk/src/guard.rs`

Split the signal flag into its own small type so the shutdown logic is host-testable without raising a real signal.

- [ ] **Step 1: Write the failing test**

Create `cr1140-sdk/src/guard.rs`:

```rust
// SPDX-License-Identifier: GPL-3.0-only
//! Shutdown/restore for the keypad LED and display backlight.
//!
//! [`ShutdownGuard`] snapshots the backlight + kbd LED on construction and
//! restores them on `Drop` — covering normal exit, `?`-propagated errors, panic
//! unwind, and host-driven shutdown (a host executor dropping our objects).
//!
//! Signal handling is **opt-in** and async-signal-safe: the handler only flips an
//! `AtomicBool` (via `signal-hook`); the sysfs restore runs later on the main
//! thread when the app loop observes [`ShutdownGuard::should_shutdown`].

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn flag_starts_clear_and_reflects_raise() {
        let flag = ShutdownFlag::new();
        assert!(!flag.is_set());
        flag.raise();
        assert!(flag.is_set());
    }

    #[test]
    fn cloned_flag_shares_state() {
        let a = ShutdownFlag::new();
        let b = a.clone();
        a.raise();
        assert!(b.is_set(), "clone must share the same AtomicBool");
    }
}
```

- [ ] **Step 2: Add the module to `lib.rs` and run the test to verify it fails**

In `cr1140-sdk/src/lib.rs`, add to the `pub mod` group:

```rust
pub mod guard;
```

Run: `cargo test -p cr1140-sdk guard::`
Expected: FAIL — `cannot find type ShutdownFlag`.

- [ ] **Step 3: Implement `ShutdownFlag`**

In `cr1140-sdk/src/guard.rs`, insert above the `#[cfg(test)]` block:

```rust
/// A shared "please shut down" flag set by a signal handler and polled by the
/// app loop. Cheap to clone (shares one `AtomicBool`).
#[derive(Clone, Default)]
pub struct ShutdownFlag(Arc<AtomicBool>);

impl ShutdownFlag {
    /// New, clear flag.
    pub fn new() -> Self {
        Self::default()
    }

    /// True once a registered signal has fired.
    pub fn is_set(&self) -> bool {
        self.0.load(Ordering::Relaxed)
    }

    /// Register SIGINT + SIGTERM to set this flag. Opt-in; standalone binaries
    /// only — do not call when a host executor (ROS 2 / Apex / Taktora) owns
    /// signals. Async-signal-safe: the handler only stores into the `AtomicBool`.
    #[cfg(feature = "signals")]
    pub fn install_handler(&self) -> crate::SdkResult<()> {
        signal_hook::flag::register(signal_hook::consts::SIGINT, Arc::clone(&self.0))?;
        signal_hook::flag::register(signal_hook::consts::SIGTERM, Arc::clone(&self.0))?;
        Ok(())
    }

    /// Test seam: set the flag without raising a real signal.
    #[cfg(test)]
    fn raise(&self) {
        self.0.store(true, Ordering::Relaxed);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cargo test -p cr1140-sdk guard::`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add cr1140-sdk/src/guard.rs cr1140-sdk/src/lib.rs
git commit -m "feat(sdk): add ShutdownFlag (testable, async-signal-safe shutdown flag)"
```

---

## Task 6: `guard` — `ShutdownGuard` (RAII capture + restore)

**Files:**
- Modify: `cr1140-sdk/src/guard.rs`
- Modify: `cr1140-sdk/src/lib.rs`

`capture`/restore touch real sysfs, so they're device-only smoke paths kept thin. The host-testable behaviour (the flag) is already covered in Task 5; here we verify the type *compiles and wires the flag through*.

- [ ] **Step 1: Write the failing test**

In `cr1140-sdk/src/guard.rs`, add inside `#[cfg(test)] mod tests`:

```rust
    #[test]
    fn guard_should_shutdown_tracks_its_flag() {
        // Build a guard without touching hardware via the test-only constructor,
        // then confirm should_shutdown() reflects the embedded flag.
        let g = ShutdownGuard::inert_for_test();
        assert!(!g.should_shutdown());
        g.flag.raise();
        assert!(g.should_shutdown());
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cargo test -p cr1140-sdk guard::tests::guard_should_shutdown`
Expected: FAIL — `cannot find type ShutdownGuard`.

- [ ] **Step 3: Implement `ShutdownGuard`**

In `cr1140-sdk/src/guard.rs`, add these imports at the top (below the existing `use` lines):

```rust
use cr1140_hal::sys::{read_backlight, read_led, set_backlight, set_kbd_backlight, Led, BACKLIGHT};
```

Then add above the `#[cfg(test)]` block:

```rust
/// Captures the current backlight + keypad-LED state and restores it on `Drop`.
pub struct ShutdownGuard {
    backlight_name: String,
    backlight: u32,
    kbd: (u8, u8, u8),
    flag: ShutdownFlag,
}

impl ShutdownGuard {
    /// Snapshot the default [`BACKLIGHT`] node and the kbd LED for later restore.
    pub fn capture() -> crate::SdkResult<Self> {
        Self::capture_for(BACKLIGHT)
    }

    /// Snapshot a specific backlight node name and the kbd LED.
    pub fn capture_for(backlight_name: &str) -> crate::SdkResult<Self> {
        let backlight = read_backlight(backlight_name)?;
        let r = read_led(Led::KbdRed.name())?.min(255) as u8;
        let g = read_led(Led::KbdGreen.name())?.min(255) as u8;
        let b = read_led(Led::KbdBlue.name())?.min(255) as u8;
        Ok(Self {
            backlight_name: backlight_name.to_string(),
            backlight,
            kbd: (r, g, b),
            flag: ShutdownFlag::new(),
        })
    }

    /// Install the opt-in SIGINT/SIGTERM handler backing [`should_shutdown`].
    /// Standalone binaries only — see [`ShutdownFlag::install_handler`].
    #[cfg(feature = "signals")]
    pub fn install_signal_handler(&self) -> crate::SdkResult<()> {
        self.flag.install_handler()
    }

    /// A clone of the shutdown flag, e.g. to share with another thread.
    pub fn flag(&self) -> ShutdownFlag {
        self.flag.clone()
    }

    /// True once a registered signal has fired; poll this in the app loop.
    pub fn should_shutdown(&self) -> bool {
        self.flag.is_set()
    }

    /// Build a guard with no captured hardware state, for host tests only.
    #[cfg(test)]
    fn inert_for_test() -> Self {
        Self {
            backlight_name: String::new(),
            backlight: 0,
            kbd: (0, 0, 0),
            flag: ShutdownFlag::new(),
        }
    }
}

impl Drop for ShutdownGuard {
    fn drop(&mut self) {
        // Best-effort restore; never panic in drop. Log failures via tracing so a
        // host subscriber (if any) sees them.
        if !self.backlight_name.is_empty() {
            if let Err(e) = set_backlight(&self.backlight_name, self.backlight) {
                tracing::warn!(error = %e, "shutdown guard: backlight restore failed");
            }
        }
        let (r, g, b) = self.kbd;
        if let Err(e) = set_kbd_backlight(r, g, b) {
            tracing::warn!(error = %e, "shutdown guard: kbd LED restore failed");
        }
    }
}
```

> Note: the `inert_for_test` guard's `Drop` still calls `set_kbd_backlight`, which fails on a non-Linux host; the error is logged and swallowed, so the test passes. The `backlight_name.is_empty()` check skips the backlight write for the inert guard.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cargo test -p cr1140-sdk guard::`
Expected: PASS (3 tests).

- [ ] **Step 5: Re-export from the crate root**

In `cr1140-sdk/src/lib.rs`, add:

```rust
pub use guard::{ShutdownFlag, ShutdownGuard};
```

Run: `cargo build -p cr1140-sdk`
Expected: PASS.

- [ ] **Step 6: Verify the `signals`-off build still compiles**

Run: `cargo build -p cr1140-sdk --no-default-features --features config`
Expected: PASS (confirms `install_handler`/`install_signal_handler` are correctly gated and nothing else references `signal-hook`).

- [ ] **Step 7: Commit**

```bash
git add cr1140-sdk/src/guard.rs cr1140-sdk/src/lib.rs
git commit -m "feat(sdk): add ShutdownGuard RAII restore of backlight + kbd LED"
```

---

## Task 7: `config` — atomic `Store`

**Files:**
- Create: `cr1140-sdk/src/config.rs`
- Modify: `cr1140-sdk/src/lib.rs`
- Test: in `cr1140-sdk/src/config.rs`

- [ ] **Step 1: Write the failing test**

Create `cr1140-sdk/src/config.rs`:

```rust
// SPDX-License-Identifier: GPL-3.0-only
//! Atomic config persistence for the writable p2 overlay (`/home/cds-apps`).
//!
//! [`Store`] is generic over any `serde` type; the app owns the schema. Saves are
//! atomic (temp file + fsync + rename) so a power cut on the overlay never leaves
//! a half-written file — the previous version stays intact.

use crate::{SdkError, SdkResult};
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};

use serde::de::DeserializeOwned;
use serde::Serialize;

/// Persistent app directory on the p2 overlay; writes here survive reboots.
pub const DEFAULT_APP_DIR: &str = "/home/cds-apps";

#[cfg(test)]
mod tests {
    use super::*;
    use serde::Deserialize;

    #[derive(Debug, PartialEq, Serialize, Deserialize, Default)]
    struct Cfg {
        brightness: u32,
        label: String,
    }

    fn temp_path(tag: &str) -> PathBuf {
        std::env::temp_dir()
            .join(format!("cr1140-store-{}-{}-{}.toml", std::process::id(), tag, line!()))
    }

    #[test]
    fn save_then_load_round_trips() {
        let p = temp_path("roundtrip");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        let cfg = Cfg { brightness: 200, label: "green".into() };
        store.save(&cfg).unwrap();
        let back: Option<Cfg> = store.load().unwrap();
        assert_eq!(back, Some(cfg));
        let _ = fs::remove_file(&p);
    }

    #[test]
    fn load_missing_file_is_none() {
        let p = temp_path("missing");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        let back: Option<Cfg> = store.load().unwrap();
        assert_eq!(back, None);
    }

    #[test]
    fn load_or_default_uses_default_when_absent() {
        let p = temp_path("default");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        let back: Cfg = store.load_or_default().unwrap();
        assert_eq!(back, Cfg::default());
    }

    #[test]
    fn save_leaves_no_tmp_file() {
        let p = temp_path("notmp");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        store.save(&Cfg { brightness: 1, label: "x".into() }).unwrap();
        let tmp = p.with_extension("tmp");
        assert!(!tmp.exists(), "temp file {tmp:?} should have been renamed away");
        let _ = fs::remove_file(&p);
    }

    #[test]
    fn load_malformed_toml_is_decode_error() {
        let p = temp_path("malformed");
        fs::write(&p, "this is not = = toml").unwrap();
        let store = Store::at(&p);
        let err = store.load::<Cfg>().unwrap_err();
        assert!(matches!(err, SdkError::Decode(_)), "got {err}");
        let _ = fs::remove_file(&p);
    }
}
```

- [ ] **Step 2: Add the module to `lib.rs` and run the test to verify it fails**

In `cr1140-sdk/src/lib.rs`, add (feature-gated):

```rust
#[cfg(feature = "config")]
pub mod config;
```

Run: `cargo test -p cr1140-sdk config::`
Expected: FAIL — `cannot find type Store`.

- [ ] **Step 3: Implement `Store`**

In `cr1140-sdk/src/config.rs`, insert above the `#[cfg(test)]` block:

```rust
/// A TOML-backed config file. The app supplies the path and the schema type.
pub struct Store {
    path: PathBuf,
}

impl Store {
    /// A store at a specific file path (e.g. `format!("{DEFAULT_APP_DIR}/app.toml")`).
    pub fn at(path: impl Into<PathBuf>) -> Self {
        Self { path: path.into() }
    }

    /// Load and decode the file. `Ok(None)` if it does not exist.
    pub fn load<T: DeserializeOwned>(&self) -> SdkResult<Option<T>> {
        match fs::read_to_string(&self.path) {
            Ok(s) => Ok(Some(toml::from_str(&s)?)),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(e) => Err(SdkError::Io(e)),
        }
    }

    /// Load, or return `T::default()` if the file is absent.
    pub fn load_or_default<T: DeserializeOwned + Default>(&self) -> SdkResult<T> {
        Ok(self.load()?.unwrap_or_default())
    }

    /// Encode and atomically write: temp file in the same dir, fsync, rename over
    /// the target, then fsync the directory. Atomic on the overlayfs upper.
    pub fn save<T: Serialize>(&self, value: &T) -> SdkResult<()> {
        let s = toml::to_string(value)?;
        let dir = self.path.parent().unwrap_or_else(|| Path::new("."));
        fs::create_dir_all(dir)?;
        let tmp = self.path.with_extension("tmp");
        {
            let mut f = fs::File::create(&tmp)?;
            f.write_all(s.as_bytes())?;
            f.sync_all()?;
        }
        fs::rename(&tmp, &self.path)?;
        // Best-effort dir fsync so the rename itself is durable; ignore errors
        // (some filesystems reject O_RDONLY dir fsync).
        if let Ok(d) = fs::File::open(dir) {
            let _ = d.sync_all();
        }
        Ok(())
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cargo test -p cr1140-sdk config::`
Expected: PASS (5 tests).

- [ ] **Step 5: Re-export from the crate root**

In `cr1140-sdk/src/lib.rs`, add:

```rust
#[cfg(feature = "config")]
pub use config::{Store, DEFAULT_APP_DIR};
```

Run: `cargo build -p cr1140-sdk`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add cr1140-sdk/src/config.rs cr1140-sdk/src/lib.rs
git commit -m "feat(sdk): add atomic TOML config Store for the p2 overlay"
```

---

## Task 8: Refresh `lib.rs` module docs

**Files:**
- Modify: `cr1140-sdk/src/lib.rs`

The crate-level doc comment still lists only `led`/`metrics`/`device`. Update it to describe the new modules so `cargo doc` and readers stay accurate.

- [ ] **Step 1: Update the crate doc comment**

Replace the doc-comment bullet list in `cr1140-sdk/src/lib.rs` (the lines describing `led`/`metrics`/`device`) with:

```rust
//! - [`led`] — RGB keypad-LED animation modes and a [`led::LedDriver`].
//! - [`metrics`] — generic Linux telemetry (CPU, memory, load, uptime) plus a
//!   [`metrics::Telemetry`] collector returning an aggregated [`metrics::Snapshot`].
//! - [`device`] — device & OS identity and network state.
//! - [`guard`] — [`guard::ShutdownGuard`]: restore backlight/LED on exit (RAII),
//!   with opt-in SIGINT/SIGTERM handling for standalone binaries.
//! - [`config`] — atomic [`config::Store`] persistence for the p2 overlay
//!   (`/home/cds-apps`); enabled by the default `config` feature.
//!
//! Errors from fallible operations surface as [`SdkError`]. This crate is a guest
//! under host executors (ROS 2 / Apex / Taktora): it logs through the `tracing`
//! facade without installing a subscriber, and never grabs signals by default.
```

- [ ] **Step 2: Verify docs build**

Run: `cargo doc -p cr1140-sdk --no-deps`
Expected: PASS (no broken intra-doc links).

- [ ] **Step 3: Commit**

```bash
git add cr1140-sdk/src/lib.rs
git commit -m "docs(sdk): document guard/config modules and SdkError in crate docs"
```

---

## Task 9: Demo dependencies

**Files:**
- Modify: `cr1140-slint-demo/Cargo.toml`

- [ ] **Step 1: Add the new demo dependencies**

In `cr1140-slint-demo/Cargo.toml`, add to `[dependencies]` (after the existing `cr1140-*` and `slint` entries):

```toml
tracing = "0.1"
tracing-subscriber = "0.3"                  # the demo is standalone, so it owns the subscriber
serde = { version = "1", features = ["derive"] }
```

- [ ] **Step 2: Verify the workspace still resolves**

Run: `cargo build -p cr1140-slint-demo`
Expected: PASS (host build of the non-Linux stub `main`; confirms deps resolve).

- [ ] **Step 3: Commit**

```bash
git add cr1140-slint-demo/Cargo.toml Cargo.lock
git commit -m "build(demo): add tracing/tracing-subscriber/serde for SDK integration"
```

---

## Task 10: Wire the demo to `Telemetry`, `ShutdownGuard`, `Store`, `tracing`

**Files:**
- Modify: `cr1140-slint-demo/src/main.rs`

This is a device-only file (`#[cfg(target_os = "linux")]`). Type-check it with `just build-slint` (cross-build); a plain host `cargo build` only compiles the non-Linux stub. Make the edits below, then verify once at the end.

- [ ] **Step 1: Add a persisted `DemoConfig` type**

In `cr1140-slint-demo/src/main.rs`, just below the `slint::include_modules!();` line (outside any `fn`, so it's shared), add:

```rust
/// Demo settings persisted to the p2 overlay so the panel comes back the way the
/// user left it. `led_mode` is the F-key index (0=Solid..5=Heartbeat); the demo
/// owns the index<->LedMode mapping so `cr1140-sdk::led` stays serde-free.
#[cfg(target_os = "linux")]
#[derive(serde::Serialize, serde::Deserialize)]
struct DemoConfig {
    backlight: u32,
    color_idx: usize,
    led_mode: u8,
}

#[cfg(target_os = "linux")]
impl Default for DemoConfig {
    fn default() -> Self {
        // Mid-brightness, LED off, Solid mode — matches the previous hard-coded start.
        Self { backlight: 0, color_idx: 0, led_mode: 0 }
    }
}

#[cfg(target_os = "linux")]
fn led_mode_from_index(i: u8) -> cr1140_sdk::led::LedMode {
    use cr1140_sdk::led::LedMode;
    match i {
        1 => LedMode::Dim,
        2 => LedMode::Pulse,
        3 => LedMode::Blink,
        4 => LedMode::Flash,
        5 => LedMode::Heartbeat,
        _ => LedMode::Solid,
    }
}

#[cfg(target_os = "linux")]
fn led_mode_to_index(m: cr1140_sdk::led::LedMode) -> u8 {
    use cr1140_sdk::led::LedMode;
    match m {
        LedMode::Solid => 0,
        LedMode::Dim => 1,
        LedMode::Pulse => 2,
        LedMode::Blink => 3,
        LedMode::Flash => 4,
        LedMode::Heartbeat => 5,
    }
}
```

- [ ] **Step 2: Update imports and init tracing**

In the `#[cfg(target_os = "linux")] fn main`, update the `use` block: remove the now-unused metrics parse helpers and add the new SDK types. Replace:

```rust
    use cr1140_sdk::metrics::{
        format_uptime, mem_used_percent, parse_meminfo, parse_uptime, read_loadavg, CpuSampler,
    };
```

with:

```rust
    use cr1140_sdk::metrics::format_uptime;
    use cr1140_sdk::{ShutdownGuard, Store, Telemetry, DEFAULT_APP_DIR};
```

Then, as the very first statement inside `main` (before opening hardware), add:

```rust
    tracing_subscriber::fmt::init();
```

- [ ] **Step 3: Capture the shutdown guard and load persisted config**

Immediately after the backlight is first read (`let bl_max = backlight_max(...)`), and before `let mut backlight = bl_max / 2;`, insert:

```rust
    // Restore the panel + LED to their pre-launch state when we exit (RAII), and
    // install the opt-in signal handler (this binary is standalone).
    let guard = ShutdownGuard::capture()?;
    guard.install_signal_handler()?;

    // Load persisted demo settings (or defaults on first run / fresh overlay).
    let store = Store::at(format!("{DEFAULT_APP_DIR}/cr1140-demo.toml"));
    let cfg: DemoConfig = store.load_or_default().unwrap_or_default();
```

- [ ] **Step 4: Apply the loaded config to backlight, LED color, and mode**

Replace:

```rust
    // Start mid-brightness so the Up/Down demo has headroom in both directions.
    let mut backlight = bl_max / 2;
    let _ = set_backlight(BACKLIGHT, backlight);

    // Keypad LED: a base color (Enter cycles PALETTE) × an animation mode (F1–F6).
    let mut led = LedDriver::new();
    let mut color_idx = 0usize; // "off"
```

with:

```rust
    // Start from persisted backlight, or mid-brightness on first run.
    let mut backlight = if cfg.backlight == 0 { bl_max / 2 } else { cfg.backlight.min(bl_max) };
    let _ = set_backlight(BACKLIGHT, backlight);

    // Keypad LED: a base color (Enter cycles PALETTE) × an animation mode (F1–F6).
    let mut led = LedDriver::new();
    let mut color_idx = cfg.color_idx.min(PALETTE.len() - 1);
    led.set_mode(led_mode_from_index(cfg.led_mode));
    let (_, r0, g0, b0) = PALETTE[color_idx];
    led.set_color((r0, g0, b0));
```

- [ ] **Step 5: Add a config-save helper**

After the `update_led_ui` closure definition, add a helper closure that snapshots current state to disk:

```rust
    let save_cfg = |store: &Store, backlight: u32, color_idx: usize, led: &LedDriver| {
        let cfg = DemoConfig {
            backlight,
            color_idx,
            led_mode: led_mode_to_index(led.mode()),
        };
        if let Err(e) = store.save(&cfg) {
            tracing::warn!(error = %e, "failed to persist demo config");
        }
    };
```

- [ ] **Step 6: Save config after each user change**

In the input-handling `match btn { ... }`, add a `save_cfg(...)` call at the end of each arm that mutates state. Specifically:

In the `Button::Up` arm, after `push_backlight(&ui, backlight);` add:
```rust
                        save_cfg(&store, backlight, color_idx, &led);
```
In the `Button::Down` arm, after `push_backlight(&ui, backlight);` add:
```rust
                        save_cfg(&store, backlight, color_idx, &led);
```
In the `Button::Enter` arm, after `update_led_ui(&ui, color_idx, led.mode());` add:
```rust
                        save_cfg(&store, backlight, color_idx, &led);
```
In the `Button::F1 | ... | Button::F6` arm, after `update_led_ui(&ui, color_idx, led.mode());` add:
```rust
                        save_cfg(&store, backlight, color_idx, &led);
```

- [ ] **Step 7: Replace the 1 Hz metrics block with `Telemetry::sample()`**

Replace the metrics setup lines:

```rust
    let mut cpu = CpuSampler::new();
    let mut last_metrics = Instant::now() - Duration::from_secs(2); // force immediate sample
```

with:

```rust
    let mut telemetry = Telemetry::new();
    let mut last_metrics = Instant::now() - Duration::from_secs(2); // force immediate sample
```

Then replace the entire `if last_metrics.elapsed() >= Duration::from_secs(1) { ... }` block with:

```rust
        // --- metrics: refresh ~1 Hz via the SDK's aggregated snapshot ---
        if last_metrics.elapsed() >= Duration::from_secs(1) {
            last_metrics = Instant::now();
            let snap = telemetry.sample();

            if let Some(p) = snap.cpu_percent {
                ui.set_cpu_percent(p);
                ui.set_cpu_text(format!("{p:.0} %").into());
            }
            if let Some(mem) = snap.mem {
                let p = mem.used_percent();
                ui.set_mem_percent(p);
                ui.set_mem_text(format!("{p:.0} %").into());
            }
            if let Some(t) = snap.soc_temp_c {
                ui.set_temp_c(t);
                // map ~20..80 °C onto the bar
                ui.set_temp_percent(((t - 20.0) / 60.0 * 100.0).clamp(0.0, 100.0));
                ui.set_temp_text(format!("{t:.1}").into()); // unit appended in .slint
            }
            if let Some(secs) = snap.uptime_secs {
                ui.set_uptime(format_uptime(secs).into());
            }
            if let Some(bt) = snap.board_temp_c {
                ui.set_board_text(format!("Board {bt:.1} °C").into());
            }
            if let Some(l) = snap.load1 {
                ui.set_load_text(format!("load {l:.2}").into());
            }
            ui.set_can_text(format!("CAN {}", read_operstate("can0")).into());
            let eth = iface_ipv4("eth0").unwrap_or_else(|| read_operstate("eth0"));
            ui.set_eth_text(format!("eth0 {eth}").into());
        }
```

> This removes the two raw `std::fs::read_to_string("/proc/...")` fallbacks. `read_operstate`/`iface_ipv4` stay (network is in `device`, not `Snapshot`). The `read_temp_c`/`SOC_THERMAL_ZONE`/`read_board_temp_c`/`read_loadavg` imports they replaced are no longer needed — see Step 9.

- [ ] **Step 8: Make the loop exit cleanly so the guard restores**

Replace the bare `loop {` that drives the app with:

```rust
    while !guard.should_shutdown() {
```

And after the loop (after its closing `}`), add:

```rust
    // `guard` drops here, restoring the pre-launch backlight + LED.
    tracing::info!("shutting down; restoring panel state");
    Ok(())
```

> The function currently ends inside an infinite `loop`; with a `while`, `main` needs a trailing `Ok(())`. Confirm there is exactly one `Ok(())` return.

- [ ] **Step 9: Clean up imports and convert `println!` to `tracing`**

Remove from the `use` blocks any now-unused HAL imports. The metrics now come from `Telemetry`, so `read_temp_c`, `SOC_THERMAL_ZONE` are no longer used directly; remove them from the `cr1140_hal::sys::{...}` import (keep `backlight_max`, `set_backlight`, `BACKLIGHT`). Remove `read_board_temp_c` from the `cr1140_sdk::device::{...}` import (keep `hostname`, `iface_ipv4`, `os_release`, `read_operstate`). Remove the unused `Duration` only if the compiler flags it (it's still used by `frame_period`/`from_secs`, so keep it).

Convert the two status `println!` calls to tracing:
- `println!("display {}x{} bpp {} stride {} ({} buffer(s))", ...)` → `tracing::info!(...)` with the same format string and args.
- `println!("ready; Slint dashboard on /dev/fb0 (Ctrl-C to exit)");` → `tracing::info!("ready; Slint dashboard on /dev/fb0 (Ctrl-C to exit)");`

- [ ] **Step 10: Cross-build the demo to type-check the Linux path**

Run: `just build-slint`
Expected: PASS — a clean release cross-build for `aarch64-unknown-linux-musl`. Fix any compile errors (unused imports, the `Ok(())`/`while` change, closure borrow of `store`/`led`) until it builds.

- [ ] **Step 11: Commit**

```bash
git add cr1140-slint-demo/src/main.rs
git commit -m "feat(demo): use Telemetry snapshot, ShutdownGuard, Store persistence, tracing"
```

---

## Task 11: Full workspace verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full host test suite**

Run: `cargo test`
Expected: PASS — all crates, including the new SDK tests (error: 3, metrics: +4, guard: 3, config: 5).

- [ ] **Step 2: Verify feature permutations compile**

Run each and expect PASS:
```bash
cargo build -p cr1140-sdk                                            # default (config+signals)
cargo build -p cr1140-sdk --no-default-features                     # lean guest: no config, no signals
cargo build -p cr1140-sdk --no-default-features --features config   # config only
cargo build -p cr1140-sdk --no-default-features --features signals  # signals only
```

- [ ] **Step 3: Lint**

Run: `cargo clippy --workspace --all-targets`
Expected: PASS with no warnings in the changed crates. Fix any clippy findings (e.g. needless clones, `format!` in `Store::at` call site).

- [ ] **Step 4: Cross-build the demo once more**

Run: `just build-slint`
Expected: PASS.

- [ ] **Step 5: Confirm the static ELF**

Run: `file target/aarch64-unknown-linux-musl/release/cr1140-slint-demo`
Expected: `ELF 64-bit LSB ... ARM aarch64 ... statically linked`.

- [ ] **Step 6: Final commit (if any lint fixes were made)**

```bash
git add -A
git commit -m "chore(sdk): clippy + feature-matrix cleanups"
```

---

## Self-Review Notes (for the implementer)

- **Device smoke test (manual, optional):** after `just run-slint`, change backlight/LED, restart the demo — settings should restore from `/home/cds-apps/cr1140-demo.toml`. Press Ctrl-C — the panel backlight + keypad LED should return to their pre-launch values (guard restore).
- **Do not** add `tracing_subscriber` to the `cr1140-sdk` library — only the demo binary initialises a subscriber.
- **`led.rs` stays serde-free** — the index↔mode mapping lives in the demo (Task 10 Step 1).
- If a `git commit` step fails with `1Password: failed to fill whole buffer`, stop and tell the user; do not disable commit signing.
