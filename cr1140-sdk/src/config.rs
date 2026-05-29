// SPDX-License-Identifier: GPL-3.0-only
//! Atomic config persistence for the writable p2 overlay (`/home/cds-apps`).
//!
//! [`Store`] is generic over any `serde` type; the app owns the schema. Saves are
//! atomic (temp file + fsync + rename) so a power cut on the overlay never leaves
//! a half-written file — the previous version stays intact.

use crate::{SdkError, SdkResult};
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};

use serde::de::DeserializeOwned;
use serde::Serialize;

/// Persistent app directory on the p2 overlay; writes here survive reboots.
pub const DEFAULT_APP_DIR: &str = "/home/cds-apps";

/// A TOML-backed config file. The app supplies the path and the schema type.
pub struct Store {
    path: PathBuf,
}

impl Store {
    /// A store at a specific file path (e.g. `format!("{DEFAULT_APP_DIR}/app.toml")`).
    pub fn at(path: impl Into<PathBuf>) -> Self {
        Self { path: path.into() }
    }

    /// Load and decode the file. `Ok(None)` if it does not exist.
    pub fn load<T: DeserializeOwned>(&self) -> SdkResult<Option<T>> {
        match fs::read_to_string(&self.path) {
            Ok(s) => Ok(Some(toml::from_str(&s)?)),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(e) => Err(SdkError::Io(e)),
        }
    }

    /// Load, or return `T::default()` if the file is absent.
    pub fn load_or_default<T: DeserializeOwned + Default>(&self) -> SdkResult<T> {
        Ok(self.load()?.unwrap_or_default())
    }

    /// Encode and atomically write: temp file in the same dir, fsync, rename over
    /// the target, then fsync the directory. Atomic on the overlayfs upper.
    pub fn save<T: Serialize>(&self, value: &T) -> SdkResult<()> {
        let s = toml::to_string(value)?;
        let dir = self.path.parent().unwrap_or_else(|| Path::new("."));
        fs::create_dir_all(dir)?;
        // Append ".tmp" to the FULL file name (not `with_extension`, which would
        // collide for "a.toml"/"a.json" and, worse, make tmp == path for a
        // ".tmp" target — silently defeating atomicity).
        let tmp = match self.path.file_name() {
            Some(name) => {
                let mut name = name.to_os_string();
                name.push(".tmp");
                self.path.with_file_name(name)
            }
            None => return Err(SdkError::Io(std::io::Error::from(std::io::ErrorKind::InvalidInput))),
        };
        {
            let mut f = fs::File::create(&tmp)?;
            f.write_all(s.as_bytes())?;
            f.sync_all()?;
        }
        fs::rename(&tmp, &self.path)?;
        // Best-effort dir fsync so the rename itself is durable; ignore errors
        // (some filesystems reject O_RDONLY dir fsync).
        if let Ok(d) = fs::File::open(dir) {
            let _ = d.sync_all();
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde::Deserialize;

    #[derive(Debug, PartialEq, Serialize, Deserialize, Default)]
    struct Cfg {
        brightness: u32,
        label: String,
    }

    fn temp_path(tag: &str) -> PathBuf {
        std::env::temp_dir().join(format!("cr1140-store-{}-{}.toml", std::process::id(), tag))
    }

    #[test]
    fn save_then_load_round_trips() {
        let p = temp_path("roundtrip");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        let cfg = Cfg { brightness: 200, label: "green".into() };
        store.save(&cfg).unwrap();
        let back: Option<Cfg> = store.load().unwrap();
        assert_eq!(back, Some(cfg));
        let _ = fs::remove_file(&p);
    }

    #[test]
    fn load_missing_file_is_none() {
        let p = temp_path("missing");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        let back: Option<Cfg> = store.load().unwrap();
        assert_eq!(back, None);
    }

    #[test]
    fn load_or_default_uses_default_when_absent() {
        let p = temp_path("default");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        let back: Cfg = store.load_or_default().unwrap();
        assert_eq!(back, Cfg::default());
    }

    #[test]
    fn save_leaves_no_tmp_file() {
        let p = temp_path("notmp");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        store.save(&Cfg { brightness: 1, label: "x".into() }).unwrap();
        let tmp = PathBuf::from(format!("{}.tmp", p.display()));
        assert!(!tmp.exists(), "temp file {tmp:?} should have been renamed away");
        let _ = fs::remove_file(&p);
    }

    #[test]
    fn save_overwrites_previous_value() {
        let p = temp_path("overwrite");
        let _ = fs::remove_file(&p);
        let store = Store::at(&p);
        store.save(&Cfg { brightness: 10, label: "old".into() }).unwrap();
        store.save(&Cfg { brightness: 99, label: "new".into() }).unwrap();
        let back: Cfg = store.load_or_default().unwrap();
        assert_eq!(back, Cfg { brightness: 99, label: "new".into() });
        let _ = fs::remove_file(&p);
    }

    #[test]
    fn load_malformed_toml_is_decode_error() {
        let p = temp_path("malformed");
        fs::write(&p, "this is not = = toml").unwrap();
        let store = Store::at(&p);
        let err = store.load::<Cfg>().unwrap_err();
        assert!(matches!(err, SdkError::Decode(_)), "got {err}");
        let _ = fs::remove_file(&p);
    }
}
