// SPDX-License-Identifier: GPL-3.0-only
/// A mutable view over a packed pixel buffer (xRGB8888, 4 bytes/pixel).
pub struct Surface<'a> {
    pub buf: &'a mut [u8],
    pub width: u32,
    pub height: u32,
    pub stride: u32, // bytes per row (may exceed width*4)
}

impl<'a> Surface<'a> {
    pub fn new(buf: &'a mut [u8], width: u32, height: u32, stride: u32) -> Self {
        Self {
            buf,
            width,
            height,
            stride,
        }
    }

    /// Byte offset of pixel (x, y); None if out of bounds.
    pub fn offset(&self, x: u32, y: u32) -> Option<usize> {
        if x >= self.width || y >= self.height {
            return None;
        }
        Some((y * self.stride + x * 4) as usize)
    }

    pub fn put_pixel(&mut self, x: u32, y: u32, color: u32) {
        if let Some(o) = self.offset(x, y) {
            self.buf[o..o + 4].copy_from_slice(&color.to_le_bytes());
        }
    }

    pub fn fill(&mut self, color: u32) {
        for y in 0..self.height {
            for x in 0..self.width {
                self.put_pixel(x, y, color);
            }
        }
    }

    /// Copy a packed xRGB8888 source buffer into this surface, honouring both
    /// row strides. Copies `min(self.width*4, src_stride)` bytes per row for
    /// `min(self.height, src_bytes.len() / src_stride)` rows — so a tightly
    /// packed render buffer blits correctly into a hardware framebuffer whose
    /// row stride exceeds `width*4`. This is the stride-aware blit a renderer
    /// would otherwise hand-roll. Panics if `src_stride` is 0.
    pub fn copy_from(&mut self, src_bytes: &[u8], src_stride: u32) {
        assert!(src_stride > 0, "src_stride must be non-zero");
        let dst_stride = self.stride as usize;
        let src_stride = src_stride as usize;
        let row = (self.width as usize * 4).min(src_stride);
        let rows = (self.height as usize).min(src_bytes.len() / src_stride);
        for y in 0..rows {
            let s = y * src_stride;
            let d = y * dst_stride;
            self.buf[d..d + row].copy_from_slice(&src_bytes[s..s + row]);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn offset_respects_stride_and_bounds() {
        let mut buf = vec![0u8; 16 * 4 * 4]; // oversized; stride 64
        let s = Surface::new(&mut buf, 4, 4, 64);
        assert_eq!(s.offset(0, 0), Some(0));
        assert_eq!(s.offset(1, 0), Some(4));
        assert_eq!(s.offset(0, 1), Some(64));
        assert_eq!(s.offset(4, 0), None); // x out of bounds
        assert_eq!(s.offset(0, 4), None); // y out of bounds
    }

    #[test]
    fn put_pixel_writes_le_xrgb() {
        let mut buf = vec![0u8; 4 * 4 * 4];
        let mut s = Surface::new(&mut buf, 4, 4, 16);
        s.put_pixel(1, 0, 0x00FF8040);
        assert_eq!(&buf[4..8], &[0x40, 0x80, 0xFF, 0x00]);
    }

    #[test]
    fn fill_sets_every_pixel() {
        let mut buf = vec![0u8; 2 * 2 * 4];
        let mut s = Surface::new(&mut buf, 2, 2, 8);
        s.fill(0x11223344);
        assert!(buf.chunks(4).all(|p| p == [0x44, 0x33, 0x22, 0x11]));
    }

    #[test]
    fn copy_from_equal_layout_is_exact() {
        let src: Vec<u8> = (0..2 * 2 * 4).map(|i| i as u8).collect();
        let mut dst = vec![0u8; 2 * 2 * 4];
        let mut s = Surface::new(&mut dst, 2, 2, 8);
        s.copy_from(&src, 8);
        assert_eq!(dst, src);
    }

    #[test]
    fn copy_from_honours_larger_dst_stride() {
        // dst is 1px wide but padded to 16-byte rows; src is tightly packed 4 bytes/row.
        let src = vec![0xAAu8, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44]; // 2 rows
        let mut dst = vec![0u8; 16 * 2];
        let mut s = Surface::new(&mut dst, 1, 2, 16);
        s.copy_from(&src, 4);
        assert_eq!(&dst[0..4], &[0xAA, 0xBB, 0xCC, 0xDD]); // row 0 at offset 0
        assert_eq!(&dst[16..20], &[0x11, 0x22, 0x33, 0x44]); // row 1 at offset 16
        assert_eq!(&dst[4..16], &[0u8; 12]); // padding untouched
    }

    #[test]
    fn copy_from_clips_to_narrower_dst_width() {
        // src has 2px rows; dst is only 1px wide — copy just the first pixel per row.
        let src = vec![1u8, 2, 3, 4, 9, 9, 9, 9]; // one row: px0=1..4, px1=9..
        let mut dst = vec![0u8; 4];
        let mut s = Surface::new(&mut dst, 1, 1, 4);
        s.copy_from(&src, 8);
        assert_eq!(dst, vec![1, 2, 3, 4]);
    }

    #[test]
    fn copy_from_stops_at_dst_height() {
        let src = vec![0xFFu8; 4 * 5]; // 5 rows of 1px
        let mut dst = vec![0u8; 4 * 2]; // only 2 rows
        let mut s = Surface::new(&mut dst, 1, 2, 4);
        s.copy_from(&src, 4);
        assert_eq!(dst, vec![0xFF; 8]); // exactly 2 rows copied, no overflow
    }
}
