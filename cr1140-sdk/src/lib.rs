// SPDX-License-Identifier: GPL-3.0-only
//! CR1140 SDK — app-building conveniences layered on top of `cr1140-hal`.
//!
//! This crate is deliberately UI-framework agnostic (no Slint, no rendering): it
//! provides the "batteries" a native CR1140 application needs regardless of how
//! it draws — keypad LED effects, system telemetry, and device/network info.
//!
//! - [`led`] — RGB keypad-LED animation modes and a [`led::LedDriver`].
//! - [`metrics`] — generic Linux telemetry (CPU, memory, load, uptime).
//! - [`device`] — device & OS identity and network state.

pub mod device;
pub mod error;
pub mod guard;
pub mod led;
pub mod metrics;

pub use error::{SdkError, SdkResult};
pub use metrics::{MemInfo, Snapshot, Telemetry};
