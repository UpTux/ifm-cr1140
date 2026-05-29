//! System: LEDs, backlight, temperature via sysfs.
pub mod parse;

use crate::sys::parse::{parse_brightness, parse_millidegrees};
use std::fs;

/// Set an LED brightness via `/sys/class/leds/<name>/brightness`.
pub fn set_led(name: &str, value: u32) -> std::io::Result<()> {
    fs::write(format!("/sys/class/leds/{name}/brightness"), value.to_string())
}

/// Set display backlight via `/sys/class/backlight/<name>/brightness`.
pub fn set_backlight(name: &str, value: u32) -> std::io::Result<()> {
    fs::write(
        format!("/sys/class/backlight/{name}/brightness"),
        value.to_string(),
    )
}

/// Read a thermal zone temperature in °C.
pub fn read_temp_c(zone: u32) -> std::io::Result<f32> {
    let s = fs::read_to_string(format!("/sys/class/thermal/thermal_zone{zone}/temp"))?;
    parse_millidegrees(&s)
        .ok_or_else(|| std::io::Error::new(std::io::ErrorKind::InvalidData, "bad temp value"))
}

/// Read max brightness for a backlight (for scaling).
pub fn backlight_max(name: &str) -> std::io::Result<u32> {
    let s = fs::read_to_string(format!("/sys/class/backlight/{name}/max_brightness"))?;
    parse_brightness(&s)
        .ok_or_else(|| std::io::Error::new(std::io::ErrorKind::InvalidData, "bad brightness value"))
}
