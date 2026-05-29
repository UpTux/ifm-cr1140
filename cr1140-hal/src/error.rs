// SPDX-License-Identifier: GPL-3.0-only
//! Unified error type for the HAL. Wraps the OS errors the underlying ABIs
//! return (`std::io::Error`) and adds HAL-specific failure causes so callers
//! can distinguish "no such device" from "wrong pixel format" from a plain I/O
//! error without string-matching.

/// Errors returned by `cr1140-hal`.
#[derive(Debug, thiserror::Error)]
pub enum HalError {
    /// An underlying OS / I/O error (open, ioctl, sysfs read/write).
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    /// A device or node could not be located (e.g. no input matched a name).
    #[error("device not found: {0}")]
    DeviceNotFound(String),
    /// The framebuffer is not the expected xRGB8888/32bpp layout.
    #[error("unsupported framebuffer format: {0}")]
    UnsupportedFormat(String),
    /// A value was outside the hardware's accepted range.
    #[error("value out of range: {0}")]
    OutOfRange(String),
    /// A sysfs/procfs value could not be parsed.
    #[error("parse error: {0}")]
    Parse(String),
}

/// `Result` specialised to [`HalError`].
pub type HalResult<T> = Result<T, HalError>;

/// Degrade a [`HalError`] back to an [`std::io::Error`] so existing
/// `io::Result`-based callers (e.g. `cr1140-sdk`) keep composing with `?`.
/// The [`HalError::Io`] variant is returned as-is; the rest map to
/// [`std::io::ErrorKind::Other`] preserving the message.
impl From<HalError> for std::io::Error {
    fn from(e: HalError) -> Self {
        match e {
            HalError::Io(io) => io,
            other => std::io::Error::other(other.to_string()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn io_error_converts_via_from() {
        let io = std::io::Error::new(std::io::ErrorKind::PermissionDenied, "nope");
        let err: HalError = io.into();
        assert!(matches!(err, HalError::Io(_)));
    }

    #[test]
    fn display_strings_name_the_cause() {
        assert_eq!(
            HalError::DeviceNotFound("ifm-keypad".into()).to_string(),
            "device not found: ifm-keypad"
        );
        assert_eq!(
            HalError::UnsupportedFormat("16 bpp".into()).to_string(),
            "unsupported framebuffer format: 16 bpp"
        );
        assert_eq!(
            HalError::OutOfRange("500 > 400".into()).to_string(),
            "value out of range: 500 > 400"
        );
    }

    #[test]
    fn io_variant_round_trips_back_to_io_error() {
        let original = std::io::Error::new(std::io::ErrorKind::NotFound, "gone");
        let io: std::io::Error = HalError::from(original).into();
        assert_eq!(io.kind(), std::io::ErrorKind::NotFound);
    }

    #[test]
    fn non_io_variant_degrades_to_other_kind() {
        let io: std::io::Error = HalError::DeviceNotFound("x".into()).into();
        assert_eq!(io.kind(), std::io::ErrorKind::Other);
        assert!(io.to_string().contains("device not found: x"));
    }

    #[test]
    fn question_mark_propagates_io_as_hal_error() {
        fn inner() -> HalResult<String> {
            let s = std::fs::read_to_string("/definitely/not/a/real/path/xyz")?;
            Ok(s)
        }
        assert!(matches!(inner(), Err(HalError::Io(_))));
    }
}
