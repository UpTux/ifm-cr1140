// SPDX-License-Identifier: GPL-3.0-only
//! Wrapping timed cycle (issue 08): a pure, host-testable model of the
//! "activate wrapping" function with INJECTED time.
//!
//! The screen has two states — [`WrapState::Idle`] and [`WrapState::Wrapping`].
//! Pressing F1 Start Wrap begins a simulated ~5 s cycle (emitting one
//! [`Command::WrapStart`] CAN frame) that fills a progress bar and auto-returns
//! to Idle; F2 Cancel returns to Idle immediately. Start is a no-op while a
//! cycle is already active.
//!
//! ## Time is injected
//!
//! Every time-dependent method takes `now_ms` (monotonic milliseconds). The
//! model stores only the start timestamp and computes state/progress lazily
//! from `now_ms` — no `tick` and no `Instant::now()`, so tests are free of
//! wall-clock flakiness.
//!
//! ## Boundary / auto-complete semantics
//!
//! A cycle is "active" over the half-open interval `[started_at, started_at +
//! WRAP_DURATION_MS)`. The instant the full duration has *elapsed* the cycle is
//! complete: at exactly `started_at + WRAP_DURATION_MS` the state is already
//! [`WrapState::Idle`] again. Consequently `progress` only ever returns values
//! strictly less than 1.0 while still `Wrapping` (e.g. 0.9998 just before the
//! boundary), and snaps back to 0.0 the moment the model is `Idle`. The 1.0
//! clamp guards against a caller passing a `now_ms` it has not yet recognised
//! as complete — progress is never reported above 1.0.

use crate::can::Command;

/// Simulated wrap-cycle duration (~5 s).
pub const WRAP_DURATION_MS: u64 = 5_000;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum WrapState {
    Idle,
    Wrapping,
}

/// Pure model of the wrapping cycle. Holds only the start timestamp of an
/// active cycle (`None` when idle); all state is derived from injected `now_ms`.
pub struct Wrapping {
    started_at: Option<u64>,
}

impl Wrapping {
    /// A fresh model, [`WrapState::Idle`].
    pub fn new() -> Self {
        Self { started_at: None }
    }

    /// Current state at `now_ms`. A cycle auto-completes back to [`WrapState::Idle`]
    /// once `WRAP_DURATION_MS` has elapsed (computed lazily — no tick needed).
    pub fn state(&self, now_ms: u64) -> WrapState {
        match self.started_at {
            Some(start) if now_ms.saturating_sub(start) < WRAP_DURATION_MS => WrapState::Wrapping,
            _ => WrapState::Idle,
        }
    }

    /// Progress `0.0..=1.0` while [`WrapState::Wrapping`]; `0.0` when
    /// [`WrapState::Idle`]. Clamped at `1.0`.
    pub fn progress(&self, now_ms: u64) -> f32 {
        match self.started_at {
            Some(start) if self.state(now_ms) == WrapState::Wrapping => {
                let elapsed = now_ms.saturating_sub(start) as f32;
                (elapsed / WRAP_DURATION_MS as f32).clamp(0.0, 1.0)
            }
            _ => 0.0,
        }
    }

    /// F1 Start Wrap. If currently [`WrapState::Idle`] at `now_ms`: begin a
    /// cycle and return `Some(Command::WrapStart)`. If a cycle is already
    /// active: ignored, return `None` (the original cycle is untouched).
    pub fn start(&mut self, now_ms: u64) -> Option<Command> {
        match self.state(now_ms) {
            WrapState::Idle => {
                self.started_at = Some(now_ms);
                Some(Command::WrapStart)
            }
            WrapState::Wrapping => None,
        }
    }

    /// F2 Cancel: return to [`WrapState::Idle`] immediately.
    pub fn cancel(&mut self) {
        self.started_at = None;
    }
}

impl Default for Wrapping {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn new_is_idle_with_zero_progress() {
        let w = Wrapping::new();
        assert_eq!(w.state(0), WrapState::Idle);
        assert_eq!(w.progress(0), 0.0);
    }

    #[test]
    fn start_from_idle_emits_wrapstart_and_begins_cycle() {
        let mut w = Wrapping::new();
        assert_eq!(w.start(0), Some(Command::WrapStart));
        assert_eq!(w.state(1), WrapState::Wrapping);
    }

    #[test]
    fn progress_is_half_at_midpoint_and_still_wrapping() {
        let mut w = Wrapping::new();
        w.start(0);
        let mid = WRAP_DURATION_MS / 2; // 2500
        assert_eq!(w.state(mid), WrapState::Wrapping);
        assert!((w.progress(mid) - 0.5).abs() < 1e-3);
    }

    #[test]
    fn auto_completes_to_idle_at_and_beyond_boundary() {
        let mut w = Wrapping::new();
        w.start(0);
        // Just below the boundary: still wrapping, progress high but < 1.0.
        assert_eq!(w.state(WRAP_DURATION_MS - 1), WrapState::Wrapping);
        assert!(w.progress(WRAP_DURATION_MS - 1) < 1.0);
        assert!(w.progress(WRAP_DURATION_MS - 1) > 0.99);
        // At the boundary the full duration has elapsed: already Idle, progress 0.0.
        assert_eq!(w.state(WRAP_DURATION_MS), WrapState::Idle);
        assert_eq!(w.progress(WRAP_DURATION_MS), 0.0);
        // Beyond: still Idle.
        assert_eq!(w.state(WRAP_DURATION_MS + 10_000), WrapState::Idle);
        assert_eq!(w.progress(WRAP_DURATION_MS + 10_000), 0.0);
    }

    #[test]
    fn start_is_ignored_while_active_and_does_not_restart() {
        let mut w = Wrapping::new();
        w.start(0);
        // Pressing start mid-cycle is a no-op: no command, no restart.
        assert_eq!(w.start(2500), None);
        // The original cycle still completes at 5000, NOT at 7500.
        assert_eq!(w.state(WRAP_DURATION_MS), WrapState::Idle);
        // Sanity: it was genuinely still wrapping just before 5000.
        assert_eq!(w.state(WRAP_DURATION_MS - 1), WrapState::Wrapping);
    }

    #[test]
    fn cancel_mid_cycle_returns_to_idle_immediately() {
        let mut w = Wrapping::new();
        w.start(0);
        assert_eq!(w.state(2500), WrapState::Wrapping);
        w.cancel();
        assert_eq!(w.state(2500), WrapState::Idle);
        assert_eq!(w.progress(2500), 0.0);
    }

    #[test]
    fn start_works_again_after_auto_complete() {
        let mut w = Wrapping::new();
        w.start(0);
        // First cycle has auto-completed by WRAP_DURATION_MS; start a fresh one.
        assert_eq!(w.state(WRAP_DURATION_MS), WrapState::Idle);
        assert_eq!(w.start(WRAP_DURATION_MS), Some(Command::WrapStart));
        // The new cycle runs from its own start: wrapping mid-way, idle at its end.
        assert_eq!(w.state(WRAP_DURATION_MS + 2500), WrapState::Wrapping);
        assert_eq!(w.state(WRAP_DURATION_MS * 2), WrapState::Idle);
    }

    #[test]
    fn start_works_again_after_cancel() {
        let mut w = Wrapping::new();
        w.start(0);
        w.cancel();
        // Cancel returns to idle, so a fresh start is accepted at the same instant.
        assert_eq!(w.start(2500), Some(Command::WrapStart));
        assert_eq!(w.state(2500 + 1), WrapState::Wrapping);
        assert_eq!(w.state(2500 + WRAP_DURATION_MS), WrapState::Idle);
    }
}
