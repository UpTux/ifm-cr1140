// SPDX-License-Identifier: GPL-3.0-only
//! CAN command seam (issue 03): a single outbound path for baler commands.
//!
//! [`encode`] is the pure, host-testable core — it maps a [`Command`] to an
//! [`EncodedFrame`] (11-bit standard id + payload bytes). [`BalerBus`] is the
//! send-or-log seam: it opens a real SocketCAN interface when one is present
//! and otherwise logs the frame it *would* have sent ("mock" writes).
//!
//! ## Placeholder message map (replace with the real baler DBC/J1939)
//!
//! These IDs and payloads are demo placeholders — not a production map.
//!
//! | Signal | ID      | Payload                          |
//! |--------|---------|----------------------------------|
//! | Knives | `0x200` | `[0]` = 0 out / 1 in             |
//! | Wrap   | `0x201` | `[0]` = 1 start                  |
//! | Bale   | `0x202` | `[0..4]` = total count, LE `u32` |
//!
//! `cr1140_hal::can::CanBus` is Linux-only (SocketCAN), so the real backend is
//! `#[cfg(target_os = "linux")]`-gated; the logging fallback is always present
//! and is the only reachable path on non-Linux hosts.

/// Outbound baler commands. PLACEHOLDER message map — replace with the real
/// baler DBC/J1939 later (documented in a comment).
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Command {
    Knives(bool),
    WrapStart,
    Bale(u32),
}

// Placeholder 11-bit standard IDs.
pub const KNIVES_ID: u16 = 0x200;
pub const WRAP_ID: u16 = 0x201;
pub const BALE_ID: u16 = 0x202;

#[derive(Clone, PartialEq, Eq, Debug)]
pub struct EncodedFrame {
    pub id: u16,
    pub data: Vec<u8>,
}

/// Pure encoder — host-testable, no I/O.
pub fn encode(cmd: &Command) -> EncodedFrame {
    match cmd {
        Command::Knives(in_) => EncodedFrame {
            id: KNIVES_ID,
            data: vec![u8::from(*in_)],
        },
        Command::WrapStart => EncodedFrame {
            id: WRAP_ID,
            data: vec![1],
        },
        Command::Bale(total) => EncodedFrame {
            id: BALE_ID,
            data: total.to_le_bytes().to_vec(),
        },
    }
}

/// Render an [`EncodedFrame`] as a human-readable string: id in hex, bytes in
/// hex — so a log line shows exactly what would have gone out on the wire.
fn fmt_frame(frame: &EncodedFrame) -> String {
    let bytes: Vec<String> = frame.data.iter().map(|b| format!("{b:02X}")).collect();
    format!("id=0x{:03X} data=[{}]", frame.id, bytes.join(" "))
}

/// Backend behind [`BalerBus`]. The `Logging` variant is always available and
/// is the only reachable variant on non-Linux hosts; the real SocketCAN backend
/// is Linux-only because `cr1140_hal::can::CanBus` is `#[cfg(target_os = "linux")]`.
enum Backend {
    /// No real bus — log the frame we would have sent.
    Logging,
    /// A bound SocketCAN interface (real frames via `send_std`).
    #[cfg(target_os = "linux")]
    Real(cr1140_hal::can::CanBus),
}

/// The send-or-log seam. Tries a real SocketCAN interface at construction;
/// falls back to logging the frame it *would* have sent.
pub struct BalerBus {
    backend: Backend,
}

impl BalerBus {
    /// Try to open `iface` (e.g. "can0"); on failure (or non-Linux host),
    /// construct a logging bus. Logs at info/warn which mode it is in.
    #[cfg(target_os = "linux")]
    pub fn open(iface: &str) -> Self {
        match cr1140_hal::can::CanBus::open(iface) {
            Ok(bus) => {
                tracing::info!(iface, "baler CAN: opened real SocketCAN interface");
                Self {
                    backend: Backend::Real(bus),
                }
            }
            Err(err) => {
                tracing::warn!(
                    iface,
                    %err,
                    "baler CAN: interface unavailable — falling back to logging frames"
                );
                Self {
                    backend: Backend::Logging,
                }
            }
        }
    }

    /// Try to open `iface`; on this non-Linux host there is no SocketCAN, so
    /// always construct a logging bus.
    #[cfg(not(target_os = "linux"))]
    pub fn open(iface: &str) -> Self {
        tracing::warn!(
            iface,
            "baler CAN: not Linux — no SocketCAN, logging frames instead"
        );
        Self {
            backend: Backend::Logging,
        }
    }

    /// Encode `cmd` and either send the real frame or log it via `tracing`.
    /// Never panics, never blocks the UI; a per-send error logs and is swallowed.
    pub fn send(&self, cmd: &Command) {
        let frame = encode(cmd);
        match &self.backend {
            #[cfg(target_os = "linux")]
            Backend::Real(bus) => {
                if let Err(err) = bus.send_std(frame.id, &frame.data) {
                    // Swallow — a transient bus error must not crash or block the UI.
                    tracing::warn!(%err, frame = %fmt_frame(&frame), "baler CAN: send failed");
                }
            }
            Backend::Logging => {
                tracing::info!(frame = %fmt_frame(&frame), "baler CAN: would send");
            }
        }
    }

    /// Test-only: is this bus the logging fallback (vs. a real socket)?
    #[cfg(test)]
    fn is_logging(&self) -> bool {
        matches!(self.backend, Backend::Logging)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encode_knives_in() {
        assert_eq!(
            encode(&Command::Knives(true)),
            EncodedFrame {
                id: KNIVES_ID,
                data: vec![1]
            }
        );
    }

    #[test]
    fn encode_knives_out() {
        assert_eq!(
            encode(&Command::Knives(false)),
            EncodedFrame {
                id: KNIVES_ID,
                data: vec![0]
            }
        );
    }

    #[test]
    fn encode_wrap_start() {
        assert_eq!(
            encode(&Command::WrapStart),
            EncodedFrame {
                id: WRAP_ID,
                data: vec![1]
            }
        );
    }

    #[test]
    fn encode_bale_total_le_u32() {
        // 0x04030201 → little-endian bytes [01, 02, 03, 04] (distinguishes order).
        assert_eq!(
            encode(&Command::Bale(0x0403_0201)),
            EncodedFrame {
                id: BALE_ID,
                data: vec![0x01, 0x02, 0x03, 0x04]
            }
        );
    }

    #[test]
    fn open_bad_iface_is_logging_bus_and_send_does_not_panic() {
        // No such interface exists (and on non-Linux there is no real bus at
        // all), so this must construct the logging fallback.
        let bus = BalerBus::open("definitely-not-a-real-iface");
        assert!(bus.is_logging());
        // Each command must route through send without panicking.
        bus.send(&Command::Knives(true));
        bus.send(&Command::Knives(false));
        bus.send(&Command::WrapStart);
        bus.send(&Command::Bale(42));
    }
}
