// SPDX-License-Identifier: GPL-3.0-only
//! CR1140 SDK — app-building conveniences layered on top of `cr1140-hal`.
//!
//! This crate is deliberately UI-framework agnostic (no Slint, no rendering): it
//! provides the "batteries" a native CR1140 application needs regardless of how
//! it draws — keypad LED effects, system telemetry, and device/network info.
//!
//! - [`led`] — RGB keypad-LED animation modes and a [`led::LedDriver`].
//! - [`metrics`] — generic Linux telemetry (CPU, memory, load, uptime) plus a
//!   [`metrics::Telemetry`] collector returning an aggregated [`metrics::Snapshot`].
//! - [`device`] — device & OS identity and network state.
//! - [`guard`] — [`guard::ShutdownGuard`]: restore backlight/LED on exit (RAII),
//!   with opt-in SIGINT/SIGTERM handling for standalone binaries.
//! - [`config`] — atomic [`config::Store`] persistence for the p2 overlay
//!   (`/home/cds-apps`); enabled by the default `config` feature.
//! - [`retain`] — reflash-surviving [`retain::Store`] on the SPI EEPROM (A/B +
//!   CRC32, `postcard`); enabled by the default `retain` feature.
//! - [`net`] — host network-config apply via `nmcli` ([`net::apply`]); off by
//!   default behind the `net` feature.
//!
//! Errors from fallible operations surface as [`SdkError`]. This crate is a guest
//! under host executors (ROS 2 / Apex / Taktora): it logs through the `tracing`
//! facade without installing a subscriber, and never grabs signals by default.

#[cfg(feature = "config")]
pub mod config;
pub mod device;
pub mod error;
pub mod guard;
pub mod led;
pub mod metrics;
#[cfg(feature = "net")]
pub mod net;
#[cfg(feature = "retain")]
pub mod retain;

pub use error::{SdkError, SdkResult};
#[cfg(feature = "config")]
pub use config::{Store, DEFAULT_APP_DIR};
pub use guard::{ShutdownFlag, ShutdownGuard};
pub use metrics::{MemInfo, Snapshot, Telemetry};
#[cfg(feature = "retain")]
pub use retain::Store as RetainStore;
