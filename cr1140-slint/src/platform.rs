//! Minimal Slint `Platform` for Option B: one window, software-rendered, driven
//! by our own super-loop in `main`. No system event loop, no GPU, no winit.

use slint::platform::software_renderer::MinimalSoftwareWindow;
use slint::platform::{Platform, WindowAdapter};
use slint::PlatformError;
use std::rc::Rc;
use std::time::Instant;

pub struct FbPlatform {
    window: Rc<MinimalSoftwareWindow>,
    start: Instant,
}

impl FbPlatform {
    pub fn new(window: Rc<MinimalSoftwareWindow>) -> Self {
        Self { window, start: Instant::now() }
    }
}

impl Platform for FbPlatform {
    fn create_window_adapter(&self) -> Result<Rc<dyn WindowAdapter>, PlatformError> {
        // Only one window on this device — hand back the one we own.
        Ok(self.window.clone())
    }

    fn duration_since_start(&self) -> core::time::Duration {
        self.start.elapsed()
    }
}
