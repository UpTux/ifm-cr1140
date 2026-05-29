// SPDX-License-Identifier: GPL-3.0-only
//! Generic Linux system telemetry from `/proc`. Not CR1140-specific — works on
//! any Linux host. Pure parsers (host-testable) plus thin procfs readers.

use std::fs;
use cr1140_hal::sys::{read_temp_c, SOC_THERMAL_ZONE};

/// CPU utilisation from `/proc/stat`, computed as the busy fraction between two
/// samples. The first sample primes the baseline and reports 0%.
pub struct CpuSampler {
    prev_idle: u64,
    prev_total: u64,
    primed: bool,
}

impl CpuSampler {
    pub fn new() -> Self {
        Self { prev_idle: 0, prev_total: 0, primed: false }
    }

    /// Read `/proc/stat` and return CPU usage % since the previous call.
    pub fn sample(&mut self) -> Option<f32> {
        let s = fs::read_to_string("/proc/stat").ok()?;
        let (idle, total) = parse_stat(&s)?;
        let pct = if self.primed && total > self.prev_total {
            let di = idle.saturating_sub(self.prev_idle);
            let dt = total - self.prev_total;
            (1.0 - di as f32 / dt as f32) * 100.0
        } else {
            0.0
        };
        self.prev_idle = idle;
        self.prev_total = total;
        self.primed = true;
        Some(pct.clamp(0.0, 100.0))
    }
}

impl Default for CpuSampler {
    fn default() -> Self {
        Self::new()
    }
}

/// Parse the aggregate `cpu` line of `/proc/stat` into `(idle, total)` jiffies.
/// `idle` includes iowait, matching the conventional `top`-style calculation.
pub fn parse_stat(content: &str) -> Option<(u64, u64)> {
    let line = content.lines().next()?;
    let mut it = line.split_whitespace();
    if it.next()? != "cpu" {
        return None;
    }
    let vals: Vec<u64> = it.filter_map(|t| t.parse().ok()).collect();
    if vals.len() < 4 {
        return None;
    }
    let idle = vals[3] + vals.get(4).copied().unwrap_or(0); // idle + iowait
    let total: u64 = vals.iter().sum();
    Some((idle, total))
}

/// Read `(MemTotal, MemAvailable)` in kB from `/proc/meminfo`.
pub fn parse_meminfo(content: &str) -> Option<(u64, u64)> {
    let mut total = None;
    let mut avail = None;
    for l in content.lines() {
        if let Some(v) = l.strip_prefix("MemTotal:") {
            total = v.split_whitespace().next().and_then(|n| n.parse().ok());
        } else if let Some(v) = l.strip_prefix("MemAvailable:") {
            avail = v.split_whitespace().next().and_then(|n| n.parse().ok());
        }
    }
    Some((total?, avail?))
}

/// Read `(MemTotal, MemAvailable)` in kB straight from `/proc/meminfo`.
pub fn read_meminfo() -> Option<(u64, u64)> {
    parse_meminfo(&fs::read_to_string("/proc/meminfo").ok()?)
}

pub fn mem_used_percent(total: u64, avail: u64) -> f32 {
    if total == 0 {
        0.0
    } else {
        ((1.0 - avail as f32 / total as f32) * 100.0).clamp(0.0, 100.0)
    }
}

/// First field of `/proc/uptime` = seconds since boot.
pub fn parse_uptime(content: &str) -> Option<f64> {
    content.split_whitespace().next()?.parse().ok()
}

/// Read seconds since boot straight from `/proc/uptime`.
pub fn read_uptime() -> Option<f64> {
    parse_uptime(&fs::read_to_string("/proc/uptime").ok()?)
}

pub fn format_uptime(secs: f64) -> String {
    let s = secs as u64;
    let (h, m, sec) = (s / 3600, (s % 3600) / 60, s % 60);
    if h > 0 {
        format!("{h}h {m:02}m {sec:02}s")
    } else {
        format!("{m}m {sec:02}s")
    }
}

/// First field of `/proc/loadavg` = the 1-minute load average.
pub fn parse_loadavg(content: &str) -> Option<f32> {
    content.split_whitespace().next()?.parse().ok()
}

pub fn read_loadavg() -> Option<f32> {
    parse_loadavg(&fs::read_to_string("/proc/loadavg").ok()?)
}

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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_stat_sums_and_idles() {
        // cpu  user nice system idle iowait irq softirq
        let s = "cpu  100 10 40 800 50 0 0\ncpu0 1 2 3 4\n";
        let (idle, total) = parse_stat(s).unwrap();
        assert_eq!(idle, 800 + 50);
        assert_eq!(total, 100 + 10 + 40 + 800 + 50);
    }

    #[test]
    fn parse_stat_rejects_non_cpu() {
        assert_eq!(parse_stat("intr 1 2 3\n"), None);
    }

    #[test]
    fn parse_meminfo_extracts_total_and_avail() {
        let s = "MemTotal:        1019600 kB\nMemFree:  200000 kB\nMemAvailable:   509800 kB\n";
        assert_eq!(parse_meminfo(s), Some((1019600, 509800)));
    }

    #[test]
    fn mem_used_percent_is_complement_of_available() {
        assert!((mem_used_percent(1000, 250) - 75.0).abs() < 0.001);
        assert_eq!(mem_used_percent(0, 0), 0.0);
    }

    #[test]
    fn parse_uptime_reads_first_field() {
        assert_eq!(parse_uptime("12345.67 98765.43\n"), Some(12345.67));
    }

    #[test]
    fn format_uptime_switches_on_hours() {
        assert_eq!(format_uptime(65.0), "1m 05s");
        assert_eq!(format_uptime(3725.0), "1h 02m 05s");
    }

    #[test]
    fn parse_loadavg_reads_first_field() {
        assert_eq!(parse_loadavg("0.17 0.08 0.08 1/99 12009"), Some(0.17));
        assert_eq!(parse_loadavg(""), None);
    }

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
}
