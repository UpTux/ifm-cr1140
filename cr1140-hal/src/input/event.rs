// SPDX-License-Identifier: GPL-3.0-only
pub const EV_KEY: u16 = 1;

/// Raw evdev event (aarch64 layout = 24 bytes: timeval(16) + type(2) + code(2) + value(4)).
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct InputEvent {
    pub type_: u16,
    pub code: u16,
    pub value: i32,
}

impl InputEvent {
    pub const SIZE: usize = 24;

    /// Decode one 24-byte evdev record. Returns None if the slice is too short.
    pub fn decode(bytes: &[u8]) -> Option<InputEvent> {
        if bytes.len() < Self::SIZE {
            return None;
        }
        // bytes 0..16 = timeval (ignored); 16..18 type; 18..20 code; 20..24 value
        let type_ = u16::from_le_bytes([bytes[16], bytes[17]]);
        let code = u16::from_le_bytes([bytes[18], bytes[19]]);
        let value = i32::from_le_bytes([bytes[20], bytes[21], bytes[22], bytes[23]]);
        Some(InputEvent { type_, code, value })
    }
}

/// Logical front-panel buttons.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub enum Button {
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    Up,
    Down,
    Left,
    Right,
    Enter,
}

#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub enum ButtonEvent {
    Pressed(Button),
    Released(Button),
}

/// Maps an evdev key code to a logical [`Button`].
///
/// The `ifm-keypad` (`/dev/input/event1`) emits standard Linux KEY_* codes,
/// confirmed key-by-key against the live device (Task 3.3): physical F1..F6 =
/// 59..64, Up=103, Down=108, Left=105, Right=106, Enter=28. Physical labels
/// match the standard codes 1:1.
pub fn code_to_button(code: u16) -> Option<Button> {
    match code {
        59 => Some(Button::F1),
        60 => Some(Button::F2),
        61 => Some(Button::F3),
        62 => Some(Button::F4),
        63 => Some(Button::F5),
        64 => Some(Button::F6),
        103 => Some(Button::Up),
        108 => Some(Button::Down),
        105 => Some(Button::Left),
        106 => Some(Button::Right),
        28 => Some(Button::Enter),
        _ => None,
    }
}

/// Translate a raw event into a logical [`ButtonEvent`] (press/release only).
pub fn to_button_event(ev: InputEvent) -> Option<ButtonEvent> {
    if ev.type_ != EV_KEY {
        return None;
    }
    let btn = code_to_button(ev.code)?;
    match ev.value {
        1 => Some(ButtonEvent::Pressed(btn)),
        0 => Some(ButtonEvent::Released(btn)),
        _ => None, // 2 = autorepeat, ignored
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn raw(type_: u16, code: u16, value: i32) -> [u8; 24] {
        let mut b = [0u8; 24];
        b[16..18].copy_from_slice(&type_.to_le_bytes());
        b[18..20].copy_from_slice(&code.to_le_bytes());
        b[20..24].copy_from_slice(&value.to_le_bytes());
        b
    }

    #[test]
    fn decode_too_short_is_none() {
        assert_eq!(InputEvent::decode(&[0u8; 23]), None);
    }

    #[test]
    fn decode_reads_type_code_value() {
        let b = raw(EV_KEY, 28, 1);
        assert_eq!(
            InputEvent::decode(&b),
            Some(InputEvent {
                type_: 1,
                code: 28,
                value: 1
            })
        );
    }

    #[test]
    fn key_press_maps_to_pressed_button() {
        let ev = InputEvent {
            type_: EV_KEY,
            code: 28,
            value: 1,
        };
        assert_eq!(
            to_button_event(ev),
            Some(ButtonEvent::Pressed(Button::Enter))
        );
    }

    #[test]
    fn key_release_maps_to_released_button() {
        let ev = InputEvent {
            type_: EV_KEY,
            code: 59,
            value: 0,
        };
        assert_eq!(to_button_event(ev), Some(ButtonEvent::Released(Button::F1)));
    }

    #[test]
    fn autorepeat_and_syn_ignored() {
        assert_eq!(
            to_button_event(InputEvent {
                type_: EV_KEY,
                code: 28,
                value: 2
            }),
            None
        );
        assert_eq!(
            to_button_event(InputEvent {
                type_: 0,
                code: 0,
                value: 0
            }),
            None
        );
    }
}
