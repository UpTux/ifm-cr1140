// SPDX-License-Identifier: GPL-3.0-only
//! CR1140 dashboard app, built on the layered crates:
//!   - `cr1140-hal`   — framebuffer, evdev keypad, backlight, SoC temp
//!   - `cr1140-sdk`   — LED effects (LedDriver), system telemetry, device info
//!   - `cr1140-slint` — Slint TargetPixel + software-rendering Platform
//!
//! Slint's pure-Rust software renderer draws into a buffer of `Xrgb8888` pixels
//! that we blit to `/dev/fb0`. No winit, DRM/KMS, libinput, or fontconfig — so
//! it cross-compiles to the static `aarch64-unknown-linux-musl` target.
//!
//! Usage: cr1140-slint-demo [event-node]   (default /dev/input/event1)

slint::include_modules!();

#[cfg(target_os = "linux")]
fn main() -> Result<(), Box<dyn std::error::Error>> {
    use cr1140_hal::display::FbDisplay;
    use cr1140_hal::input::{Button, ButtonEvent, ButtonReader};
    use cr1140_hal::sys::{backlight_max, read_temp_c, set_backlight, BACKLIGHT, SOC_THERMAL_ZONE};
    use cr1140_sdk::device::{hostname, iface_ipv4, os_release, read_board_temp_c, read_operstate};
    use cr1140_sdk::led::{LedDriver, LedMode};
    use cr1140_sdk::metrics::{
        format_uptime, mem_used_percent, parse_meminfo, parse_uptime, read_loadavg, CpuSampler,
    };
    use cr1140_slint::{FbPlatform, Xrgb8888};
    use slint::platform::set_platform;
    use slint::platform::software_renderer::{MinimalSoftwareWindow, RepaintBufferType};
    use std::thread::sleep;
    use std::time::{Duration, Instant};

    // Keypad LED colors Enter cycles through: (name, r, g, b).
    const PALETTE: &[(&str, u8, u8, u8)] = &[
        ("off", 0, 0, 0),
        ("green", 0, 255, 0),
        ("yellow", 255, 255, 0),
        ("orange", 255, 90, 0),
        ("red", 255, 0, 0),
        ("blue", 0, 0, 255),
    ];

    // --- open hardware via the HAL ---
    // Double-buffer so we own the panel against `ifm-local-setup`, which also
    // writes /dev/fb0 between our redraws (falls back to single-buffer if the
    // driver can't grant a second buffer).
    let mut fb = FbDisplay::open_double_buffered("/dev/fb0")?;
    let (w, h) = (fb.width as usize, fb.height as usize);
    println!(
        "display {}x{} bpp {} stride {} ({} buffer(s))",
        fb.width, fb.height, fb.bits_per_pixel, fb.stride, fb.buffer_count()
    );
    // Locate the keypad by name; an explicit event node arg still overrides.
    let mut reader = match std::env::args().nth(1) {
        Some(node) => ButtonReader::open_nonblocking(&node)?,
        None => ButtonReader::open_keypad_nonblocking()?,
    };

    let bl_max = backlight_max(BACKLIGHT).unwrap_or(400).max(1);
    // Start mid-brightness so the Up/Down demo has headroom in both directions.
    let mut backlight = bl_max / 2;
    let _ = set_backlight(BACKLIGHT, backlight);

    // Keypad LED: a base color (Enter cycles PALETTE) × an animation mode (F1–F6).
    let mut led = LedDriver::new();
    let mut color_idx = 0usize; // "off"

    // --- set up Slint on our custom platform ---
    let window = MinimalSoftwareWindow::new(RepaintBufferType::ReusedBuffer);
    set_platform(Box::new(FbPlatform::new(window.clone())))
        .map_err(|e| format!("set_platform: {e}"))?;
    window.set_size(slint::PhysicalSize::new(fb.width, fb.height));

    // PlatformError isn't std::error::Error in no-std Slint, so map it by hand.
    let ui = AppWindow::new().map_err(|e| format!("AppWindow::new: {e}"))?;

    // Render target: tightly packed (pixel stride == width); blit handles the
    // hardware stride when copying into the framebuffer.
    let pixel_stride = w;
    let mut buf = vec![Xrgb8888::default(); pixel_stride * h];

    let mut cpu = CpuSampler::new();
    let mut last_metrics = Instant::now() - Duration::from_secs(2); // force immediate sample
    let frame_period = Duration::from_millis(16);

    let push_backlight = |ui: &AppWindow, value: u32| {
        let pct = (value * 100 / bl_max) as i32;
        ui.set_backlight_percent(pct);
        ui.set_backlight_text(format!("{pct} %").into());
    };
    // Reflect the current color+mode in the UI (name shown in the LED's own
    // color; "off" uses a muted gray so the label stays readable).
    let update_led_ui = |ui: &AppWindow, idx: usize, mode: LedMode| {
        let (name, r, g, b) = PALETTE[idx];
        if name == "off" {
            ui.set_led_text("off".into());
            ui.set_led_color(slint::Color::from_rgb_u8(0x5a, 0x6b, 0x7a));
        } else {
            ui.set_led_text(format!("{name} · {}", mode.name()).into());
            ui.set_led_color(slint::Color::from_rgb_u8(r, g, b));
        }
    };
    push_backlight(&ui, backlight);
    update_led_ui(&ui, color_idx, led.mode());

    // --- static device identity (read once) ---
    ui.set_hostname(hostname().into());
    let model = os_release("PRETTY_NAME").unwrap_or_else(|| "ecomatDisplay".into());
    let build = os_release("BUILD_ID").unwrap_or_default();
    let subtitle = if build.is_empty() { model } else { format!("{model} · build {build}") };
    ui.set_subtitle(subtitle.into());
    // eth0 IP/state is refreshed in the tick below, not once here: at boot the
    // app starts before networking is up, so a one-shot read shows a stale
    // "down" for the device's whole runtime.

    println!("ready; Slint dashboard on /dev/fb0 (Ctrl-C to exit)");

    loop {
        slint::platform::update_timers_and_animations();

        // --- input: drain everything queued, update UI / hardware ---
        while let Some(ev) = reader.poll_button()? {
            if let ButtonEvent::Pressed(btn) = ev {
                ui.set_last_key(format!("{btn:?}").into());
                match btn {
                    Button::Up => {
                        backlight = (backlight + bl_max / 10).min(bl_max);
                        let _ = set_backlight(BACKLIGHT, backlight);
                        push_backlight(&ui, backlight);
                    }
                    Button::Down => {
                        backlight = backlight.saturating_sub(bl_max / 10);
                        let _ = set_backlight(BACKLIGHT, backlight);
                        push_backlight(&ui, backlight);
                    }
                    Button::Enter => {
                        color_idx = (color_idx + 1) % PALETTE.len();
                        let (_, r, g, b) = PALETTE[color_idx];
                        led.set_color((r, g, b));
                        update_led_ui(&ui, color_idx, led.mode());
                    }
                    Button::F1
                    | Button::F2
                    | Button::F3
                    | Button::F4
                    | Button::F5
                    | Button::F6 => {
                        led.set_mode(match btn {
                            Button::F1 => LedMode::Solid,
                            Button::F2 => LedMode::Dim,
                            Button::F3 => LedMode::Pulse,
                            Button::F4 => LedMode::Blink,
                            Button::F5 => LedMode::Flash,
                            _ => LedMode::Heartbeat,
                        });
                        // A mode is invisible while the color is "off"; light it.
                        if PALETTE[color_idx].0 == "off" {
                            color_idx = 1; // green
                            let (_, r, g, b) = PALETTE[color_idx];
                            led.set_color((r, g, b));
                        }
                        update_led_ui(&ui, color_idx, led.mode());
                    }
                    _ => {}
                }
            }
        }

        // --- metrics: refresh ~1 Hz ---
        if last_metrics.elapsed() >= Duration::from_secs(1) {
            last_metrics = Instant::now();

            if let Some(p) = cpu.sample() {
                ui.set_cpu_percent(p);
                ui.set_cpu_text(format!("{p:.0} %").into());
            }
            if let Ok(s) = std::fs::read_to_string("/proc/meminfo") {
                if let Some((total, avail)) = parse_meminfo(&s) {
                    let p = mem_used_percent(total, avail);
                    ui.set_mem_percent(p);
                    ui.set_mem_text(format!("{p:.0} %").into());
                }
            }
            if let Ok(t) = read_temp_c(SOC_THERMAL_ZONE) {
                ui.set_temp_c(t);
                // map ~20..80 °C onto the bar
                ui.set_temp_percent(((t - 20.0) / 60.0 * 100.0).clamp(0.0, 100.0));
                ui.set_temp_text(format!("{t:.1}").into()); // unit appended in .slint
            }
            if let Ok(s) = std::fs::read_to_string("/proc/uptime") {
                if let Some(secs) = parse_uptime(&s) {
                    ui.set_uptime(format_uptime(secs).into());
                }
            }
            if let Some(bt) = read_board_temp_c() {
                ui.set_board_text(format!("Board {bt:.1} °C").into());
            }
            if let Some(l) = read_loadavg() {
                ui.set_load_text(format!("load {l:.2}").into());
            }
            ui.set_can_text(format!("CAN {}", read_operstate("can0")).into());
            let eth = iface_ipv4("eth0").unwrap_or_else(|| read_operstate("eth0"));
            ui.set_eth_text(format!("eth0 {eth}").into());
        }

        // --- drive the keypad LED animation (writes sysfs only on change) ---
        let _ = led.tick();

        // --- render only when dirty, then blit + flip the framebuffer ---
        let drawn = window.draw_if_needed(|renderer| {
            renderer.render(&mut buf, pixel_stride);
        });
        if drawn {
            // Reinterpret the packed Xrgb8888 render buffer as bytes (same LE
            // layout as the framebuffer); the HAL's stride-aware `copy_from`
            // handles the hardware row stride.
            let src_bytes =
                unsafe { std::slice::from_raw_parts(buf.as_ptr() as *const u8, buf.len() * 4) };
            fb.surface().copy_from(src_bytes, (pixel_stride * 4) as u32);
            let _ = fb.present();
        }

        sleep(frame_period);
    }
}

#[cfg(not(target_os = "linux"))]
fn main() {
    eprintln!("cr1140-slint-demo is Linux-only (fbdev + evdev)");
}
