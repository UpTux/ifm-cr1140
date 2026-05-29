//! Display output: pixel surface plus an fbdev backend.
pub mod surface;
pub use surface::Surface;

pub mod fbdev;
pub use fbdev::FbDisplay;
