/// Parse a sysfs thermal_zone temp file ("42000\n" millidegrees) to °C.
pub fn parse_millidegrees(s: &str) -> Option<f32> {
    s.trim().parse::<i32>().ok().map(|m| m as f32 / 1000.0)
}

/// Parse a sysfs brightness/max_brightness integer ("255\n").
pub fn parse_brightness(s: &str) -> Option<u32> {
    s.trim().parse::<u32>().ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn millidegrees_to_celsius() {
        assert_eq!(parse_millidegrees("42000\n"), Some(42.0));
        assert_eq!(parse_millidegrees("  -5500 "), Some(-5.5));
        assert_eq!(parse_millidegrees("nan"), None);
    }

    #[test]
    fn brightness_parses_trimmed_int() {
        assert_eq!(parse_brightness("255\n"), Some(255));
        assert_eq!(parse_brightness(""), None);
    }
}
