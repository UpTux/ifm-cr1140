//! Linux framebuffer (fbdev) backend. The CR1140 runs linuxfb at 800x480,
//! so `/dev/fb0` is the display path.
use crate::display::Surface;
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
const FBIOGET_FSCREENINFO: u32 = 0x4602;

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
    len: usize,
    _file: File, // keep the fd open for the life of the mapping
    pub width: u32,
    pub height: u32,
    pub stride: u32,
    pub bits_per_pixel: u32,
}

impl FbDisplay {
    /// Open a framebuffer device (e.g. `/dev/fb0`).
    pub fn open(path: &str) -> std::io::Result<Self> {
        let file = OpenOptions::new().read(true).write(true).open(path)?;
        let fd = file.as_raw_fd();

        let mut var = FbVarScreeninfo::default();
        let mut fix: FbFixScreeninfo = unsafe { std::mem::zeroed() };
        unsafe {
            if libc::ioctl(fd, FBIOGET_VSCREENINFO as _, &mut var as *mut _) != 0 {
                return Err(std::io::Error::last_os_error());
            }
            if libc::ioctl(fd, FBIOGET_FSCREENINFO as _, &mut fix as *mut _) != 0 {
                return Err(std::io::Error::last_os_error());
            }
        }

        let len = fix.line_length as usize * var.yres as usize;
        let len_nz = NonZeroUsize::new(len)
            .ok_or_else(|| std::io::Error::new(std::io::ErrorKind::InvalidData, "zero-size fb"))?;
        let map = unsafe {
            mmap(
                None,
                len_nz,
                ProtFlags::PROT_READ | ProtFlags::PROT_WRITE,
                MapFlags::MAP_SHARED,
                &file,
                0,
            )?
        };

        Ok(Self {
            map: map.cast::<u8>(),
            len,
            _file: file,
            width: var.xres,
            height: var.yres,
            stride: fix.line_length,
            bits_per_pixel: var.bits_per_pixel,
        })
    }

    /// Borrow the framebuffer as a [`Surface`] for drawing.
    pub fn surface(&mut self) -> Surface<'_> {
        let buf = unsafe { std::slice::from_raw_parts_mut(self.map.as_ptr(), self.len) };
        Surface::new(buf, self.width, self.height, self.stride)
    }
}

impl Drop for FbDisplay {
    fn drop(&mut self) {
        unsafe {
            let _ = munmap(self.map.cast(), self.len);
        }
    }
}
