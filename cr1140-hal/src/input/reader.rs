use crate::input::event::{to_button_event, ButtonEvent, InputEvent};
use std::fs::{File, OpenOptions};
use std::io::{ErrorKind, Read};
use std::os::unix::fs::OpenOptionsExt;

/// Reader of logical button events from an evdev node (`/dev/input/eventN`).
///
/// Open with [`ButtonReader::open`] for blocking reads ([`next_button`]), or
/// [`ButtonReader::open_nonblocking`] for a render-loop-friendly
/// [`poll_button`] that never blocks.
///
/// [`next_button`]: ButtonReader::next_button
/// [`poll_button`]: ButtonReader::poll_button
pub struct ButtonReader {
    file: File,
}

impl ButtonReader {
    /// Open in blocking mode.
    pub fn open(path: &str) -> std::io::Result<Self> {
        Ok(Self { file: File::open(path)? })
    }

    /// Open in non-blocking mode (`O_NONBLOCK`) for use with [`poll_button`].
    ///
    /// [`poll_button`]: ButtonReader::poll_button
    pub fn open_nonblocking(path: &str) -> std::io::Result<Self> {
        let file = OpenOptions::new()
            .read(true)
            .custom_flags(nix::libc::O_NONBLOCK)
            .open(path)?;
        Ok(Self { file })
    }

    /// Blocking: returns the next logical button event, skipping non-button records.
    pub fn next_button(&mut self) -> std::io::Result<ButtonEvent> {
        let mut buf = [0u8; InputEvent::SIZE];
        loop {
            self.file.read_exact(&mut buf)?;
            if let Some(ev) = InputEvent::decode(&buf) {
                if let Some(be) = to_button_event(ev) {
                    return Ok(be);
                }
            }
        }
    }

    /// Non-blocking: returns the next button event if one is queued, or `None`
    /// if there is nothing to read right now. Requires [`open_nonblocking`].
    /// Non-button records (e.g. SYN) queued ahead of a button are drained.
    ///
    /// [`open_nonblocking`]: ButtonReader::open_nonblocking
    pub fn poll_button(&mut self) -> std::io::Result<Option<ButtonEvent>> {
        let mut buf = [0u8; InputEvent::SIZE];
        loop {
            match self.file.read(&mut buf) {
                // evdev returns whole events; a full record means one event.
                Ok(n) if n >= InputEvent::SIZE => {
                    if let Some(ev) = InputEvent::decode(&buf) {
                        if let Some(be) = to_button_event(ev) {
                            return Ok(Some(be));
                        }
                    }
                    // non-button event (SYN, etc.) — keep draining
                    continue;
                }
                Ok(_) => return Ok(None), // 0 or short read: nothing usable now
                Err(e) if e.kind() == ErrorKind::WouldBlock => return Ok(None),
                Err(e) => return Err(e),
            }
        }
    }
}
