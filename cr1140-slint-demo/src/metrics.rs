//! Live device metrics from `/proc`. Pure parsers (host-testable) plus thin
//! sysfs/procfs readers used by the render loop.

use std::fs;

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

pub fn format_uptime(secs: f64) -> String {
    let s = secs as u64;
    let (h, m, sec) = (s / 3600, (s % 3600) / 60, s % 60);
    if h > 0 {
        format!("{h}h {m:02}m {sec:02}s")
    } else {
        format!("{m}m {sec:02}s")
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
}
