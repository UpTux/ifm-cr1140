//! Front-panel buttons via evdev.
pub mod event;
pub use event::{to_button_event, Button, ButtonEvent, InputEvent};
