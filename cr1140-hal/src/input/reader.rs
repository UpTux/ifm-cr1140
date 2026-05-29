use crate::input::event::{to_button_event, ButtonEvent, InputEvent};
use std::fs::File;
use std::io::Read;

/// Blocking reader of logical button events from an evdev node (`/dev/input/eventN`).
pub struct ButtonReader {
    file: File,
}

impl ButtonReader {
    pub fn open(path: &str) -> std::io::Result<Self> {
        Ok(Self { file: File::open(path)? })
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
}
