// SPDX-License-Identifier: GPL-3.0-only
//! System: LEDs, backlight, temperature via sysfs.
pub mod parse;

use crate::error::{HalError, HalResult};
use crate::sys::parse::{parse_brightness, parse_millidegrees};
use std::fs;
use std::os::unix::fs::FileExt as _;
use std::path::Path;

/// The display backlight node under `/sys/class/backlight/` on the CR1140.
pub const BACKLIGHT: &str = "backlight";
/// `max_brightness` of [`BACKLIGHT`] on the CR1140. Prefer reading it at runtime
/// with [`backlight_max`]; this is the documented hardware value for a default.
pub const BACKLIGHT_MAX_HINT: u32 = 400;
/// Thermal zone backing the SoC temperature (`/sys/class/thermal/thermal_zone0`).
pub const SOC_THERMAL_ZONE: u32 = 0;

/// The CR1140's onboard LEDs. The three `*:status` LEDs are binary
/// (`max == 1`); the three `*:kbd_backlight` channels are 8-bit PWM
/// (`max == 255`), so only the keypad channels give a visible brightness ramp.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Led {
    StatusRed,
    StatusGreen,
    StatusBlue,
    KbdRed,
    KbdGreen,
    KbdBlue,
}

impl Led {
    /// The sysfs leaf name under `/sys/class/leds/`.
    pub fn name(self) -> &'static str {
        match self {
            Led::StatusRed => "red:status",
            Led::StatusGreen => "green:status",
            Led::StatusBlue => "blue:status",
            Led::KbdRed => "red:kbd_backlight",
            Led::KbdGreen => "green:kbd_backlight",
            Led::KbdBlue => "blue:kbd_backlight",
        }
    }

    /// `max_brightness` for this LED: 1 for the binary status LEDs, 255 for the
    /// PWM keypad-backlight channels.
    pub fn max(self) -> u32 {
        match self {
            Led::StatusRed | Led::StatusGreen | Led::StatusBlue => 1,
            Led::KbdRed | Led::KbdGreen | Led::KbdBlue => 255,
        }
    }
}

/// Set an LED brightness via `/sys/class/leds/<name>/brightness`.
pub fn set_led(name: &str, value: u32) -> HalResult<()> {
    fs::write(format!("/sys/class/leds/{name}/brightness"), value.to_string())?;
    Ok(())
}

/// Read an LED's current brightness (for save/restore).
pub fn read_led(name: &str) -> HalResult<u32> {
    let s = fs::read_to_string(format!("/sys/class/leds/{name}/brightness"))?;
    parse_brightness(&s).ok_or_else(|| HalError::Parse(format!("led {name} brightness: {s:?}")))
}

/// Set a typed [`Led`], clamping the value to that LED's [`Led::max`].
pub fn set_led_typed(led: Led, value: u32) -> HalResult<()> {
    set_led(led.name(), value.min(led.max()))
}

/// Set the RGB keypad backlight color. The CR1140 keypad LED is three PWM
/// channels (`red`/`green`/`blue:kbd_backlight`, each 0–255), so any color is a
/// mix: e.g. yellow = (255,255,0), orange = (255,90,0), off = (0,0,0).
pub fn set_kbd_backlight(r: u8, g: u8, b: u8) -> HalResult<()> {
    set_led(Led::KbdRed.name(), r as u32)?;
    set_led(Led::KbdGreen.name(), g as u32)?;
    set_led(Led::KbdBlue.name(), b as u32)?;
    Ok(())
}

/// Set display backlight via `/sys/class/backlight/<name>/brightness`.
pub fn set_backlight(name: &str, value: u32) -> HalResult<()> {
    fs::write(
        format!("/sys/class/backlight/{name}/brightness"),
        value.to_string(),
    )?;
    Ok(())
}

/// Read the backlight's current brightness (for save/restore).
pub fn read_backlight(name: &str) -> HalResult<u32> {
    let s = fs::read_to_string(format!("/sys/class/backlight/{name}/brightness"))?;
    parse_brightness(&s).ok_or_else(|| HalError::Parse(format!("backlight {name}: {s:?}")))
}

/// Read a thermal zone temperature in °C.
pub fn read_temp_c(zone: u32) -> HalResult<f32> {
    let s = fs::read_to_string(format!("/sys/class/thermal/thermal_zone{zone}/temp"))?;
    parse_millidegrees(&s).ok_or_else(|| HalError::Parse(format!("thermal_zone{zone}: {s:?}")))
}

/// Read max brightness for a backlight (for scaling).
pub fn backlight_max(name: &str) -> HalResult<u32> {
    let s = fs::read_to_string(format!("/sys/class/backlight/{name}/max_brightness"))?;
    parse_brightness(&s)
        .ok_or_else(|| HalError::Parse(format!("backlight {name} max_brightness: {s:?}")))
}

/// Available LED leaf names under `/sys/class/leds/`.
pub fn list_leds() -> HalResult<Vec<String>> {
    dir_entry_names("/sys/class/leds")
}

/// Available backlight names under `/sys/class/backlight/`.
pub fn list_backlights() -> HalResult<Vec<String>> {
    dir_entry_names("/sys/class/backlight")
}

/// Available thermal-zone numbers (the `N` in `thermal_zoneN`).
pub fn list_thermal_zones() -> HalResult<Vec<u32>> {
    Ok(dir_entry_names("/sys/class/thermal")?
        .iter()
        .filter_map(|n| parse_thermal_zone_num(n))
        .collect())
}

/// Sorted leaf names of a directory's entries.
fn dir_entry_names<P: AsRef<Path>>(dir: P) -> HalResult<Vec<String>> {
    let mut names = Vec::new();
    for entry in fs::read_dir(dir)? {
        if let Some(name) = entry?.file_name().to_str() {
            names.push(name.to_string());
        }
    }
    names.sort();
    Ok(names)
}

/// Parse `"thermal_zoneN"` → `N`; anything else → `None`.
fn parse_thermal_zone_num(name: &str) -> Option<u32> {
    name.strip_prefix("thermal_zone")?.parse().ok()
}

/// Stable sysfs path of the writable SPI retain EEPROM (32 KB).
///
/// The nvmem *node* is `spi1.00`, but the nvmem index can renumber across
/// kernels / probe order — so discovery keys on this **stable bus path**, never
/// on the index. See ADR-0002 and device-facts ("nvmem / EEPROM map").
pub const SPI_RETAIN_EEPROM: &str = "/sys/bus/spi/devices/spi1.0/eeprom";

/// A thin, typed window onto an nvmem byte device (an EEPROM exposed as a flat
/// binary attribute under sysfs).
///
/// This is *just* typed offset access — no A/B buffering, no CRC, no
/// serialization. Durability and atomicity are the **caller's** concern; the
/// SDK's `retain::Store` layers integrity (A/B double-buffer + CRC32) on top.
///
/// Bytes are read and written via positional I/O (`pread`/`pwrite`) so a single
/// `Nvmem` handle can service many offsets without seeking.
#[derive(Debug)]
pub struct Nvmem {
    file: fs::File,
    len: usize,
    writable: bool,
}

impl Nvmem {
    /// Open an nvmem device for **read and write** at a stable sysfs path.
    ///
    /// Use this for the writable SPI retain EEPROM. The length is taken from the
    /// file's reported size at open time.
    pub fn open(path: impl AsRef<Path>) -> HalResult<Self> {
        Self::open_with(path.as_ref(), true)
    }

    /// Open an nvmem device for **read only** at a stable sysfs path.
    ///
    /// Use this for the factory identity EEPROMs, which must never be written. A
    /// subsequent [`write_at`](Self::write_at) returns a permission-denied
    /// [`HalError::Io`].
    pub fn open_readonly(path: impl AsRef<Path>) -> HalResult<Self> {
        Self::open_with(path.as_ref(), false)
    }

    /// Open the known writable SPI retain EEPROM ([`SPI_RETAIN_EEPROM`]).
    pub fn open_retain() -> HalResult<Self> {
        Self::open(SPI_RETAIN_EEPROM)
    }

    fn open_with(path: &Path, writable: bool) -> HalResult<Self> {
        let file = fs::OpenOptions::new()
            .read(true)
            .write(writable)
            .open(path)
            .map_err(|e| match e.kind() {
                std::io::ErrorKind::NotFound => {
                    HalError::DeviceNotFound(format!("nvmem {}: {e}", path.display()))
                }
                _ => HalError::Io(e),
            })?;
        let len = file.metadata()?.len() as usize;
        Ok(Nvmem {
            file,
            len,
            writable,
        })
    }

    /// Total size of the device in bytes.
    pub fn len(&self) -> usize {
        self.len
    }

    /// Whether the device is empty (zero-length).
    pub fn is_empty(&self) -> bool {
        self.len == 0
    }

    /// Read `buf.len()` bytes starting at `offset`.
    ///
    /// Returns [`HalError::OutOfRange`] if `[offset, offset + buf.len())` falls
    /// outside `[0, len())`; [`HalError::Io`] on a device read failure.
    pub fn read_at(&self, offset: usize, buf: &mut [u8]) -> HalResult<()> {
        self.check_range(offset, buf.len())?;
        self.file
            .read_exact_at(buf, offset as u64)
            .map_err(HalError::Io)
    }

    /// Write `buf` starting at `offset`.
    ///
    /// Returns [`HalError::OutOfRange`] if the write would exceed `len()`, a
    /// permission-denied [`HalError::Io`] if the device was opened read-only, and
    /// [`HalError::Io`] on a device write failure. Durability/atomicity are the
    /// caller's concern.
    pub fn write_at(&self, offset: usize, buf: &[u8]) -> HalResult<()> {
        if !self.writable {
            return Err(HalError::Io(std::io::Error::new(
                std::io::ErrorKind::PermissionDenied,
                "nvmem opened read-only",
            )));
        }
        self.check_range(offset, buf.len())?;
        self.file
            .write_all_at(buf, offset as u64)
            .map_err(HalError::Io)
    }

    fn check_range(&self, offset: usize, n: usize) -> HalResult<()> {
        let end = offset
            .checked_add(n)
            .ok_or_else(|| HalError::OutOfRange(format!("offset {offset} + len {n} overflows")))?;
        if end > self.len {
            return Err(HalError::OutOfRange(format!(
                "[{offset}, {end}) exceeds device length {}",
                self.len
            )));
        }
        Ok(())
    }
}

/// Read-only accessor for the device's factory identity EEPROM (I²C `0-0051`).
///
/// Exposes the immutable factory identity (MAC, and — best-effort — serial /
/// article / product) programmed by ifm. This EEPROM survives a firmware reflash
/// on its own and is **never written** by this crate.
///
/// The on-EEPROM format uses an ifm `vhip` magic. Field offsets beyond the MAC
/// are only partially reverse-engineered from a single dump (see issue 02); only
/// [`mac`](Self::mac) is offset-confirmed against live hardware. Use
/// [`read_at`](Self::read_at) for raw access to inferred regions.
#[derive(Debug)]
pub struct FactoryEeprom {
    nvmem: Nvmem,
}

impl FactoryEeprom {
    /// Stable sysfs path of the device-identity EEPROM (I²C `0-0051`).
    pub const IDENTITY_EEPROM: &'static str = "/sys/bus/i2c/devices/0-0051/eeprom";

    /// Offset of the 6-byte binary MAC address. Confirmed against live hardware
    /// (`00:02:01:ab:bd:49` on the recon unit; see device-facts "nvmem / EEPROM map").
    pub const MAC_OFFSET: usize = 0xe9;

    /// Open the known device-identity EEPROM ([`IDENTITY_EEPROM`](Self::IDENTITY_EEPROM)).
    pub fn open() -> HalResult<Self> {
        Self::open_at(Self::IDENTITY_EEPROM)
    }

    /// Open a factory identity EEPROM at an explicit sysfs path (read-only).
    pub fn open_at(path: impl AsRef<Path>) -> HalResult<Self> {
        Ok(FactoryEeprom {
            nvmem: Nvmem::open_readonly(path)?,
        })
    }

    /// Raw read into `buf` at `offset` — escape hatch for not-yet-typed fields.
    pub fn read_at(&self, offset: usize, buf: &mut [u8]) -> HalResult<()> {
        self.nvmem.read_at(offset, buf)
    }

    /// The factory MAC address as six bytes (offset [`MAC_OFFSET`](Self::MAC_OFFSET)).
    pub fn mac(&self) -> HalResult<[u8; 6]> {
        let mut mac = [0u8; 6];
        self.nvmem.read_at(Self::MAC_OFFSET, &mut mac)?;
        Ok(mac)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn led_names_match_sysfs_leaves() {
        assert_eq!(Led::StatusRed.name(), "red:status");
        assert_eq!(Led::StatusBlue.name(), "blue:status");
        assert_eq!(Led::KbdRed.name(), "red:kbd_backlight");
        assert_eq!(Led::KbdBlue.name(), "blue:kbd_backlight");
    }

    #[test]
    fn led_max_is_1_for_status_255_for_kbd() {
        assert_eq!(Led::StatusGreen.max(), 1);
        assert_eq!(Led::KbdGreen.max(), 255);
    }

    #[test]
    fn thermal_zone_num_parses_zonen_only() {
        assert_eq!(parse_thermal_zone_num("thermal_zone0"), Some(0));
        assert_eq!(parse_thermal_zone_num("thermal_zone12"), Some(12));
        assert_eq!(parse_thermal_zone_num("cooling_device0"), None);
        assert_eq!(parse_thermal_zone_num("thermal_zone"), None);
    }

    #[test]
    fn dir_entry_names_lists_sorted_children() {
        let dir = std::env::temp_dir().join(format!("cr1140-sys-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(dir.join("green:status")).unwrap();
        fs::create_dir_all(dir.join("red:status")).unwrap();
        let mut names = dir_entry_names(&dir).unwrap();
        names.sort();
        assert_eq!(names, vec!["green:status".to_string(), "red:status".to_string()]);
        let _ = fs::remove_dir_all(&dir);
    }

    // ---- Nvmem / FactoryEeprom ----

    /// A zero-filled temp file of `size` bytes, standing in for an nvmem device.
    fn sized_tmp(name: &str, size: usize) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("cr1140-hal-nvmem-{}", std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        let p = dir.join(name);
        let f = fs::File::create(&p).unwrap();
        f.set_len(size as u64).unwrap();
        p
    }

    #[test]
    fn nvmem_reports_len() {
        let p = sized_tmp("len", 0x8000);
        let nv = Nvmem::open(&p).unwrap();
        assert_eq!(nv.len(), 0x8000);
        assert!(!nv.is_empty());
    }

    #[test]
    fn nvmem_round_trip_read_write() {
        let p = sized_tmp("rw", 256);
        let nv = Nvmem::open(&p).unwrap();
        let payload = [0xde, 0xad, 0xbe, 0xef];
        nv.write_at(16, &payload).unwrap();
        let mut buf = [0u8; 4];
        nv.read_at(16, &mut buf).unwrap();
        assert_eq!(buf, payload);
        // Bytes outside the written window stay zero.
        let mut z = [0xffu8; 4];
        nv.read_at(0, &mut z).unwrap();
        assert_eq!(z, [0, 0, 0, 0]);
    }

    #[test]
    fn nvmem_read_out_of_range_errs() {
        let p = sized_tmp("ror", 16);
        let nv = Nvmem::open(&p).unwrap();
        let mut buf = [0u8; 8];
        assert!(matches!(nv.read_at(12, &mut buf), Err(HalError::OutOfRange(_))));
    }

    #[test]
    fn nvmem_write_out_of_range_errs() {
        let p = sized_tmp("wor", 16);
        let nv = Nvmem::open(&p).unwrap();
        assert!(matches!(nv.write_at(12, &[0u8; 8]), Err(HalError::OutOfRange(_))));
    }

    #[test]
    fn nvmem_offset_overflow_errs() {
        let p = sized_tmp("ovf", 16);
        let nv = Nvmem::open(&p).unwrap();
        let mut buf = [0u8; 1];
        assert!(matches!(
            nv.read_at(usize::MAX, &mut buf),
            Err(HalError::OutOfRange(_))
        ));
    }

    #[test]
    fn nvmem_read_only_rejects_write() {
        let p = sized_tmp("ro", 16);
        let nv = Nvmem::open_readonly(&p).unwrap();
        let err = nv.write_at(0, &[1, 2, 3]).unwrap_err();
        assert!(matches!(err, HalError::Io(_)));
        // Reads still work.
        let mut buf = [0u8; 3];
        nv.read_at(0, &mut buf).unwrap();
    }

    #[test]
    fn nvmem_missing_path_is_device_not_found() {
        let err = Nvmem::open("/nonexistent/nvmem/device").unwrap_err();
        assert!(matches!(err, HalError::DeviceNotFound(_)));
    }

    #[test]
    fn factory_reads_mac_at_confirmed_offset() {
        let p = sized_tmp("factory", 512);
        let mac = [0x00, 0x02, 0x01, 0xab, 0xbd, 0x49];
        // Seed the MAC bytes via a writable handle, then reopen read-only.
        let w = Nvmem::open(&p).unwrap();
        w.write_at(FactoryEeprom::MAC_OFFSET, &mac).unwrap();
        drop(w);

        let eeprom = FactoryEeprom::open_at(&p).unwrap();
        assert_eq!(eeprom.mac().unwrap(), mac);

        let mut raw = [0u8; 6];
        eeprom.read_at(FactoryEeprom::MAC_OFFSET, &mut raw).unwrap();
        assert_eq!(raw, mac);
    }
}
