//! Hardware abstraction layer for the ifm CR1140/CR1141 (aarch64, Yocto Linux).
//!
//! Wraps stock Linux ABIs the device exposes: fbdev (display), evdev (buttons),
//! SocketCAN (CAN), and sysfs (LEDs/backlight/temperature).
//!
//! Most fallible calls return [`HalResult`], so callers can match on the
//! [`HalError`] cause (missing device, unsupported format, out-of-range value)
//! rather than string-matching an [`std::io::Error`].
pub mod error;
pub use error::{HalError, HalResult};

pub mod display;
pub mod input;
pub mod can;
pub mod sys;

pub mod prelude;
