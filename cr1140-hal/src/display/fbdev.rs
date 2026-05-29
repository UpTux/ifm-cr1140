// SPDX-License-Identifier: GPL-3.0-only
//! Linux framebuffer (fbdev) backend. The CR1140 runs linuxfb at 800x480,
//! so `/dev/fb0` is the display path.
use crate::display::Surface;
use crate::error::{HalError, HalResult};
use nix::libc;
use nix::sys::mman::{mmap, munmap, MapFlags, ProtFlags};
use std::fs::{File, OpenOptions};
use std::num::NonZeroUsize;
use std::os::fd::AsRawFd;
use std::ptr::NonNull;

// Linux fb ioctls (from <linux/fb.h>). The `request` arg of ioctl() is
// `c_ulong` on glibc but `c_int` on musl; `as _` at the call site coerces to
// whichever the active target libc expects.
const FBIOGET_VSCREENINFO: u32 = 0x4600;
const FBIOPUT_VSCREENINFO: u32 = 0x4601;
const FBIOGET_FSCREENINFO: u32 = 0x4602;
const FBIOPAN_DISPLAY: u32 = 0x4606;
const FBIOBLANK: u32 = 0x4611;

const FB_BLANK_UNBLANK: libc::c_int = 0;
const FB_BLANK_POWERDOWN: libc::c_int = 4;

/// Validate that a framebuffer is the xRGB8888 layout this HAL assumes (32 bpp,
/// channel offsets red=16 / green=8 / blue=0). Returns a human-readable reason
/// on mismatch so [`FbDisplay::open`] can surface it instead of mapping a buffer
/// that renders as garbage.
fn check_xrgb8888(bpp: u32, r_off: u32, g_off: u32, b_off: u32) -> Result<(), String> {
    if bpp != 32 {
        return Err(format!("expected 32 bpp, got {bpp}"));
    }
    if (r_off, g_off, b_off) != (16, 8, 0) {
        return Err(format!(
            "expected channel offsets r16/g8/b0, got r{r_off}/g{g_off}/b{b_off}"
        ));
    }
    Ok(())
}

/// Byte offset of buffer `index` within the mapped region.
fn buffer_byte_offset(index: u32, yres: u32, stride: u32) -> usize {
    index as usize * yres as usize * stride as usize
}

/// The `yoffset` (in scanlines) that pans the display to buffer `index`.
fn pan_yoffset(index: u32, yres: u32) -> u32 {
    index * yres
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
struct FbBitfield {
    offset: u32,
    length: u32,
    msb_right: u32,
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
struct FbVarScreeninfo {
    xres: u32,
    yres: u32,
    xres_virtual: u32,
    yres_virtual: u32,
    xoffset: u32,
    yoffset: u32,
    bits_per_pixel: u32,
    grayscale: u32,
    red: FbBitfield,
    green: FbBitfield,
    blue: FbBitfield,
    transp: FbBitfield,
    nonstd: u32,
    activate: u32,
    height: u32,
    width: u32,
    accel_flags: u32,
    pixclock: u32,
    left_margin: u32,
    right_margin: u32,
    upper_margin: u32,
    lower_margin: u32,
    hsync_len: u32,
    vsync_len: u32,
    sync: u32,
    vmode: u32,
    rotate: u32,
    colorspace: u32,
    reserved: [u32; 4],
}

#[repr(C)]
struct FbFixScreeninfo {
    id: [u8; 16],
    smem_start: u64,
    smem_len: u32,
    type_: u32,
    type_aux: u32,
    visual: u32,
    xpanstep: u16,
    ypanstep: u16,
    ywrapstep: u16,
    line_length: u32,
    mmio_start: u64,
    mmio_len: u32,
    accel: u32,
    capabilities: u16,
    reserved: [u16; 2],
}

/// An mmap'd framebuffer ready for drawing via [`Surface`].
pub struct FbDisplay {
    map: NonNull<u8>,
    len: usize,            // total mapped bytes (buffer_len * num_buffers)
    buffer_len: usize,     // bytes in one buffer (stride * height)
    var: FbVarScreeninfo,  // kept for FBIOPAN_DISPLAY
    num_buffers: u32,      // 1 (single) or 2 (double-buffered)
    back: u32,             // index `surface()` draws into / `present()` flips to
    _file: File,           // keep the fd open for the life of the mapping
    pub width: u32,
    pub height: u32,
    pub stride: u32,
    pub bits_per_pixel: u32,
}

impl FbDisplay {
    /// Open a framebuffer device (e.g. `/dev/fb0`) as a single buffer. Draws via
    /// [`surface`](Self::surface) are immediately visible; [`present`](Self::present)
    /// is a no-op.
    pub fn open(path: &str) -> HalResult<Self> {
        Self::open_with_buffers(path, 1)
    }

    /// Open double-buffered: [`surface`](Self::surface) returns an off-screen
    /// back buffer and [`present`](Self::present) flips it on-screen via
    /// `FBIOPAN_DISPLAY`. This avoids tearing and lets an app hold the panel
    /// against the device's `ifm-local-setup` helper, which also writes
    /// `/dev/fb0` between redraws. If the driver cannot provide a second buffer
    /// (no room in `yres_virtual`), it transparently falls back to single
    /// buffering — check [`buffer_count`](Self::buffer_count).
    pub fn open_double_buffered(path: &str) -> HalResult<Self> {
        Self::open_with_buffers(path, 2)
    }

    fn open_with_buffers(path: &str, want: u32) -> HalResult<Self> {
        let file = OpenOptions::new().read(true).write(true).open(path)?;
        let fd = file.as_raw_fd();

        let mut var = FbVarScreeninfo::default();
        let mut fix: FbFixScreeninfo = unsafe { std::mem::zeroed() };
        unsafe {
            if libc::ioctl(fd, FBIOGET_VSCREENINFO as _, &mut var as *mut _) != 0 {
                return Err(std::io::Error::last_os_error().into());
            }
            if libc::ioctl(fd, FBIOGET_FSCREENINFO as _, &mut fix as *mut _) != 0 {
                return Err(std::io::Error::last_os_error().into());
            }
        }

        check_xrgb8888(var.bits_per_pixel, var.red.offset, var.green.offset, var.blue.offset)
            .map_err(HalError::UnsupportedFormat)?;

        let yres = var.yres;
        let stride = fix.line_length;
        let mut num_buffers = 1;

        if want >= 2 {
            // Ask the driver for a 2x-tall virtual surface; re-read to see what
            // it actually granted. Failure here is non-fatal — fall back to 1.
            var.yres_virtual = yres * 2;
            var.xres_virtual = var.xres;
            var.yoffset = 0;
            unsafe {
                libc::ioctl(fd, FBIOPUT_VSCREENINFO as _, &mut var as *mut _);
                libc::ioctl(fd, FBIOGET_VSCREENINFO as _, &mut var as *mut _);
            }
            if var.yres_virtual >= yres * 2 {
                num_buffers = 2;
            }
        }

        let buffer_len = stride as usize * yres as usize;
        let len = buffer_len * num_buffers as usize;
        let len_nz = NonZeroUsize::new(len)
            .ok_or_else(|| HalError::UnsupportedFormat("zero-size fb".into()))?;
        let map = unsafe {
            mmap(
                None,
                len_nz,
                ProtFlags::PROT_READ | ProtFlags::PROT_WRITE,
                MapFlags::MAP_SHARED,
                &file,
                0,
            )
            .map_err(std::io::Error::from)?
        };

        Ok(Self {
            map: map.cast::<u8>(),
            len,
            buffer_len,
            var,
            num_buffers,
            // Double-buffered: display buffer 0, draw into buffer 1 first.
            back: if num_buffers == 2 { 1 } else { 0 },
            _file: file,
            width: var.xres,
            height: yres,
            stride,
            bits_per_pixel: var.bits_per_pixel,
        })
    }

    /// Number of buffers actually in use: 2 if double-buffering was granted,
    /// else 1.
    pub fn buffer_count(&self) -> u32 {
        self.num_buffers
    }

    /// Borrow the back buffer as a [`Surface`] for drawing. Single-buffered, this
    /// is the visible buffer; double-buffered, it is off-screen until
    /// [`present`](Self::present).
    pub fn surface(&mut self) -> Surface<'_> {
        let off = buffer_byte_offset(self.back, self.height, self.stride);
        let buf = unsafe { std::slice::from_raw_parts_mut(self.map.as_ptr().add(off), self.buffer_len) };
        Surface::new(buf, self.width, self.height, self.stride)
    }

    /// Flip the back buffer on-screen. No-op when single-buffered.
    pub fn present(&mut self) -> HalResult<()> {
        if self.num_buffers < 2 {
            return Ok(());
        }
        let display_idx = self.back;
        self.var.yoffset = pan_yoffset(display_idx, self.height);
        self.var.xoffset = 0;
        let fd = self._file.as_raw_fd();
        let rc = unsafe { libc::ioctl(fd, FBIOPAN_DISPLAY as _, &mut self.var as *mut _) };
        if rc != 0 {
            return Err(std::io::Error::last_os_error().into());
        }
        self.back = 1 - self.back; // next frame draws into the now-hidden buffer
        Ok(())
    }

    /// Power the panel down (`on = true`) or back up (`on = false`) via
    /// `FBIOBLANK`.
    pub fn blank(&self, on: bool) -> HalResult<()> {
        let arg = if on { FB_BLANK_POWERDOWN } else { FB_BLANK_UNBLANK };
        let fd = self._file.as_raw_fd();
        let rc = unsafe { libc::ioctl(fd, FBIOBLANK as _, arg) };
        if rc != 0 {
            return Err(std::io::Error::last_os_error().into());
        }
        Ok(())
    }
}

impl Drop for FbDisplay {
    fn drop(&mut self) {
        unsafe {
            let _ = munmap(self.map.cast(), self.len);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn check_xrgb8888_accepts_the_device_layout() {
        assert!(check_xrgb8888(32, 16, 8, 0).is_ok());
    }

    #[test]
    fn check_xrgb8888_rejects_wrong_depth() {
        assert!(check_xrgb8888(16, 11, 5, 0).is_err());
    }

    #[test]
    fn check_xrgb8888_rejects_swapped_channels() {
        // BGRX (offsets r0/g8/b16) at 32 bpp must be rejected, not silently mis-rendered.
        let err = check_xrgb8888(32, 0, 8, 16).unwrap_err();
        assert!(err.contains("offsets"), "{err}");
    }

    #[test]
    fn buffer_offsets_and_pan_scale_with_index() {
        // 800x480, stride 3200 (the device's real geometry).
        assert_eq!(buffer_byte_offset(0, 480, 3200), 0);
        assert_eq!(buffer_byte_offset(1, 480, 3200), 480 * 3200);
        assert_eq!(pan_yoffset(0, 480), 0);
        assert_eq!(pan_yoffset(1, 480), 480);
    }
}
