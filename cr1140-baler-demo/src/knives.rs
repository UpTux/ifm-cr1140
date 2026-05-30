// SPDX-License-Identifier: GPL-3.0-only
//! Knives in/out toggle (issue 07).

use crate::can::Command;

/// Session-only knife position. Live machine state, NOT persisted — starts OUT
/// each launch (issue 07). The type carries no resume/load path, so the
/// position is in-memory only by construction.
pub struct Knives {
    /// `false` = OUT (default each launch), `true` = IN.
    engaged: bool,
}

impl Knives {
    /// New session: knives start OUT.
    pub fn new() -> Self {
        Self { engaged: false }
    }

    /// `true` = IN, `false` = OUT.
    pub fn is_in(&self) -> bool {
        self.engaged
    }

    /// Toggle IN<->OUT and return the KNIVES command carrying the NEW position.
    pub fn toggle(&mut self) -> Command {
        self.engaged = !self.engaged;
        Command::Knives(self.engaged)
    }
}

impl Default for Knives {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn new_starts_out() {
        let k = Knives::new();
        assert!(!k.is_in());
    }

    #[test]
    fn toggle_out_to_in_returns_knives_true() {
        let mut k = Knives::new();
        let cmd = k.toggle();
        assert!(k.is_in());
        assert_eq!(cmd, Command::Knives(true));
    }

    #[test]
    fn toggle_in_to_out_returns_knives_false() {
        let mut k = Knives::new();
        k.toggle(); // OUT -> IN
        let cmd = k.toggle(); // IN -> OUT
        assert!(!k.is_in());
        assert_eq!(cmd, Command::Knives(false));
    }

    #[test]
    fn default_matches_new_starts_out() {
        assert!(!Knives::default().is_in());
    }

    #[test]
    fn toggle_twice_returns_to_out() {
        // State is consistent across toggles: OUT -> IN -> OUT, and the second
        // command reflects the new (OUT) position.
        let mut k = Knives::new();
        assert_eq!(k.toggle(), Command::Knives(true));
        assert_eq!(k.toggle(), Command::Knives(false));
        assert!(!k.is_in());
    }
}
