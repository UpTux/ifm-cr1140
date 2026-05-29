// SPDX-License-Identifier: GPL-3.0-only
//! Unified SDK error type. Mirrors `cr1140_hal::HalError`'s thiserror pattern so
//! callers — including ROS 2 / Apex nodes — can match on the cause. Metrics
//! parsers/readers stay `Option`; fallible hardware and config operations return
//! [`SdkResult`].

/// Errors from fallible SDK operations (hardware restore, config IO).
#[derive(Debug, thiserror::Error)]
pub enum SdkError {
    /// An underlying HAL operation failed (sysfs read/write, parse).
    #[error("hal: {0}")]
    Hal(#[from] cr1140_hal::HalError),
    /// Filesystem IO failed.
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    /// A config file could not be decoded from TOML.
    #[cfg(feature = "config")]
    #[error("config decode: {0}")]
    Decode(#[from] toml::de::Error),
    /// A config value could not be encoded to TOML.
    #[cfg(feature = "config")]
    #[error("config encode: {0}")]
    Encode(#[from] toml::ser::Error),
}

/// Result alias for SDK operations.
pub type SdkResult<T> = Result<T, SdkError>;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn from_io_error_displays_with_prefix() {
        let io = std::io::Error::new(std::io::ErrorKind::NotFound, "nope");
        let e: SdkError = io.into();
        assert!(e.to_string().starts_with("io: "), "got {e}");
    }

    #[test]
    fn from_hal_error_is_wrapped() {
        let hal = cr1140_hal::HalError::DeviceNotFound("kbd".into());
        let e: SdkError = hal.into();
        assert!(matches!(e, SdkError::Hal(_)));
        assert!(e.to_string().starts_with("hal: "), "got {e}");
    }

    #[cfg(feature = "config")]
    #[test]
    fn from_toml_decode_error_is_wrapped() {
        // `= 1` with no key is invalid TOML.
        let err = toml::from_str::<toml::Table>("= 1").unwrap_err();
        let e: SdkError = err.into();
        assert!(matches!(e, SdkError::Decode(_)));
    }
}
