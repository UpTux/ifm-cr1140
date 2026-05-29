//! Hardware abstraction layer for the ifm CR1140/CR1141 (aarch64, Yocto Linux).
//!
//! Wraps stock Linux ABIs the device exposes: fbdev (display), evdev (buttons),
//! SocketCAN (CAN), and sysfs (LEDs/backlight/temperature).
pub mod display;
pub mod input;
pub mod can;
pub mod sys;
