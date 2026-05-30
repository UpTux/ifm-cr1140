// SPDX-License-Identifier: GPL-3.0-only
//! Bale counter + retained lifetime total, stats, resets (issues 04/05/06).
//!
//! The bale [`Counter`] is one cohesive model: a per-launch session count, a
//! reflash-surviving lifetime total persisted via `retain::Store`, a live
//! bales/hr rate, and two resets (immediate session reset + double-confirm
//! total reset). All time-dependent methods take `now_ms: u64` (monotonic
//! milliseconds) — time is injected so tests are free of wall-clock flakiness.

use crate::can::Command;

/// Debounce window: persist the retained total at most this long after the last
/// change (coalesces bale bursts). Low-frequency-only per ADR-0002.
pub const PERSIST_DEBOUNCE_MS: u64 = 2_000;
/// Double-confirm window for the lifetime-total reset (~2 s).
pub const RESET_TOTAL_WINDOW_MS: u64 = 2_000;
/// On-EEPROM schema version for [`BalerRetain`].
pub const RETAIN_VERSION: u8 = 1;

/// The sole retain `T` — the demo owns the whole EEPROM region (documented in
/// CONTEXT.md). Co-running with an app that stores config in retain would
/// clobber it.
#[derive(serde::Serialize, serde::Deserialize, Clone, PartialEq, Eq, Debug)]
pub struct BalerRetain {
    pub version: u8,
    pub total_bales: u32,
}

impl Default for BalerRetain {
    fn default() -> Self {
        Self {
            version: RETAIN_VERSION,
            total_bales: 0,
        }
    }
}

/// The bale counter model. Owns the session count, the lifetime total, the
/// session's bale timestamps (for bales/hr), the retain dirty-tracking, and the
/// total-reset double-confirm arm state.
pub struct Counter {
    session: u32,
    total: u32,
    /// Monotonic-ms timestamps of this session's bales, in arrival order. Drives
    /// the live bales/hr rate (issue 05); cleared on a session reset (issue 06).
    bale_times: Vec<u64>,
    /// Set when `total` differs from what's persisted in retain.
    dirty: bool,
    /// When the current dirty run started (ms) — drives the persist debounce.
    dirty_since: u64,
    /// `Some(ts)` while the total-reset double-confirm is armed (ts = the arming
    /// press time). Auto-expires `RESET_TOTAL_WINDOW_MS` after `ts`.
    reset_armed_at: Option<u64>,
}

/// Outcome of an F3 Reset Total press.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum ResetTotal {
    /// First press (or after expiry): armed, awaiting a confirming press.
    Armed,
    /// Second press within the window: the lifetime total was zeroed.
    Committed,
}

impl Counter {
    /// Build from the loaded retain blob: `total = blob.total_bales`, session 0,
    /// no session bale timestamps, not dirty, reset disarmed.
    pub fn from_retain(blob: &BalerRetain) -> Self {
        Self {
            session: 0,
            total: blob.total_bales,
            bale_times: Vec::new(),
            dirty: false,
            dirty_since: 0,
            reset_armed_at: None,
        }
    }

    pub fn session(&self) -> u32 {
        self.session
    }

    pub fn total(&self) -> u32 {
        self.total
    }

    // --- issue 04: bale event ---

    /// +1 Bale (sim): increment session + total, record the timestamp, mark the
    /// retained total dirty (`dirty_since = now_ms`), and return the BALE command
    /// carrying the new lifetime total.
    pub fn add_bale(&mut self, now_ms: u64) -> Command {
        self.session += 1;
        self.total += 1;
        self.bale_times.push(now_ms);
        self.mark_dirty(now_ms);
        Command::Bale(self.total)
    }

    /// Mark the retained total dirty as of `now_ms`. The debounce measures from
    /// this timestamp, so a fresh change re-arms the window — persistence lands
    /// at most `PERSIST_DEBOUNCE_MS` after the *last* change, coalescing bursts.
    fn mark_dirty(&mut self, now_ms: u64) {
        self.dirty = true;
        self.dirty_since = now_ms;
    }

    // --- issue 04: retain persistence (debounced + flush-on-exit) ---

    /// Any unpersisted total change pending.
    pub fn is_dirty(&self) -> bool {
        self.dirty
    }

    /// True once the debounce has elapsed since the last change: `dirty && now_ms
    /// - dirty_since >= PERSIST_DEBOUNCE_MS`. The coordinator polls this and, on
    /// `true`, writes `to_retain()` then calls `mark_persisted()`.
    pub fn needs_persist(&self, now_ms: u64) -> bool {
        self.dirty && now_ms.saturating_sub(self.dirty_since) >= PERSIST_DEBOUNCE_MS
    }

    /// Snapshot the retained blob: current schema version + lifetime total.
    pub fn to_retain(&self) -> BalerRetain {
        BalerRetain {
            version: RETAIN_VERSION,
            total_bales: self.total,
        }
    }

    /// Clear the dirty flag after a successful retain write.
    pub fn mark_persisted(&mut self) {
        self.dirty = false;
    }

    // --- issue 05: stats row ---

    /// Live bales/hr.
    ///
    /// WINDOW: the whole current session — the average rate since the FIRST
    /// session bale, extrapolated to an hour:
    ///
    /// ```text
    /// bales_per_hour = bale_count / ((now_ms - first_bale_ms) / 3_600_000)
    /// ```
    ///
    /// Returns `0.0` when there are no bales yet, or when no time has elapsed
    /// since the first bale (avoids division by zero). Chosen over a rolling
    /// window because the session is short-lived and operators want the steady
    /// running average for the field, not an instantaneous spike.
    pub fn bales_per_hour(&self, now_ms: u64) -> f32 {
        let Some(&first) = self.bale_times.first() else {
            return 0.0;
        };
        let elapsed_ms = now_ms.saturating_sub(first);
        if elapsed_ms == 0 {
            return 0.0;
        }
        let elapsed_hours = elapsed_ms as f32 / 3_600_000.0;
        self.bale_times.len() as f32 / elapsed_hours
    }

    /// Documented STATIC MOCK — the demo has no bale-diameter sensor, so this is
    /// a plausible placeholder value, clearly labelled in the UI.
    pub fn avg_diameter_m(&self) -> f32 {
        1.45
    }

    /// Documented STATIC MOCK — the demo has no net-wrap sensor, so this is a
    /// plausible placeholder percentage, clearly labelled in the UI.
    pub fn net_used_pct(&self) -> f32 {
        62.0
    }

    // --- issue 06: resets ---

    /// F1 Reset Session: zero the session count immediately and clear the
    /// session bale timestamps (so bales/hr restarts). The lifetime total and
    /// its retain dirty-state are untouched.
    pub fn reset_session(&mut self) {
        self.session = 0;
        self.bale_times.clear();
    }

    /// F3 Reset Total press. A first press (disarmed, or after the window has
    /// expired) arms the double-confirm and returns [`ResetTotal::Armed`]. A
    /// second press while still armed commits: zero the lifetime total, mark the
    /// retained total dirty, disarm — returning [`ResetTotal::Committed`].
    pub fn press_reset_total(&mut self, now_ms: u64) -> ResetTotal {
        if self.reset_total_armed(now_ms) {
            self.total = 0;
            self.mark_dirty(now_ms);
            self.reset_armed_at = None;
            ResetTotal::Committed
        } else {
            self.reset_armed_at = Some(now_ms);
            ResetTotal::Armed
        }
    }

    /// True only if the reset is armed AND still within the confirm window
    /// (auto-expires `RESET_TOTAL_WINDOW_MS` after the arming press).
    pub fn reset_total_armed(&self, now_ms: u64) -> bool {
        match self.reset_armed_at {
            Some(armed_at) => now_ms.saturating_sub(armed_at) < RESET_TOTAL_WINDOW_MS,
            None => false,
        }
    }

    /// Disarm the total-reset confirm (e.g. on leaving the Bale Counter screen).
    pub fn disarm_reset_total(&mut self) {
        self.reset_armed_at = None;
    }
}

impl Default for Counter {
    fn default() -> Self {
        Self::from_retain(&BalerRetain::default())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn approx(a: f32, b: f32) {
        assert!((a - b).abs() < 1e-3, "expected ~{b}, got {a}");
    }

    // --- issue 04: bale event + retain persistence ---

    #[test]
    fn from_retain_takes_total_from_blob_session_zero() {
        let c = Counter::from_retain(&BalerRetain {
            version: RETAIN_VERSION,
            total_bales: 137,
        });
        assert_eq!(c.total(), 137);
        assert_eq!(c.session(), 0);
    }

    #[test]
    fn add_bale_increments_both_returns_bale_command_and_dirties() {
        let mut c = Counter::from_retain(&BalerRetain {
            version: RETAIN_VERSION,
            total_bales: 10,
        });
        assert!(!c.is_dirty());
        let cmd = c.add_bale(1_000);
        assert_eq!(c.session(), 1);
        assert_eq!(c.total(), 11);
        assert_eq!(cmd, Command::Bale(11));
        assert!(c.is_dirty());
    }

    #[test]
    fn needs_persist_waits_for_debounce_then_mark_persisted_clears() {
        let mut c = Counter::default();
        // Not dirty yet → never needs persisting.
        assert!(!c.needs_persist(0));

        c.add_bale(1_000);
        // Immediately after the bale (now == dirty_since): not yet.
        assert!(!c.needs_persist(1_000));
        // One ms before the window closes: still not.
        assert!(!c.needs_persist(1_000 + PERSIST_DEBOUNCE_MS - 1));
        // Exactly at the window: now persist.
        assert!(c.needs_persist(1_000 + PERSIST_DEBOUNCE_MS));

        c.mark_persisted();
        assert!(!c.is_dirty());
        assert!(!c.needs_persist(1_000 + PERSIST_DEBOUNCE_MS));
    }

    #[test]
    fn to_retain_carries_version_and_current_total() {
        let mut c = Counter::from_retain(&BalerRetain {
            version: RETAIN_VERSION,
            total_bales: 5,
        });
        c.add_bale(0);
        c.add_bale(0);
        let blob = c.to_retain();
        assert_eq!(blob.version, RETAIN_VERSION);
        assert_eq!(blob.total_bales, 7);
    }

    /// A zero-filled temp file standing in for the 32 KB SPI retain EEPROM —
    /// mirrors the pattern in `cr1140-sdk/src/retain.rs` tests (no `tempfile`
    /// dependency, no device). Returns the path so the test can reopen it.
    fn fake_eeprom(tag: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("cr1140-baler-counter-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let p = dir.join(format!("{tag}.bin"));
        let f = std::fs::File::create(&p).unwrap();
        f.set_len(0x8000).unwrap();
        p
    }

    #[test]
    fn retain_round_trip_total_survives_reopen() {
        use cr1140_hal::sys::Nvmem;
        use cr1140_sdk::retain::Store;

        let path = fake_eeprom("roundtrip");

        // First "boot": seed an initial lifetime total, build the counter from
        // what loads back, add several bales, then persist the new total.
        {
            let store: Store<BalerRetain> = Store::open(Nvmem::open(&path).unwrap()).unwrap();
            store
                .save(&BalerRetain {
                    version: RETAIN_VERSION,
                    total_bales: 40,
                })
                .unwrap();

            let mut c = Counter::from_retain(&store.load_or_default().unwrap());
            assert_eq!(c.total(), 40);
            c.add_bale(0);
            c.add_bale(10);
            c.add_bale(20); // total 43
            store.save(&c.to_retain()).unwrap();
            c.mark_persisted();
        }

        // Second "boot" (reflash-equivalent: fresh Store over the same bytes):
        // the lifetime total survived, session starts back at 0.
        {
            let store: Store<BalerRetain> = Store::open(Nvmem::open(&path).unwrap()).unwrap();
            let blob = store.load_or_default().unwrap();
            assert_eq!(blob.total_bales, 43);
            let c = Counter::from_retain(&blob);
            assert_eq!(c.total(), 43);
            assert_eq!(c.session(), 0);
        }
    }

    // --- issue 05: stats row ---

    #[test]
    fn bales_per_hour_averages_since_first_bale() {
        let mut c = Counter::default();
        // No bales yet, and no time elapsed since (non-existent) first bale → 0.
        approx(c.bales_per_hour(0), 0.0);

        // 6 bales, first at t=0, each a minute apart (the last lands at 5 min).
        for i in 0..6 {
            c.add_bale(i * 60_000);
        }
        // Window = since the FIRST bale (t=0). At t=6 min: 6 bales / 0.1 h = 60.
        approx(c.bales_per_hour(360_000), 60.0);

        // Still computed from now_ms relative to the first bale: at 12 min the
        // same 6 bales average to 30/hr (rate updates as now_ms advances).
        approx(c.bales_per_hour(720_000), 30.0);
    }

    #[test]
    fn bales_per_hour_zero_when_no_time_since_first_bale() {
        let mut c = Counter::default();
        c.add_bale(5_000);
        // now == first bale time: no elapsed window → 0.0 (avoid div-by-zero).
        approx(c.bales_per_hour(5_000), 0.0);
    }

    #[test]
    fn mock_stats_return_documented_constants() {
        let c = Counter::default();
        approx(c.avg_diameter_m(), 1.45);
        approx(c.net_used_pct(), 62.0);
    }

    // --- issue 06: resets ---

    #[test]
    fn reset_session_zeroes_session_keeps_total_restarts_rate() {
        let mut c = Counter::from_retain(&BalerRetain {
            version: RETAIN_VERSION,
            total_bales: 100,
        });
        c.add_bale(0);
        c.add_bale(60_000);
        assert_eq!(c.session(), 2);
        // 2 bales, first at t=0, now 1 min: 2 / (1/60 h) = 120/hr.
        approx(c.bales_per_hour(60_000), 120.0);

        c.reset_session();
        assert_eq!(c.session(), 0); // session zeroed immediately
        assert_eq!(c.total(), 102); // lifetime total untouched
                                    // Session bale timestamps cleared, so bales/hr restarts from nothing.
        approx(c.bales_per_hour(120_000), 0.0);
    }

    #[test]
    fn press_reset_total_arms_then_commits_within_window() {
        let mut c = Counter::from_retain(&BalerRetain {
            version: RETAIN_VERSION,
            total_bales: 99,
        });
        assert!(!c.reset_total_armed(0));

        // First press: arms.
        assert_eq!(c.press_reset_total(0), ResetTotal::Armed);
        assert!(c.reset_total_armed(0));
        assert_eq!(c.total(), 99); // not yet wiped

        // Second press within the window: commits.
        assert_eq!(c.press_reset_total(1_000), ResetTotal::Committed);
        assert_eq!(c.total(), 0); // lifetime total wiped
        assert!(c.is_dirty()); // must be persisted to retain
        assert!(!c.reset_total_armed(1_000)); // disarmed after commit
    }

    #[test]
    fn reset_total_arm_auto_expires_and_re_arms_after_window() {
        let mut c = Counter::from_retain(&BalerRetain {
            version: RETAIN_VERSION,
            total_bales: 50,
        });
        assert_eq!(c.press_reset_total(0), ResetTotal::Armed);

        // Within the window: still armed.
        assert!(c.reset_total_armed(RESET_TOTAL_WINDOW_MS - 1));
        // Past the window: auto-expired.
        assert!(!c.reset_total_armed(RESET_TOTAL_WINDOW_MS + 1));

        // A press after expiry re-arms (does NOT commit) and leaves total intact.
        assert_eq!(
            c.press_reset_total(RESET_TOTAL_WINDOW_MS + 1),
            ResetTotal::Armed
        );
        assert_eq!(c.total(), 50);
    }

    #[test]
    fn disarm_reset_total_clears_armed_state() {
        let mut c = Counter::from_retain(&BalerRetain {
            version: RETAIN_VERSION,
            total_bales: 7,
        });
        c.press_reset_total(0);
        assert!(c.reset_total_armed(0));

        c.disarm_reset_total();
        assert!(!c.reset_total_armed(0));
        // After disarming, the next press arms afresh rather than committing.
        assert_eq!(c.press_reset_total(100), ResetTotal::Armed);
        assert_eq!(c.total(), 7);
    }
}
