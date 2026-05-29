//! Option B spike: a Slint UI driven by the CR1140 HAL.
//!
//! Slint's pure-Rust software renderer draws into a buffer of [`Xrgb8888`]
//! pixels; we blit that to `/dev/fb0` through `cr1140_hal`'s mmap'd `Surface`.
//! Input comes from the evdev keypad via `cr1140_hal::input::ButtonReader`, and
//! the dashboard shows live values read from `/proc` and sysfs.
//!
//! No winit, no DRM/KMS, no libinput, no fontconfig — so it still cross-compiles
//! to the static `aarch64-unknown-linux-musl` target.
//!
//! Usage: cr1140-slint-demo [event-node]   (default /dev/input/event1)

slint::include_modules!();

mod metrics;
mod pixel;
#[cfg(target_os = "linux")]
mod platform;

#[cfg(target_os = "linux")]
fn main() -> Result<(), Box<dyn std::error::Error>> {
    use cr1140_hal::display::FbDisplay;
    use cr1140_hal::input::{Button, ButtonEvent, ButtonReader};
    use cr1140_hal::sys::{backlight_max, read_temp_c, set_backlight, set_led};
    use metrics::{
        format_uptime, hostname, iface_ipv4, mem_used_percent, os_release_value, parse_meminfo,
        parse_uptime, read_board_temp_c, read_loadavg, read_operstate, CpuSampler,
    };
    use pixel::Xrgb8888;
    use platform::FbPlatform;
    use slint::platform::software_renderer::{MinimalSoftwareWindow, RepaintBufferType};
    use slint::platform::set_platform;
    use std::thread::sleep;
    use std::time::{Duration, Instant};

    const BACKLIGHT: &str = "backlight";
    const LED: &str = "green:kbd_backlight";

    let event_node = std::env::args().nth(1).unwrap_or_else(|| "/dev/input/event1".into());

    // --- open hardware via the HAL ---
    let mut fb = FbDisplay::open("/dev/fb0")?;
    let (w, h) = (fb.width as usize, fb.height as usize);
    println!("display {}x{} bpp {} stride {}", fb.width, fb.height, fb.bits_per_pixel, fb.stride);
    let mut reader = ButtonReader::open_nonblocking(&event_node)?;

    let bl_max = backlight_max(BACKLIGHT).unwrap_or(400).max(1);
    // Start mid-brightness so the Up/Down demo has headroom in both directions.
    let mut backlight = bl_max / 2;
    let _ = set_backlight(BACKLIGHT, backlight);
    let mut led_on = false;
    let _ = set_led(LED, 0);

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
    push_backlight(&ui, backlight);

    // --- static device identity (read once) ---
    ui.set_hostname(hostname().into());
    let osr = std::fs::read_to_string("/etc/os-release").unwrap_or_default();
    let model = os_release_value(&osr, "PRETTY_NAME").unwrap_or_else(|| "ecomatDisplay".into());
    let build = os_release_value(&osr, "BUILD_ID").unwrap_or_default();
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
                        led_on = !led_on;
                        let _ = set_led(LED, if led_on { 255 } else { 0 });
                        ui.set_led_on(led_on);
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
            if let Ok(t) = read_temp_c(0) {
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

        // --- render only when dirty, then blit to the framebuffer ---
        let drawn = window.draw_if_needed(|renderer| {
            renderer.render(&mut buf, pixel_stride);
        });
        if drawn {
            blit(&buf, pixel_stride, &mut fb);
        }

        sleep(frame_period);
    }

    // Copy the render buffer (packed at `pixel_stride`) into the framebuffer,
    // honouring the hardware row stride. Both sides are little-endian xRGB8888,
    // so a raw byte copy is correct on aarch64.
    fn blit(buf: &[Xrgb8888], pixel_stride: usize, fb: &mut FbDisplay) {
        let w = fb.width as usize;
        let h = fb.height as usize;
        let dst_stride = fb.stride as usize;
        let surf = fb.surface();
        for y in 0..h {
            let src = &buf[y * pixel_stride..y * pixel_stride + w];
            let src_bytes =
                unsafe { std::slice::from_raw_parts(src.as_ptr() as *const u8, w * 4) };
            let dst_off = y * dst_stride;
            surf.buf[dst_off..dst_off + w * 4].copy_from_slice(src_bytes);
        }
    }
}

#[cfg(not(target_os = "linux"))]
fn main() {
    eprintln!("cr1140-slint-demo is Linux-only (fbdev + evdev)");
}
