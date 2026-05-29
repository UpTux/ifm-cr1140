//! Keypad-LED animation modes. Each mode is a pure brightness curve over time,
//! so the render loop can sample `level(t)` every frame and scale the chosen
//! RGB color by it. Pure → unit-testable on the host.

use std::f32::consts::TAU;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum LedMode {
    Solid,     // F1: always on, full brightness
    Dim,       // F2: always on, 50%
    Pulse,     // F3: smooth breathe
    Blink,     // F4: 1 Hz on/off
    Flash,     // F5: fast strobe
    Heartbeat, // F6: double-beat
}

impl LedMode {
    pub fn name(&self) -> &'static str {
        match self {
            LedMode::Solid => "solid",
            LedMode::Dim => "50%",
            LedMode::Pulse => "pulse",
            LedMode::Blink => "blink",
            LedMode::Flash => "flash",
            LedMode::Heartbeat => "heartbeat",
        }
    }

    /// Brightness multiplier in `0.0..=1.0` at `t` seconds since the mode began.
    pub fn level(&self, t: f32) -> f32 {
        match self {
            LedMode::Solid => 1.0,
            LedMode::Dim => 0.5,
            // 2 s breathe; starts at 0, peaks at 1 at t=1s.
            LedMode::Pulse => 0.5 - 0.5 * (t * TAU / 2.0).cos(),
            // 1 Hz square wave.
            LedMode::Blink => {
                if (t % 1.0) < 0.5 {
                    1.0
                } else {
                    0.0
                }
            }
            // ~4 Hz strobe: short on, longer off.
            LedMode::Flash => {
                if (t % 0.25) < 0.1 {
                    1.0
                } else {
                    0.0
                }
            }
            // Two quick beats then a pause, ~1.2 s period.
            LedMode::Heartbeat => {
                let p = t % 1.2;
                if p < 0.12 || (0.22..0.34).contains(&p) {
                    1.0
                } else {
                    0.0
                }
            }
        }
    }
}

/// Scale a base RGB color by a `0.0..=1.0` brightness level into channel bytes.
pub fn scale(rgb: (u8, u8, u8), level: f32) -> (u8, u8, u8) {
    let l = level.clamp(0.0, 1.0);
    let s = |c: u8| (c as f32 * l).round() as u8;
    (s(rgb.0), s(rgb.1), s(rgb.2))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn solid_and_dim_are_constant() {
        assert_eq!(LedMode::Solid.level(0.0), 1.0);
        assert_eq!(LedMode::Solid.level(99.0), 1.0);
        assert_eq!(LedMode::Dim.level(0.0), 0.5);
        assert_eq!(LedMode::Dim.level(99.0), 0.5);
    }

    #[test]
    fn pulse_breathes_from_zero_to_one() {
        assert!(LedMode::Pulse.level(0.0).abs() < 0.001, "starts dark");
        assert!((LedMode::Pulse.level(1.0) - 1.0).abs() < 0.001, "peaks mid-cycle");
        assert!(LedMode::Pulse.level(2.0).abs() < 0.001, "dark again after 2s");
    }

    #[test]
    fn blink_is_1hz_square() {
        assert_eq!(LedMode::Blink.level(0.0), 1.0);
        assert_eq!(LedMode::Blink.level(0.4), 1.0);
        assert_eq!(LedMode::Blink.level(0.6), 0.0);
        assert_eq!(LedMode::Blink.level(1.1), 1.0); // wraps each second
    }

    #[test]
    fn flash_strobes_on_briefly() {
        assert_eq!(LedMode::Flash.level(0.0), 1.0);
        assert_eq!(LedMode::Flash.level(0.05), 1.0);
        assert_eq!(LedMode::Flash.level(0.15), 0.0);
    }

    #[test]
    fn heartbeat_has_two_beats() {
        assert_eq!(LedMode::Heartbeat.level(0.0), 1.0); // beat 1
        assert_eq!(LedMode::Heartbeat.level(0.16), 0.0); // gap
        assert_eq!(LedMode::Heartbeat.level(0.25), 1.0); // beat 2
        assert_eq!(LedMode::Heartbeat.level(0.7), 0.0); // long pause
    }

    #[test]
    fn scale_multiplies_and_clamps() {
        assert_eq!(scale((255, 100, 0), 0.5), (128, 50, 0));
        assert_eq!(scale((255, 255, 255), 0.0), (0, 0, 0));
        assert_eq!(scale((10, 20, 30), 2.0), (10, 20, 30)); // clamps to 1.0
    }
}
