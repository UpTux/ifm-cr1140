// SPDX-License-Identifier: GPL-3.0-only
//! CAN access via SocketCAN (Linux-only; the module is empty on other hosts).

#[cfg(target_os = "linux")]
mod imp {
    use socketcan::{CanFrame, CanSocket, EmbeddedFrame, Id, Socket, StandardId};

    /// A bound SocketCAN interface.
    pub struct CanBus {
        sock: CanSocket,
    }

    impl CanBus {
        /// Open an existing SocketCAN interface (e.g. "can0"). The interface must
        /// already be up (`ip link set can0 up type can bitrate 250000`).
        pub fn open(iface: &str) -> std::io::Result<Self> {
            let sock = CanSocket::open(iface)?;
            Ok(Self { sock })
        }

        /// Send a standard (11-bit) frame.
        pub fn send_std(&self, id: u16, data: &[u8]) -> std::io::Result<()> {
            let sid = StandardId::new(id).ok_or_else(|| {
                std::io::Error::new(std::io::ErrorKind::InvalidInput, "id out of 11-bit range")
            })?;
            let frame = CanFrame::new(sid, data).ok_or_else(|| {
                std::io::Error::new(std::io::ErrorKind::InvalidInput, "data too long")
            })?;
            self.sock.write_frame(&frame)?;
            Ok(())
        }

        /// Blocking receive of the next frame; returns (id, data bytes).
        pub fn recv(&self) -> std::io::Result<(u32, Vec<u8>)> {
            let frame = self.sock.read_frame()?;
            let id = match frame.id() {
                Id::Standard(s) => s.as_raw() as u32,
                Id::Extended(e) => e.as_raw(),
            };
            Ok((id, frame.data().to_vec()))
        }
    }
}

#[cfg(target_os = "linux")]
pub use imp::CanBus;
