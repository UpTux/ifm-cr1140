// SPDX-License-Identifier: GPL-3.0-only
//! Slint integration for the CR1140 — the framework-specific glue kept out of
//! both `cr1140-hal` (hardware) and `cr1140-sdk` (UI-agnostic): a
//! framebuffer-matching [`Xrgb8888`] `TargetPixel` and a minimal
//! software-rendering [`FbPlatform`].
//!
//! Pair with `cr1140-hal`'s `FbDisplay` to blit, and drive Slint from your own
//! super-loop. See `docs/slint-spike.md` for the rendering approach (Option B).

mod pixel;
mod platform;

pub use pixel::Xrgb8888;
pub use platform::FbPlatform;
