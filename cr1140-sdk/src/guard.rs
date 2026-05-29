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

use cr1140_hal::sys::{read_backlight, read_led, set_backlight, set_kbd_backlight, Led, BACKLIGHT};

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

/// Captures the current backlight + keypad-LED state and restores it on `Drop`.
pub struct ShutdownGuard {
    /// Captured backlight node name. Empty means "no hardware captured" (the
    /// `inert_for_test` guard), in which case `Drop` skips all restore writes.
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
        // host subscriber (if any) sees them. An empty backlight_name marks an
        // inert guard with no captured hardware — skip both restores together.
        if self.backlight_name.is_empty() {
            return;
        }
        if let Err(e) = set_backlight(&self.backlight_name, self.backlight) {
            tracing::warn!(error = %e, "shutdown guard: backlight restore failed");
        }
        let (r, g, b) = self.kbd;
        if let Err(e) = set_kbd_backlight(r, g, b) {
            tracing::warn!(error = %e, "shutdown guard: kbd LED restore failed");
        }
    }
}

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

    #[test]
    fn guard_should_shutdown_tracks_its_flag() {
        // Build a guard without touching hardware via the test-only constructor,
        // then confirm should_shutdown() reflects the embedded flag.
        let g = ShutdownGuard::inert_for_test();
        assert!(!g.should_shutdown());
        g.flag.raise();
        assert!(g.should_shutdown());
    }
}
