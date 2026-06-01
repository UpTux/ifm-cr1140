// SPDX-License-Identifier: GPL-3.0-only
//! Pure model for the Settings -> Fieldbus screen of the round-baler demo.
//!
//! Like [`crate::counter`] and [`crate::wrapping`], this module is free of UI,
//! time-source, and persistence dependencies: every method that needs the clock
//! takes an explicit `now_ms` (monotonic milliseconds), so the whole settings
//! state machine is host-testable without a real `Instant`.
//!
//! The screen lets the operator switch the active *fieldbus* between EtherCAT
//! (the machine default) and plain Ethernet (a service / diagnostics mode).
//! Switching only takes effect after a reboot, so the commit is gated behind a
//! double-press confirmation that mirrors the "reset total" pattern in
//! [`crate::counter`].

/// Which fieldbus the operator panel speaks.
///
/// Kept a plain C-like enum so it can be embedded in `BalerRetain` and persisted
/// via postcard without surprises.
#[derive(serde::Serialize, serde::Deserialize, Clone, Copy, PartialEq, Eq, Debug)]
pub enum Fieldbus {
    /// EtherCAT real-time fieldbus — the normal machine default.
    EtherCat,
    /// Plain Ethernet — a service / diagnostics mode.
    Ethernet,
}

impl Default for Fieldbus {
    // Spelled out (not derived) to document the policy decision: EtherCAT is the
    // machine default; Ethernet is the service / diagnostics mode.
    #[allow(clippy::derivable_impls)]
    fn default() -> Self {
        Fieldbus::EtherCat
    }
}

impl Fieldbus {
    /// The *other* fieldbus (EtherCAT <-> Ethernet).
    pub fn toggled(self) -> Fieldbus {
        match self {
            Fieldbus::EtherCat => Fieldbus::Ethernet,
            Fieldbus::Ethernet => Fieldbus::EtherCat,
        }
    }
}

/// Outcome of pressing the "reboot to apply" soft-key.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum RebootPress {
    /// First press (or after expiry): armed, awaiting a confirming press.
    Armed,
    /// Second press within the window: the caller should flush retain + reboot.
    Committed,
}

/// State machine for the Settings -> Fieldbus screen. Pure; injected time.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct Settings {
    /// The fieldbus the app booted in — an immutable snapshot.
    booted: Fieldbus,
    /// The fieldbus the operator has currently selected.
    pending: Fieldbus,
    /// `Some(ts)` while the reboot double-confirm is armed (ts = the arming
    /// press time). Auto-expires `REBOOT_CONFIRM_WINDOW_MS` after `ts`.
    reboot_armed_at: Option<u64>,
}

impl Settings {
    /// Double-confirm window for the "reboot to apply" press (~2 s).
    pub const REBOOT_CONFIRM_WINDOW_MS: u64 = 2_000;

    /// A fresh model booted in `booted`: `pending == booted`, reboot disarmed.
    pub fn new(booted: Fieldbus) -> Self {
        Self {
            booted,
            pending: booted,
            reboot_armed_at: None,
        }
    }

    pub fn booted(&self) -> Fieldbus {
        self.booted
    }

    pub fn pending(&self) -> Fieldbus {
        self.pending
    }

    /// Toggle the pending fieldbus (EtherCAT <-> Ethernet).
    ///
    /// The safe-state interlock is supplied by the caller as `can_switch` (this
    /// keeps the settings model decoupled from the wrapping model). When the
    /// switch is allowed the pending selection flips, any pending reboot is
    /// disarmed (the target just changed), and `true` is returned. When it is
    /// blocked nothing changes and `false` is returned.
    pub fn toggle(&mut self, can_switch: bool) -> bool {
        if !can_switch {
            return false;
        }
        self.pending = self.pending.toggled();
        self.reboot_armed_at = None;
        true
    }

    /// Whether the pending selection differs from the booted fieldbus, i.e. a
    /// reboot is needed to apply it.
    pub fn reboot_required(&self) -> bool {
        self.pending != self.booted
    }

    /// Press the "reboot to apply" soft-key.
    ///
    /// Only meaningful while [`reboot_required`](Self::reboot_required) holds;
    /// otherwise returns `None`. A first press (disarmed, or after the window has
    /// expired) arms the double-confirm and returns [`RebootPress::Armed`]. A
    /// second press while still armed commits: disarm and return
    /// [`RebootPress::Committed`] (the caller's signal to flush retain + reboot).
    pub fn press_reboot(&mut self, now_ms: u64) -> Option<RebootPress> {
        if !self.reboot_required() {
            return None;
        }
        if self.reboot_armed(now_ms) {
            self.reboot_armed_at = None;
            Some(RebootPress::Committed)
        } else {
            self.reboot_armed_at = Some(now_ms);
            Some(RebootPress::Armed)
        }
    }

    /// True only if the reboot is armed AND still within the confirm window
    /// (auto-expires `REBOOT_CONFIRM_WINDOW_MS` after the arming press).
    pub fn reboot_armed(&self, now_ms: u64) -> bool {
        match self.reboot_armed_at {
            Some(armed_at) => now_ms.saturating_sub(armed_at) < Self::REBOOT_CONFIRM_WINDOW_MS,
            None => false,
        }
    }

    /// Disarm the reboot double-confirm (e.g. on leaving the Settings screen).
    pub fn disarm_reboot(&mut self) {
        self.reboot_armed_at = None;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fieldbus_default_is_ethercat() {
        assert_eq!(Fieldbus::default(), Fieldbus::EtherCat);
    }

    #[test]
    fn fieldbus_toggled_flips_both_ways() {
        assert_eq!(Fieldbus::EtherCat.toggled(), Fieldbus::Ethernet);
        assert_eq!(Fieldbus::Ethernet.toggled(), Fieldbus::EtherCat);
    }

    #[test]
    fn new_pending_matches_booted_and_no_reboot() {
        let s = Settings::new(Fieldbus::EtherCat);
        assert_eq!(s.booted(), Fieldbus::EtherCat);
        assert_eq!(s.pending(), Fieldbus::EtherCat);
        assert!(!s.reboot_required());
    }

    #[test]
    fn toggle_when_allowed_flips_pending_and_tracks_reboot_required() {
        let mut s = Settings::new(Fieldbus::EtherCat);
        assert!(s.toggle(true));
        assert_eq!(s.pending(), Fieldbus::Ethernet);
        assert!(s.reboot_required());
        // Toggling back to the booted value clears the reboot requirement.
        assert!(s.toggle(true));
        assert_eq!(s.pending(), Fieldbus::EtherCat);
        assert!(!s.reboot_required());
    }

    #[test]
    fn toggle_when_blocked_is_a_noop() {
        let mut s = Settings::new(Fieldbus::EtherCat);
        assert!(!s.toggle(false));
        assert_eq!(s.pending(), Fieldbus::EtherCat);
        assert!(!s.reboot_required());
    }

    #[test]
    fn press_reboot_is_none_when_not_required() {
        let mut s = Settings::new(Fieldbus::EtherCat);
        // pending == booted, so there is nothing to reboot for.
        assert_eq!(s.press_reboot(1_000), None);
        assert!(!s.reboot_armed(1_000));
    }

    #[test]
    fn press_reboot_arms_then_commits_within_window() {
        let mut s = Settings::new(Fieldbus::EtherCat);
        assert!(s.toggle(true));
        assert!(s.reboot_required());

        // First press: arms.
        assert_eq!(s.press_reboot(0), Some(RebootPress::Armed));
        assert!(s.reboot_armed(0));

        // Second press within the window: commits and disarms.
        assert_eq!(s.press_reboot(1_000), Some(RebootPress::Committed));
        assert!(!s.reboot_armed(1_000));
    }

    #[test]
    fn reboot_arm_auto_expires_and_re_arms_after_window() {
        let mut s = Settings::new(Fieldbus::EtherCat);
        assert!(s.toggle(true));
        assert_eq!(s.press_reboot(0), Some(RebootPress::Armed));

        // Within the window: still armed.
        assert!(s.reboot_armed(Settings::REBOOT_CONFIRM_WINDOW_MS - 1));
        // Past the window: auto-expired.
        assert!(!s.reboot_armed(Settings::REBOOT_CONFIRM_WINDOW_MS + 1));

        // A press after expiry re-arms (does NOT commit).
        assert_eq!(
            s.press_reboot(Settings::REBOOT_CONFIRM_WINDOW_MS + 1),
            Some(RebootPress::Armed)
        );
    }

    #[test]
    fn toggle_disarms_a_pending_reboot() {
        let mut s = Settings::new(Fieldbus::EtherCat);
        assert!(s.toggle(true));
        assert_eq!(s.press_reboot(0), Some(RebootPress::Armed));
        assert!(s.reboot_armed(0));

        // The target just changed, so the armed reboot is cleared. Toggling back
        // to booted also clears reboot_required, so the next press is a no-op.
        assert!(s.toggle(true));
        assert!(!s.reboot_armed(0));
    }

    #[test]
    fn disarm_reboot_clears_armed_state() {
        let mut s = Settings::new(Fieldbus::EtherCat);
        assert!(s.toggle(true));
        assert_eq!(s.press_reboot(0), Some(RebootPress::Armed));
        assert!(s.reboot_armed(0));

        s.disarm_reboot();
        assert!(!s.reboot_armed(0));
        // After disarming, the next press arms afresh rather than committing.
        assert_eq!(s.press_reboot(100), Some(RebootPress::Armed));
    }
}
