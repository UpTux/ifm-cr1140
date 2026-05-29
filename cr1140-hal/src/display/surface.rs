/// A mutable view over a packed pixel buffer (xRGB8888, 4 bytes/pixel).
pub struct Surface<'a> {
    pub buf: &'a mut [u8],
    pub width: u32,
    pub height: u32,
    pub stride: u32, // bytes per row (may exceed width*4)
}

impl<'a> Surface<'a> {
    pub fn new(buf: &'a mut [u8], width: u32, height: u32, stride: u32) -> Self {
        Self { buf, width, height, stride }
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
}
