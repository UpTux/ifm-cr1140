// SPDX-License-Identifier: GPL-3.0-only
//! A `TargetPixel` matching the CR1140 framebuffer layout so Slint's software
//! renderer can render directly into a buffer we blit to `/dev/fb0` with no
//! per-pixel format conversion.
//!
//! The framebuffer is xRGB8888: in memory each pixel is the little-endian bytes
//! `[B, G, R, X]`. We store the pixel as `0x00RRGGBB` in a `u32`, so
//! `u32::to_le_bytes()` yields exactly `[B, G, R, 0x00]` — identical to the
//! convention in `cr1140_hal::display::Surface`.

use slint::platform::software_renderer::{PremultipliedRgbaColor, TargetPixel};

#[derive(Clone, Copy, Default, PartialEq, Eq, Debug)]
#[repr(transparent)]
pub struct Xrgb8888(pub u32);

impl Xrgb8888 {
    #[inline]
    fn channels(self) -> (u32, u32, u32) {
        ((self.0 >> 16) & 0xff, (self.0 >> 8) & 0xff, self.0 & 0xff)
    }
}

impl TargetPixel for Xrgb8888 {
    /// Source-over blend. `color`'s RGB are already premultiplied by its alpha,
    /// so `out = src + dst * (1 - alpha)`.
    fn blend(&mut self, color: PremultipliedRgbaColor) {
        let inv = 255 - color.alpha as u32;
        let (dr, dg, db) = self.channels();
        let r = color.red as u32 + (dr * inv) / 255;
        let g = color.green as u32 + (dg * inv) / 255;
        let b = color.blue as u32 + (db * inv) / 255;
        self.0 = (r.min(255) << 16) | (g.min(255) << 8) | b.min(255);
    }

    fn from_rgb(red: u8, green: u8, blue: u8) -> Self {
        Xrgb8888((red as u32) << 16 | (green as u32) << 8 | blue as u32)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn from_rgb_packs_to_fb_byte_order() {
        // R=0xFF G=0x80 B=0x40 -> stored 0x00FF8040 -> LE bytes [0x40,0x80,0xFF,0x00]
        let p = Xrgb8888::from_rgb(0xFF, 0x80, 0x40);
        assert_eq!(p.0, 0x00FF_8040);
        assert_eq!(p.0.to_le_bytes(), [0x40, 0x80, 0xFF, 0x00]);
    }

    #[test]
    fn opaque_blend_replaces_destination() {
        let mut p = Xrgb8888::from_rgb(0, 0, 0);
        // fully opaque red (premultiplied: alpha=255, rgb already *1)
        p.blend(PremultipliedRgbaColor {
            red: 200,
            green: 100,
            blue: 50,
            alpha: 255,
        });
        assert_eq!(p, Xrgb8888::from_rgb(200, 100, 50));
    }

    #[test]
    fn transparent_blend_keeps_destination() {
        let mut p = Xrgb8888::from_rgb(10, 20, 30);
        p.blend(PremultipliedRgbaColor {
            red: 0,
            green: 0,
            blue: 0,
            alpha: 0,
        });
        assert_eq!(p, Xrgb8888::from_rgb(10, 20, 30));
    }

    #[test]
    fn half_alpha_blends_halfway() {
        // dst white, src = premultiplied black at alpha 128 (rgb=0) -> ~half white
        let mut p = Xrgb8888::from_rgb(255, 255, 255);
        p.blend(PremultipliedRgbaColor {
            red: 0,
            green: 0,
            blue: 0,
            alpha: 128,
        });
        let (r, g, b) = p.channels();
        assert!((126..=128).contains(&r), "r={r}");
        assert_eq!(r, g);
        assert_eq!(g, b);
    }
}
