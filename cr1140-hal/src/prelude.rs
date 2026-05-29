//! Convenience re-exports. `use cr1140_hal::prelude::*;` pulls in the common
//! types so apps don't need a long list of module-qualified `use` statements.

pub use crate::display::{FbDisplay, Surface};
pub use crate::error::{HalError, HalResult};
pub use crate::input::{Button, ButtonEvent, ButtonReader, KEYPAD_NAME};
pub use crate::sys::{Led, BACKLIGHT, SOC_THERMAL_ZONE};

#[cfg(target_os = "linux")]
pub use crate::can::CanBus;
