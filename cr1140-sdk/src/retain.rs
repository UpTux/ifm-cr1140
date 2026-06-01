// SPDX-License-Identifier: GPL-3.0-only
//! Reflash-surviving persistence on the SPI EEPROM.
//!
//! [`Store`] is the power-safe sibling of [`crate::config::Store`]. Where
//! `config::Store` keeps ordinary app settings as TOML on the p2 overlay — which
//! a firmware update `mkfs.ext4 -F`'s away — `retain::Store` writes a serde type
//! to the 32 KB SPI EEPROM, the only writable storage a `.swu` never touches
//! (ADR-0002). Use it for the handful of values that must survive a reflash:
//! factory calibration, network/IP config.
//!
//! ## Integrity: A/B double-buffer + CRC32
//!
//! The region is split into two equal slots. Each slot is framed as
//! `[magic | version | seq | len | crc32 | payload]`. [`save`](Store::save)
//! writes the **inactive** slot with a bumped sequence number; that slot only
//! becomes current once it carries both the higher `seq` *and* a valid CRC. A
//! power loss mid-write therefore only ever corrupts the inactive slot — the
//! previous good slot is left intact, so [`load`](Store::load) recovers the last
//! committed value. `magic` rejects CODESYS leftovers and blank EEPROM; an
//! unknown `version` is treated as absent rather than panicking.
//!
//! ## Endurance: write-only-if-changed
//!
//! EEPROM has finite write endurance, so [`save`](Store::save) reads the active
//! slot first and **no-ops if the encoded payload is identical** — no write, no
//! `seq` bump. This makes "call `save()` whenever the value might have changed"
//! safe by construction.
//!
//! **Low-frequency only.** This store targets power-safe, low-frequency settings.
//! A future high-frequency retain workload should route to the battery-backed
//! SNVS LPGPR (16 B, effectively unlimited writes), not the EEPROM — see
//! ADR-0002.

use std::marker::PhantomData;

use cr1140_hal::sys::Nvmem;
use serde::de::DeserializeOwned;
use serde::Serialize;

use crate::error::{SdkError, SdkResult};

/// Slot framing magic (`b"RTNS"` — ReTaiN Store). Rejects blank/foreign bytes.
const MAGIC: [u8; 4] = *b"RTNS";

/// On-EEPROM framing version. An unknown version is treated as absent.
const FORMAT_VERSION: u8 = 1;

/// Fixed slot header size: magic[4] + version[1] + reserved[3] + seq[4] + len[4] + crc[4].
const HEADER_SIZE: usize = 20;

/// IEEE 802.3 CRC-32 (reflected, polynomial `0xEDB88320`) over `data`.
///
/// Hand-rolled to avoid a dependency — the retain payload is small and written
/// rarely, so a table-free byte loop is more than fast enough.
fn crc32(data: &[u8]) -> u32 {
    let mut crc: u32 = 0xFFFF_FFFF;
    for &byte in data {
        crc ^= byte as u32;
        for _ in 0..8 {
            let mask = (crc & 1).wrapping_neg();
            crc = (crc >> 1) ^ (0xEDB8_8320 & mask);
        }
    }
    !crc
}

/// A decoded, integrity-checked slot.
struct Slot {
    seq: u32,
    payload: Vec<u8>,
}

/// A reflash-surviving store for a serde-serializable type `T`, backed by an
/// nvmem device (the SPI retain EEPROM).
///
/// Mirrors the [`crate::config::Store`] surface (`load` / `load_or_default` /
/// `save`) but adds A/B + CRC32 integrity and write-coalescing. The application
/// owns the top-level `T` and embeds whatever it needs (e.g. the SDK's
/// `net::NetworkConfig`) into a single composed blob.
pub struct Store<T> {
    nvmem: Nvmem,
    slot_size: usize,
    _marker: PhantomData<T>,
}

impl<T> Store<T>
where
    T: Serialize + DeserializeOwned,
{
    /// Open a retain store over `nvmem`, splitting it into two equal slots.
    ///
    /// Returns [`SdkError::Retain`] if the device is too small to hold even an
    /// empty-payload slot pair.
    pub fn open(nvmem: Nvmem) -> SdkResult<Self> {
        let slot_size = nvmem.len() / 2;
        if slot_size < HEADER_SIZE {
            return Err(SdkError::Retain(format!(
                "device too small: {} bytes, need at least {} per slot",
                nvmem.len(),
                HEADER_SIZE
            )));
        }
        Ok(Store {
            nvmem,
            slot_size,
            _marker: PhantomData,
        })
    }

    /// Byte offset of slot `index` (0 or 1) within the device.
    fn slot_offset(&self, index: usize) -> usize {
        index * self.slot_size
    }

    /// The largest payload that fits in a slot.
    fn max_payload(&self) -> usize {
        self.slot_size - HEADER_SIZE
    }

    /// Read and integrity-check slot `index`. Returns `Ok(None)` for any slot
    /// that is blank, foreign, an unknown version, or fails its CRC — i.e. "not a
    /// committed value", never an error.
    fn read_slot(&self, index: usize) -> SdkResult<Option<Slot>> {
        let base = self.slot_offset(index);
        let mut header = [0u8; HEADER_SIZE];
        self.nvmem.read_at(base, &mut header)?;

        if header[0..4] != MAGIC {
            return Ok(None);
        }
        if header[4] != FORMAT_VERSION {
            // Unknown schema version → treat as absent, don't panic.
            return Ok(None);
        }
        let seq = u32::from_le_bytes(header[8..12].try_into().unwrap());
        let len = u32::from_le_bytes(header[12..16].try_into().unwrap()) as usize;
        let stored_crc = u32::from_le_bytes(header[16..20].try_into().unwrap());

        if len > self.max_payload() {
            // Corrupt length field — can't trust this slot.
            return Ok(None);
        }
        let mut payload = vec![0u8; len];
        self.nvmem.read_at(base + HEADER_SIZE, &mut payload)?;

        if crc32(&payload) != stored_crc {
            return Ok(None);
        }
        Ok(Some(Slot { seq, payload }))
    }

    /// The currently-active slot (the valid one with the highest `seq`), if any,
    /// along with its index.
    fn active(&self) -> SdkResult<Option<(usize, Slot)>> {
        let a = self.read_slot(0)?;
        let b = self.read_slot(1)?;
        Ok(match (a, b) {
            (Some(a), Some(b)) => {
                if b.seq > a.seq {
                    Some((1, b))
                } else {
                    Some((0, a))
                }
            }
            (Some(a), None) => Some((0, a)),
            (None, Some(b)) => Some((1, b)),
            (None, None) => None,
        })
    }

    /// Load and decode the committed value, or `Ok(None)` if the region holds no
    /// valid value (blank, foreign, all-corrupt, or unknown version).
    pub fn load(&self) -> SdkResult<Option<T>> {
        match self.active()? {
            Some((_, slot)) => {
                let value = postcard::from_bytes(&slot.payload)
                    .map_err(|e| SdkError::Retain(format!("decode: {e}")))?;
                Ok(Some(value))
            }
            None => Ok(None),
        }
    }

    /// Load the value or fall back to `T::default()` if absent.
    pub fn load_or_default(&self) -> SdkResult<T>
    where
        T: Default,
    {
        Ok(self.load()?.unwrap_or_default())
    }

    /// Serialize and commit `value` to the inactive slot.
    ///
    /// No-ops (no write, no `seq` bump) if the encoded payload is byte-identical
    /// to the active slot — see the module-level note on endurance.
    pub fn save(&self, value: &T) -> SdkResult<()> {
        let payload =
            postcard::to_stdvec(value).map_err(|e| SdkError::Retain(format!("encode: {e}")))?;
        if payload.len() > self.max_payload() {
            return Err(SdkError::Retain(format!(
                "payload {} bytes exceeds slot capacity {}",
                payload.len(),
                self.max_payload()
            )));
        }

        let active = self.active()?;
        if let Some((_, slot)) = &active {
            if slot.payload == payload {
                // Write-only-if-changed: identical value, nothing to do.
                return Ok(());
            }
        }

        // Write the *inactive* slot so a torn write can't clobber the active one.
        let (target_index, next_seq) = match &active {
            Some((index, slot)) => (1 - index, slot.seq.wrapping_add(1)),
            None => (0, 1),
        };

        let mut frame = vec![0u8; HEADER_SIZE + payload.len()];
        frame[0..4].copy_from_slice(&MAGIC);
        frame[4] = FORMAT_VERSION;
        // bytes 5..8 reserved (zero)
        frame[8..12].copy_from_slice(&next_seq.to_le_bytes());
        frame[12..16].copy_from_slice(&(payload.len() as u32).to_le_bytes());
        frame[16..20].copy_from_slice(&crc32(&payload).to_le_bytes());
        frame[HEADER_SIZE..].copy_from_slice(&payload);

        self.nvmem
            .write_at(self.slot_offset(target_index), &frame)?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde::{Deserialize, Serialize};
    use std::fs;
    use std::path::PathBuf;

    /// A zero-filled temp file of `size` bytes, standing in for the SPI retain
    /// EEPROM. Returns the path so a test can reopen it (e.g. to inject
    /// corruption) without a `tempfile` dependency.
    fn eeprom_path(tag: &str, size: usize) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("cr1140-retain-{}", std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        let p = dir.join(format!("{tag}.bin"));
        let f = fs::File::create(&p).unwrap();
        f.set_len(size as u64).unwrap();
        p
    }

    fn fake_eeprom(tag: &str) -> (PathBuf, Nvmem) {
        let p = eeprom_path(tag, 0x8000);
        let nv = Nvmem::open(&p).unwrap();
        (p, nv)
    }

    #[derive(Debug, PartialEq, Clone, Serialize, Deserialize, Default)]
    struct Settings {
        name: String,
        count: u32,
        dns: Vec<String>,
    }

    fn sample() -> Settings {
        Settings {
            name: "device-1".into(),
            count: 7,
            dns: vec!["1.1.1.1".into(), "8.8.8.8".into()],
        }
    }

    #[test]
    fn crc32_matches_known_vector() {
        // Standard IEEE CRC-32 of "123456789" is 0xCBF43926.
        assert_eq!(crc32(b"123456789"), 0xCBF4_3926);
    }

    #[test]
    fn blank_region_loads_none() {
        let (_p, nv) = fake_eeprom("blank");
        let store: Store<Settings> = Store::open(nv).unwrap();
        assert_eq!(store.load().unwrap(), None);
    }

    #[test]
    fn round_trip_save_load() {
        let (_p, nv) = fake_eeprom("roundtrip");
        let store: Store<Settings> = Store::open(nv).unwrap();
        let value = sample();
        store.save(&value).unwrap();
        assert_eq!(store.load().unwrap(), Some(value));
    }

    #[test]
    fn load_or_default_when_absent() {
        let (_p, nv) = fake_eeprom("default");
        let store: Store<Settings> = Store::open(nv).unwrap();
        assert_eq!(store.load_or_default().unwrap(), Settings::default());
    }

    #[test]
    fn save_alternates_slots_and_bumps_seq() {
        let (_p, nv) = fake_eeprom("alternate");
        let store: Store<Settings> = Store::open(nv).unwrap();

        let mut v = sample();
        store.save(&v).unwrap();
        let (idx1, slot1) = store.active().unwrap().unwrap();
        assert_eq!(idx1, 0);
        assert_eq!(slot1.seq, 1);

        v.count = 8;
        store.save(&v).unwrap();
        let (idx2, slot2) = store.active().unwrap().unwrap();
        assert_eq!(idx2, 1, "second save lands in the inactive slot");
        assert_eq!(slot2.seq, 2);

        v.count = 9;
        store.save(&v).unwrap();
        let (idx3, slot3) = store.active().unwrap().unwrap();
        assert_eq!(idx3, 0, "third save wraps back to slot 0");
        assert_eq!(slot3.seq, 3);
    }

    #[test]
    fn unchanged_save_is_a_noop() {
        let (_p, nv) = fake_eeprom("noop");
        let store: Store<Settings> = Store::open(nv).unwrap();
        let value = sample();

        store.save(&value).unwrap();
        let seq_before = store.active().unwrap().unwrap().1.seq;

        // Saving the identical value must not write or bump seq.
        store.save(&value).unwrap();
        let seq_after = store.active().unwrap().unwrap().1.seq;
        assert_eq!(seq_before, seq_after);
    }

    #[test]
    fn torn_write_recovers_previous_value() {
        let (p, nv) = fake_eeprom("torn");
        let store: Store<Settings> = Store::open(nv).unwrap();

        // Commit v1 (slot 0), then v2 (slot 1).
        let mut v1 = sample();
        store.save(&v1).unwrap();
        v1.count = 99;
        let v2 = v1.clone();
        store.save(&v2).unwrap();
        assert_eq!(store.active().unwrap().unwrap().0, 1);

        // Simulate a torn write that corrupted the *inactive* slot (slot 0).
        let scribble = Nvmem::open(&p).unwrap();
        scribble.write_at(0, &[0xAAu8; 64]).unwrap();

        // The committed v2 in slot 1 still loads.
        assert_eq!(store.load().unwrap(), Some(v2));
    }

    #[test]
    fn corrupt_active_payload_falls_back_to_none_when_alone() {
        let (p, nv) = fake_eeprom("corrupt");
        let store: Store<Settings> = Store::open(nv).unwrap();
        store.save(&sample()).unwrap();

        // Flip a payload byte in slot 0 → CRC fails → slot is not valid.
        let raw = Nvmem::open(&p).unwrap();
        let mut byte = [0u8; 1];
        raw.read_at(HEADER_SIZE, &mut byte).unwrap();
        byte[0] ^= 0xFF;
        raw.write_at(HEADER_SIZE, &byte).unwrap();

        assert_eq!(store.load().unwrap(), None);
    }

    #[test]
    fn unknown_version_is_treated_as_absent() {
        let (p, nv) = fake_eeprom("version");
        let store: Store<Settings> = Store::open(nv).unwrap();
        store.save(&sample()).unwrap();

        // Bump the on-disk version byte to an unknown value.
        let raw = Nvmem::open(&p).unwrap();
        raw.write_at(4, &[0xFF]).unwrap();

        assert_eq!(store.load().unwrap(), None);
    }

    #[test]
    fn oversized_payload_errors() {
        let p = eeprom_path("oversized", 64); // 32-byte slots
        let nv = Nvmem::open(&p).unwrap();
        let store: Store<Settings> = Store::open(nv).unwrap();
        let big = Settings {
            name: "x".repeat(100),
            count: 0,
            dns: vec![],
        };
        assert!(matches!(store.save(&big), Err(SdkError::Retain(_))));
    }

    #[test]
    fn too_small_device_errors() {
        let p = eeprom_path("tiny", 8); // 4-byte slots, < HEADER_SIZE
        let nv = Nvmem::open(&p).unwrap();
        assert!(matches!(
            Store::<Settings>::open(nv),
            Err(SdkError::Retain(_))
        ));
    }
}
