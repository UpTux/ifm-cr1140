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
