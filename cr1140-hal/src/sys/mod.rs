// SPDX-License-Identifier: GPL-3.0-only
//! System: LEDs, backlight, temperature via sysfs.
pub mod parse;

use crate::error::{HalError, HalResult};
use crate::sys::parse::{parse_brightness, parse_millidegrees};
use std::fs;
use std::path::Path;

/// The display backlight node under `/sys/class/backlight/` on the CR1140.
pub const BACKLIGHT: &str = "backlight";
/// `max_brightness` of [`BACKLIGHT`] on the CR1140. Prefer reading it at runtime
/// with [`backlight_max`]; this is the documented hardware value for a default.
pub const BACKLIGHT_MAX_HINT: u32 = 400;
/// Thermal zone backing the SoC temperature (`/sys/class/thermal/thermal_zone0`).
pub const SOC_THERMAL_ZONE: u32 = 0;

/// The CR1140's onboard LEDs. The three `*:status` LEDs are binary
/// (`max == 1`); the three `*:kbd_backlight` channels are 8-bit PWM
/// (`max == 255`), so only the keypad channels give a visible brightness ramp.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Led {
    StatusRed,
    StatusGreen,
    StatusBlue,
    KbdRed,
    KbdGreen,
    KbdBlue,
}

impl Led {
    /// The sysfs leaf name under `/sys/class/leds/`.
    pub fn name(self) -> &'static str {
        match self {
            Led::StatusRed => "red:status",
            Led::StatusGreen => "green:status",
            Led::StatusBlue => "blue:status",
            Led::KbdRed => "red:kbd_backlight",
            Led::KbdGreen => "green:kbd_backlight",
            Led::KbdBlue => "blue:kbd_backlight",
        }
    }

    /// `max_brightness` for this LED: 1 for the binary status LEDs, 255 for the
    /// PWM keypad-backlight channels.
    pub fn max(self) -> u32 {
        match self {
            Led::StatusRed | Led::StatusGreen | Led::StatusBlue => 1,
            Led::KbdRed | Led::KbdGreen | Led::KbdBlue => 255,
        }
    }
}

/// Set an LED brightness via `/sys/class/leds/<name>/brightness`.
pub fn set_led(name: &str, value: u32) -> HalResult<()> {
    fs::write(format!("/sys/class/leds/{name}/brightness"), value.to_string())?;
    Ok(())
}

/// Read an LED's current brightness (for save/restore).
pub fn read_led(name: &str) -> HalResult<u32> {
    let s = fs::read_to_string(format!("/sys/class/leds/{name}/brightness"))?;
    parse_brightness(&s).ok_or_else(|| HalError::Parse(format!("led {name} brightness: {s:?}")))
}

/// Set a typed [`Led`], clamping the value to that LED's [`Led::max`].
pub fn set_led_typed(led: Led, value: u32) -> HalResult<()> {
    set_led(led.name(), value.min(led.max()))
}

/// Set the RGB keypad backlight color. The CR1140 keypad LED is three PWM
/// channels (`red`/`green`/`blue:kbd_backlight`, each 0–255), so any color is a
/// mix: e.g. yellow = (255,255,0), orange = (255,90,0), off = (0,0,0).
pub fn set_kbd_backlight(r: u8, g: u8, b: u8) -> HalResult<()> {
    set_led(Led::KbdRed.name(), r as u32)?;
    set_led(Led::KbdGreen.name(), g as u32)?;
    set_led(Led::KbdBlue.name(), b as u32)?;
    Ok(())
}

/// Set display backlight via `/sys/class/backlight/<name>/brightness`.
pub fn set_backlight(name: &str, value: u32) -> HalResult<()> {
    fs::write(
        format!("/sys/class/backlight/{name}/brightness"),
        value.to_string(),
    )?;
    Ok(())
}

/// Read the backlight's current brightness (for save/restore).
pub fn read_backlight(name: &str) -> HalResult<u32> {
    let s = fs::read_to_string(format!("/sys/class/backlight/{name}/brightness"))?;
    parse_brightness(&s).ok_or_else(|| HalError::Parse(format!("backlight {name}: {s:?}")))
}

/// Read a thermal zone temperature in °C.
pub fn read_temp_c(zone: u32) -> HalResult<f32> {
    let s = fs::read_to_string(format!("/sys/class/thermal/thermal_zone{zone}/temp"))?;
    parse_millidegrees(&s).ok_or_else(|| HalError::Parse(format!("thermal_zone{zone}: {s:?}")))
}

/// Read max brightness for a backlight (for scaling).
pub fn backlight_max(name: &str) -> HalResult<u32> {
    let s = fs::read_to_string(format!("/sys/class/backlight/{name}/max_brightness"))?;
    parse_brightness(&s)
        .ok_or_else(|| HalError::Parse(format!("backlight {name} max_brightness: {s:?}")))
}

/// Available LED leaf names under `/sys/class/leds/`.
pub fn list_leds() -> HalResult<Vec<String>> {
    dir_entry_names("/sys/class/leds")
}

/// Available backlight names under `/sys/class/backlight/`.
pub fn list_backlights() -> HalResult<Vec<String>> {
    dir_entry_names("/sys/class/backlight")
}

/// Available thermal-zone numbers (the `N` in `thermal_zoneN`).
pub fn list_thermal_zones() -> HalResult<Vec<u32>> {
    Ok(dir_entry_names("/sys/class/thermal")?
        .iter()
        .filter_map(|n| parse_thermal_zone_num(n))
        .collect())
}

/// Sorted leaf names of a directory's entries.
fn dir_entry_names<P: AsRef<Path>>(dir: P) -> HalResult<Vec<String>> {
    let mut names = Vec::new();
    for entry in fs::read_dir(dir)? {
        if let Some(name) = entry?.file_name().to_str() {
            names.push(name.to_string());
        }
    }
    names.sort();
    Ok(names)
}

/// Parse `"thermal_zoneN"` → `N`; anything else → `None`.
fn parse_thermal_zone_num(name: &str) -> Option<u32> {
    name.strip_prefix("thermal_zone")?.parse().ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn led_names_match_sysfs_leaves() {
        assert_eq!(Led::StatusRed.name(), "red:status");
        assert_eq!(Led::StatusBlue.name(), "blue:status");
        assert_eq!(Led::KbdRed.name(), "red:kbd_backlight");
        assert_eq!(Led::KbdBlue.name(), "blue:kbd_backlight");
    }

    #[test]
    fn led_max_is_1_for_status_255_for_kbd() {
        assert_eq!(Led::StatusGreen.max(), 1);
        assert_eq!(Led::KbdGreen.max(), 255);
    }

    #[test]
    fn thermal_zone_num_parses_zonen_only() {
        assert_eq!(parse_thermal_zone_num("thermal_zone0"), Some(0));
        assert_eq!(parse_thermal_zone_num("thermal_zone12"), Some(12));
        assert_eq!(parse_thermal_zone_num("cooling_device0"), None);
        assert_eq!(parse_thermal_zone_num("thermal_zone"), None);
    }

    #[test]
    fn dir_entry_names_lists_sorted_children() {
        let dir = std::env::temp_dir().join(format!("cr1140-sys-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(dir.join("green:status")).unwrap();
        fs::create_dir_all(dir.join("red:status")).unwrap();
        let mut names = dir_entry_names(&dir).unwrap();
        names.sort();
        assert_eq!(names, vec!["green:status".to_string(), "red:status".to_string()]);
        let _ = fs::remove_dir_all(&dir);
    }
}
