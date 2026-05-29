use crate::error::{HalError, HalResult};
use crate::input::event::{to_button_event, ButtonEvent, InputEvent};
use std::fs::{File, OpenOptions};
use std::io::{ErrorKind, Read};
use std::os::fd::{AsFd, AsRawFd, BorrowedFd, RawFd};
use std::os::unix::fs::OpenOptionsExt;
use std::path::Path;

/// evdev `name` of the CR1140 front-panel keypad (`gpio-keys`).
pub const KEYPAD_NAME: &str = "ifm-keypad";

/// Find the `/dev/input/eventN` node whose evdev device name equals `target`,
/// by scanning `<class_dir>/event*/device/name`. `class_dir` is a parameter so
/// this is host-testable; callers pass `/sys/class/input`. evdev numbering is
/// not stable across reboots, so matching by name beats hardcoding a node.
pub fn find_input_by_name<P: AsRef<Path>>(class_dir: P, target: &str) -> HalResult<String> {
    let mut entries: Vec<String> = std::fs::read_dir(&class_dir)?
        .filter_map(|e| e.ok()?.file_name().to_str().map(str::to_string))
        .filter(|n| n.starts_with("event"))
        .collect();
    entries.sort(); // deterministic: lowest eventN wins on a (theoretical) tie

    for ev in entries {
        let name_path = class_dir.as_ref().join(&ev).join("device").join("name");
        if let Ok(name) = std::fs::read_to_string(&name_path) {
            if name.trim() == target {
                return Ok(format!("/dev/input/{ev}"));
            }
        }
    }
    Err(HalError::DeviceNotFound(format!("input device named {target:?}")))
}

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
    pub fn open(path: &str) -> HalResult<Self> {
        Ok(Self { file: File::open(path)? })
    }

    /// Open in non-blocking mode (`O_NONBLOCK`) for use with [`poll_button`].
    ///
    /// [`poll_button`]: ButtonReader::poll_button
    pub fn open_nonblocking(path: &str) -> HalResult<Self> {
        let file = OpenOptions::new()
            .read(true)
            .custom_flags(nix::libc::O_NONBLOCK)
            .open(path)?;
        Ok(Self { file })
    }

    /// Open the CR1140 keypad in blocking mode, locating its node by name
    /// ([`KEYPAD_NAME`]) instead of hardcoding `/dev/input/event1`.
    pub fn open_keypad() -> HalResult<Self> {
        Self::open(&find_input_by_name("/sys/class/input", KEYPAD_NAME)?)
    }

    /// Open the CR1140 keypad in non-blocking mode (for [`poll_button`]),
    /// locating its node by name ([`KEYPAD_NAME`]).
    ///
    /// [`poll_button`]: ButtonReader::poll_button
    pub fn open_keypad_nonblocking() -> HalResult<Self> {
        Self::open_nonblocking(&find_input_by_name("/sys/class/input", KEYPAD_NAME)?)
    }

    /// Blocking: returns the next logical button event, skipping non-button records.
    pub fn next_button(&mut self) -> HalResult<ButtonEvent> {
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
    pub fn poll_button(&mut self) -> HalResult<Option<ButtonEvent>> {
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
                Err(e) => return Err(e.into()),
            }
        }
    }
}

/// Expose the underlying fd so the keypad can be registered in an `epoll`/
/// `select`/`poll` set alongside CAN and a timer, instead of busy-polling.
impl AsFd for ButtonReader {
    fn as_fd(&self) -> BorrowedFd<'_> {
        self.file.as_fd()
    }
}

impl AsRawFd for ButtonReader {
    fn as_raw_fd(&self) -> RawFd {
        self.file.as_raw_fd()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Build a fake `/sys/class/input` tree: `events` is a list of
    /// `(node, Option<name>)` — `None` means the node has no `device/name`.
    fn fake_class_dir(tag: &str, events: &[(&str, Option<&str>)]) -> std::path::PathBuf {
        let root = std::env::temp_dir().join(format!("cr1140-input-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        for (node, name) in events {
            let dev = root.join(node).join("device");
            std::fs::create_dir_all(&dev).unwrap();
            if let Some(n) = name {
                std::fs::write(dev.join("name"), format!("{n}\n")).unwrap();
            }
        }
        root
    }

    #[test]
    fn finds_node_by_name_and_trims_newline() {
        let dir = fake_class_dir(
            "match",
            &[("event0", Some("snvs-powerkey")), ("event1", Some("ifm-keypad"))],
        );
        assert_eq!(
            find_input_by_name(&dir, "ifm-keypad").unwrap(),
            "/dev/input/event1"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn no_match_is_device_not_found() {
        let dir = fake_class_dir("nomatch", &[("event0", Some("snvs-powerkey"))]);
        assert!(matches!(
            find_input_by_name(&dir, "ifm-keypad"),
            Err(HalError::DeviceNotFound(_))
        ));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn skips_nodes_missing_a_name_file() {
        let dir = fake_class_dir(
            "missing",
            &[("event0", None), ("event2", Some("ifm-keypad"))],
        );
        assert_eq!(
            find_input_by_name(&dir, "ifm-keypad").unwrap(),
            "/dev/input/event2"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }
}
