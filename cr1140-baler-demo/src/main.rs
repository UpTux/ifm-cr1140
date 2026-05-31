// SPDX-License-Identifier: GPL-3.0-only
//! CR1140 round-baler operator panel — a second reference application built on
//! the layered crates:
//!   - `cr1140-hal`   — framebuffer, evdev keypad, CAN, retain EEPROM
//!   - `cr1140-sdk`   — `ShutdownGuard`, reflash-surviving `retain::Store`
//!   - `cr1140-slint` — Slint TargetPixel + software-rendering Platform
//!
//! Slint's pure-Rust software renderer draws into a buffer of `Xrgb8888` pixels
//! that we blit to `/dev/fb0`. No winit, DRM/KMS, libinput, or fontconfig — so
//! it cross-compiles to the static `aarch64-unknown-linux-musl` target.
//!
//! Light-theme, multi-screen, soft-key-footer UI. See `PRD.md` for the spec.
//!
//! Usage: cr1140-baler-demo [event-node]   (default: keypad by name)

slint::include_modules!();

mod can;
mod counter;
mod knives;
mod router;
mod wrapping;

#[cfg(target_os = "linux")]
mod app {
    //! Linux-only UI glue: maps model state onto the generated `AppWindow`
    //! properties, with change-detection so we only repaint when something moves.
    use crate::counter::Counter;
    use crate::knives::Knives;
    use crate::router::{Router, Screen};
    use crate::wrapping::{WrapState, Wrapping};
    use crate::AppWindow;

    /// UI screen index for the `screen` property
    /// (0 Menu · 1 Dashboard · 2 Bale Counter · 3 Knives · 4 Wrapping · 5 Telemetry).
    pub fn screen_index(s: Screen) -> i32 {
        match s {
            Screen::Menu => 0,
            Screen::Dashboard => 1,
            Screen::BaleCounter => 2,
            Screen::Knives => 3,
            Screen::Wrapping => 4,
            Screen::Telemetry => 5,
        }
    }

    /// Soft-key footer labels (F1..F6) for the active screen — per the PRD/mockup.
    pub fn footer_for(s: Screen) -> [&'static str; 6] {
        match s {
            Screen::Menu => ["", "", "", "", "", "Exit"],
            // Combined dashboard: counter keys + wrapping keys on one screen.
            Screen::Dashboard => [
                "+1 Bale",
                "Start Wrap",
                "Reset Sess",
                "Reset Total",
                "Cancel",
                "Back",
            ],
            Screen::BaleCounter => ["Reset Sess", "+1 Bale", "Reset Total", "", "", "Back"],
            Screen::Knives => ["Toggle", "", "", "", "", "Back"],
            Screen::Wrapping => ["Start Wrap", "Cancel", "", "", "", "Back"],
            // Up/Down adjust the backlight (shown by the centre d-pad hint).
            Screen::Telemetry => ["", "", "", "", "", "Back"],
        }
    }

    /// Pre-formatted telemetry view, filled by the main loop (~1 Hz) and pushed
    /// to the UI by [`refresh`]. Strings are ASCII / markup-embedded glyphs only.
    #[derive(Default)]
    pub struct Tele {
        pub cpu: String,
        pub mem: String,
        pub temp: String,
        pub uptime: String,
        pub net: String,
        pub backlight_text: String,
        pub backlight_pct: i32,
    }

    fn set_sk(ui: &AppWindow, i: usize, label: &str) {
        match i {
            0 => ui.set_sk1(label.into()),
            1 => ui.set_sk2(label.into()),
            2 => ui.set_sk3(label.into()),
            3 => ui.set_sk4(label.into()),
            4 => ui.set_sk5(label.into()),
            _ => ui.set_sk6(label.into()),
        }
    }

    /// Last-pushed UI values, so `refresh` only calls a setter when the value
    /// actually changed (Slint marks a property dirty on every `set`, which would
    /// otherwise force a full repaint every frame).
    #[derive(Default)]
    pub struct UiCache {
        clock: String,
        screen: Option<i32>,
        menu_cursor: Option<i32>,
        title: String,
        sk: [String; 6],
        session: String,
        total: String,
        avg: String,
        bph: String,
        net: String,
        reset_armed: Option<bool>,
        knives_in: Option<bool>,
        wrap_active: Option<bool>,
        wrap_progress: Option<f32>,
        tele_cpu: String,
        tele_mem: String,
        tele_temp: String,
        tele_uptime: String,
        tele_net: String,
        backlight_text: String,
        backlight_pct: Option<i32>,
    }

    // Push the bale-counter fields (shared by the Bale Counter and Dashboard
    // screens), only where changed.
    fn push_counter(ui: &AppWindow, cache: &mut UiCache, counter: &Counter, now_ms: u64) {
        let session = counter.session().to_string();
        if cache.session != session {
            ui.set_session_count(session.clone().into());
            cache.session = session;
        }
        let total = counter.total().to_string();
        if cache.total != total {
            ui.set_total_count(total.clone().into());
            cache.total = total;
        }
        let avg = format!("{:.2}", counter.avg_diameter_m());
        if cache.avg != avg {
            ui.set_avg_diameter(avg.clone().into());
            cache.avg = avg;
        }
        let bph = format!("{:.0}", counter.bales_per_hour(now_ms));
        if cache.bph != bph {
            ui.set_bales_per_hour(bph.clone().into());
            cache.bph = bph;
        }
        let net = format!("{:.0}", counter.net_used_pct());
        if cache.net != net {
            ui.set_net_used(net.clone().into());
            cache.net = net;
        }
        let armed = counter.reset_total_armed(now_ms);
        if cache.reset_armed != Some(armed) {
            ui.set_reset_armed(armed);
            cache.reset_armed = Some(armed);
        }
    }

    // Push the wrapping state + progress (shared by the Wrapping and Dashboard
    // screens), only where changed.
    fn push_wrapping(ui: &AppWindow, cache: &mut UiCache, wrapping: &Wrapping, now_ms: u64) {
        let active = wrapping.state(now_ms) == WrapState::Wrapping;
        if cache.wrap_active != Some(active) {
            ui.set_wrapping_active(active);
            cache.wrap_active = Some(active);
        }
        let p = wrapping.progress(now_ms);
        if cache.wrap_progress.map_or(true, |c| (c - p).abs() > 0.002) {
            ui.set_wrap_progress(p);
            cache.wrap_progress = Some(p);
        }
    }

    /// Push the current model state into the UI, only where it changed.
    #[allow(clippy::too_many_arguments)]
    pub fn refresh(
        ui: &AppWindow,
        cache: &mut UiCache,
        router: &Router,
        counter: &Counter,
        knives: &Knives,
        wrapping: &Wrapping,
        tele: &Tele,
        now_ms: u64,
        clock: &str,
    ) {
        if cache.clock != clock {
            ui.set_clock(clock.into());
            cache.clock = clock.to_string();
        }

        let screen = router.screen();
        let idx = screen_index(screen);
        if cache.screen != Some(idx) {
            ui.set_screen(idx);
            cache.screen = Some(idx);
        }
        let cursor = router.menu_cursor() as i32;
        if cache.menu_cursor != Some(cursor) {
            ui.set_menu_cursor(cursor);
            cache.menu_cursor = Some(cursor);
        }
        let title = router.screen_title();
        if cache.title != title {
            ui.set_screen_title(title.into());
            cache.title = title.to_string();
        }
        let footer = footer_for(screen);
        for (i, label) in footer.iter().enumerate() {
            if cache.sk[i] != *label {
                set_sk(ui, i, label);
                cache.sk[i] = label.to_string();
            }
        }

        match screen {
            Screen::Dashboard => {
                push_counter(ui, cache, counter, now_ms);
                push_wrapping(ui, cache, wrapping, now_ms);
            }
            Screen::BaleCounter => push_counter(ui, cache, counter, now_ms),
            Screen::Wrapping => push_wrapping(ui, cache, wrapping, now_ms),
            Screen::Knives => {
                let ki = knives.is_in();
                if cache.knives_in != Some(ki) {
                    ui.set_knives_in(ki);
                    cache.knives_in = Some(ki);
                }
            }
            Screen::Telemetry => {
                if cache.tele_cpu != tele.cpu {
                    ui.set_tele_cpu(tele.cpu.clone().into());
                    cache.tele_cpu = tele.cpu.clone();
                }
                if cache.tele_mem != tele.mem {
                    ui.set_tele_mem(tele.mem.clone().into());
                    cache.tele_mem = tele.mem.clone();
                }
                if cache.tele_temp != tele.temp {
                    ui.set_tele_temp(tele.temp.clone().into());
                    cache.tele_temp = tele.temp.clone();
                }
                if cache.tele_uptime != tele.uptime {
                    ui.set_tele_uptime(tele.uptime.clone().into());
                    cache.tele_uptime = tele.uptime.clone();
                }
                if cache.tele_net != tele.net {
                    ui.set_tele_net(tele.net.clone().into());
                    cache.tele_net = tele.net.clone();
                }
                if cache.backlight_text != tele.backlight_text {
                    ui.set_backlight_text(tele.backlight_text.clone().into());
                    cache.backlight_text = tele.backlight_text.clone();
                }
                if cache.backlight_pct != Some(tele.backlight_pct) {
                    ui.set_backlight_percent(tele.backlight_pct);
                    cache.backlight_pct = Some(tele.backlight_pct);
                }
            }
            Screen::Menu => {}
        }
    }
}

#[cfg(target_os = "linux")]
fn main() -> Result<(), Box<dyn std::error::Error>> {
    use crate::can::BalerBus;
    use crate::counter::{BalerRetain, Counter};
    use crate::knives::Knives;
    use crate::router::{Effect, Nav, Router, Screen};
    use crate::wrapping::Wrapping;
    use cr1140_hal::display::FbDisplay;
    use cr1140_hal::input::{Button, ButtonEvent, ButtonReader};
    use cr1140_hal::sys::{backlight_max, set_backlight, Nvmem, BACKLIGHT, BACKLIGHT_MAX_HINT};
    use cr1140_sdk::device::{iface_ipv4, read_operstate};
    use cr1140_sdk::metrics::format_uptime;
    use cr1140_sdk::retain::Store as RetainStore;
    use cr1140_sdk::{ShutdownGuard, Telemetry};
    use cr1140_slint::{FbPlatform, Xrgb8888};
    use slint::platform::set_platform;
    use slint::platform::software_renderer::{MinimalSoftwareWindow, RepaintBufferType};
    use std::thread::sleep;
    use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

    tracing_subscriber::fmt::init();

    // --- CLI: optional event-node positional + selectable CAN iface ---
    //   cr1140-baler-demo [event-node] [--can <iface>]
    // Defaults: keypad located by name, CAN interface "can0".
    let mut input_node: Option<String> = None;
    let mut can_iface = String::from("can0");
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--can" => {
                if let Some(iface) = args.next() {
                    can_iface = iface;
                }
            }
            node => input_node = Some(node.to_string()),
        }
    }

    // --- open hardware via the HAL ---
    // Double-buffer so we own the panel against `ifm-local-setup`, which also
    // writes /dev/fb0 between our redraws (falls back to single-buffer if the
    // driver can't grant a second buffer).
    let mut fb = FbDisplay::open_double_buffered("/dev/fb0")?;
    let (w, h) = (fb.width as usize, fb.height as usize);
    tracing::info!(
        "display {}x{} bpp {} stride {} ({} buffer(s))",
        fb.width,
        fb.height,
        fb.bits_per_pixel,
        fb.stride,
        fb.buffer_count()
    );
    // Locate the keypad by name; an explicit event node arg still overrides.
    let mut reader = match input_node {
        Some(node) => ButtonReader::open_nonblocking(&node)?,
        None => ButtonReader::open_keypad_nonblocking()?,
    };

    // Outbound command seam: real SocketCAN when present, logged frames otherwise.
    let bus = BalerBus::open(&can_iface);

    // No backlight/LED capture needed for this panel, but the guard still gives
    // us the opt-in SIGINT/SIGTERM flag for a clean exit (this binary is
    // standalone, so it owns the handler).
    let guard = ShutdownGuard::capture()?;
    guard.install_signal_handler()?;

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
    let frame_period = Duration::from_millis(16);

    // --- static header chrome (placeholder strings until a real machine bus) ---
    ui.set_machine("CR-BALER 9000".into());
    ui.set_field("North 40".into());
    ui.set_status("READY".into());
    ui.set_iso("ISO".into());

    // Home-menu labels come from the router (single source of truth for order).
    let entries = Router::menu_entries();
    ui.set_menu1(entries[0].into());
    ui.set_menu2(entries[1].into());
    ui.set_menu3(entries[2].into());
    ui.set_menu4(entries[3].into());
    ui.set_menu5(entries[4].into());

    // --- retain: reflash-surviving lifetime total on the SPI EEPROM ---
    // The demo owns the whole retain region (sole `BalerRetain` blob). If the
    // EEPROM is unavailable (e.g. a dev box), run without persistence rather
    // than refuse to start.
    let retain: Option<RetainStore<BalerRetain>> = match Nvmem::open_retain() {
        Ok(nv) => match RetainStore::open(nv) {
            Ok(store) => Some(store),
            Err(e) => {
                tracing::warn!(error = %e, "retain store unavailable; lifetime total won't persist");
                None
            }
        },
        Err(e) => {
            tracing::warn!(error = %e, "retain EEPROM unavailable; lifetime total won't persist");
            None
        }
    };
    let loaded = retain
        .as_ref()
        .and_then(|s| s.load_or_default().ok())
        .unwrap_or_default();

    // Persist the current lifetime total to retain (write-only-if-changed inside
    // the store; we additionally debounce calls so bale bursts coalesce).
    let persist = |store: &Option<RetainStore<BalerRetain>>, counter: &Counter| {
        if let Some(store) = store {
            if let Err(e) = store.save(&counter.to_retain()) {
                tracing::warn!(error = %e, "failed to persist lifetime total");
            }
        }
    };

    // --- models + navigation ---
    let mut router = Router::new();
    let mut counter = Counter::from_retain(&loaded);
    let mut knives = Knives::new();
    let mut wrapping = Wrapping::new();

    // --- telemetry + backlight (Telemetry screen) ---
    // No HAL getter for the current brightness, so set a known value at startup
    // (full) — the displayed % then matches reality. `ShutdownGuard` restores the
    // pre-launch backlight on exit. Up/Down on the Telemetry screen adjust it.
    let bl_max = backlight_max(BACKLIGHT)
        .unwrap_or(BACKLIGHT_MAX_HINT)
        .max(1);
    let mut backlight = bl_max;
    let _ = set_backlight(BACKLIGHT, backlight);
    let bl_step = (bl_max / 10).max(1);
    let mut telemetry = Telemetry::new();
    let mut tele = app::Tele::default();
    let mut last_sample_ms: Option<u64> = None;
    // Helper: format the backlight % into the tele view.
    let backlight_view = |tele: &mut app::Tele, value: u32| {
        let pct = (value * 100 / bl_max) as i32;
        tele.backlight_pct = pct;
        tele.backlight_text = format!("{pct} %");
    };
    backlight_view(&mut tele, backlight);

    // Monotonic clock for the injected-time model methods (debounce, bales/hr,
    // reset-arm window, wrap cycle).
    let start = Instant::now();
    let mut cache = app::UiCache::default();

    tracing::info!("ready; baler panel on /dev/fb0 (F6 or Ctrl-C to exit)");

    let mut running = true;
    while running && !guard.should_shutdown() {
        slint::platform::update_timers_and_animations();
        let now_ms = start.elapsed().as_millis() as u64;

        // --- input: drain everything queued; keys are screen-specific ---
        while let Some(ev) = reader.poll_button()? {
            if let ButtonEvent::Pressed(btn) = ev {
                match router.screen() {
                    Screen::Menu => {
                        let nav = match btn {
                            Button::Up => Some(Nav::Up),
                            Button::Down => Some(Nav::Down),
                            Button::Enter => Some(Nav::Enter),
                            Button::F6 => Some(Nav::Back),
                            _ => None,
                        };
                        if let Some(nav) = nav {
                            if router.handle(nav) == Effect::Exit {
                                running = false;
                            }
                        }
                    }
                    // Combined dashboard: counter keys (F1/F3/F4) + wrapping
                    // keys (F2/F5) on one screen.
                    Screen::Dashboard => match btn {
                        Button::F1 => bus.send(&counter.add_bale(now_ms)),
                        Button::F2 => {
                            if let Some(cmd) = wrapping.start(now_ms) {
                                bus.send(&cmd);
                            }
                        }
                        Button::F3 => counter.reset_session(),
                        Button::F4 => {
                            let _ = counter.press_reset_total(now_ms);
                        }
                        Button::F5 => wrapping.cancel(),
                        Button::F6 => {
                            counter.disarm_reset_total();
                            router.handle(Nav::Back);
                        }
                        _ => {}
                    },
                    Screen::BaleCounter => match btn {
                        Button::F1 => counter.reset_session(),
                        Button::F2 => bus.send(&counter.add_bale(now_ms)),
                        Button::F3 => {
                            // First press arms the double-confirm; second within
                            // the window commits (zeroes the total, marks dirty).
                            let _ = counter.press_reset_total(now_ms);
                        }
                        Button::F6 => {
                            // Leaving the screen auto-disarms a pending reset.
                            counter.disarm_reset_total();
                            router.handle(Nav::Back);
                        }
                        _ => {}
                    },
                    Screen::Knives => match btn {
                        Button::F1 => bus.send(&knives.toggle()),
                        Button::F6 => {
                            router.handle(Nav::Back);
                        }
                        _ => {}
                    },
                    Screen::Wrapping => match btn {
                        Button::F1 => {
                            if let Some(cmd) = wrapping.start(now_ms) {
                                bus.send(&cmd);
                            }
                        }
                        Button::F2 => wrapping.cancel(),
                        Button::F6 => {
                            router.handle(Nav::Back);
                        }
                        _ => {}
                    },
                    Screen::Telemetry => match btn {
                        Button::Up => {
                            backlight = (backlight + bl_step).min(bl_max);
                            let _ = set_backlight(BACKLIGHT, backlight);
                            backlight_view(&mut tele, backlight);
                        }
                        Button::Down => {
                            backlight = backlight.saturating_sub(bl_step);
                            let _ = set_backlight(BACKLIGHT, backlight);
                            backlight_view(&mut tele, backlight);
                        }
                        Button::F6 => {
                            router.handle(Nav::Back);
                        }
                        _ => {}
                    },
                }
            }
        }

        // --- retain: debounced persist of the lifetime total ---
        if counter.needs_persist(now_ms) {
            persist(&retain, &counter);
            counter.mark_persisted();
        }

        // --- telemetry: refresh ~1 Hz via the SDK's aggregated snapshot ---
        if last_sample_ms.map_or(true, |t| now_ms.saturating_sub(t) >= 1000) {
            last_sample_ms = Some(now_ms);
            let snap = telemetry.sample();
            tele.cpu = snap.cpu_percent.map_or("—".into(), |p| format!("{p:.0} %"));
            tele.mem = snap
                .mem
                .map_or("—".into(), |m| format!("{:.0} %", m.used_percent()));
            // The "°C" unit is appended in the .slint markup so its glyph embeds.
            tele.temp = snap.soc_temp_c.map_or("—".into(), |t| format!("{t:.1}"));
            tele.uptime = snap.uptime_secs.map_or("—".into(), format_uptime);
            let can = read_operstate("can0");
            let eth = iface_ipv4("eth0").unwrap_or_else(|| read_operstate("eth0"));
            tele.net = format!("CAN {can} / eth0 {eth}");
        }

        // --- live clock from system time (UTC), pushed only on change below ---
        let secs = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_secs();
        let clock = format!(
            "{:02}:{:02}:{:02}",
            (secs / 3600) % 24,
            (secs / 60) % 60,
            secs % 60
        );

        // --- push model state into the UI (change-detected) ---
        app::refresh(
            &ui, &mut cache, &router, &counter, &knives, &wrapping, &tele, now_ms, &clock,
        );

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

    // Always flush a pending lifetime total on graceful exit (debounce may not
    // have elapsed) — within the retain module's low-frequency envelope.
    if counter.is_dirty() {
        persist(&retain, &counter);
        counter.mark_persisted();
    }

    tracing::info!("shutting down baler panel");
    Ok(())
}

#[cfg(not(target_os = "linux"))]
fn main() {
    eprintln!("cr1140-baler-demo is Linux-only (fbdev + evdev)");
}
