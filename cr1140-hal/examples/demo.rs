//! All-module demo with a continuous redraw loop: repaints the screen every
//! frame (~30 fps), polls buttons non-blockingly, drives an LED, reads
//! temperature, and best-effort opens CAN. Replaces the CODESYS visu.
//!
//! Each frame draws: a top band in the last-pressed button's color, a dark
//! background, and a white bar sweeping left-to-right (visible proof of the
//! continuous redraw — and it keeps re-asserting our content on the fb).
//!
//! Usage: demo [event-node] [led-name] [can-iface]
//!   defaults: /dev/input/event1  (no led)  can0

#[cfg(target_os = "linux")]
fn main() -> std::io::Result<()> {
    use cr1140_hal::can::CanBus;
    use cr1140_hal::display::FbDisplay;
    use cr1140_hal::input::{Button, ButtonEvent, ButtonReader};
    use cr1140_hal::sys::{read_temp_c, set_led};
    use std::thread::sleep;
    use std::time::Duration;

    let mut args = std::env::args().skip(1);
    let event_node = args.next().unwrap_or_else(|| "/dev/input/event1".into());
    let led = args.next();
    let can_iface = args.next().unwrap_or_else(|| "can0".into());

    let mut fb = FbDisplay::open("/dev/fb0")?;
    println!(
        "display {}x{} bpp {}",
        fb.width, fb.height, fb.bits_per_pixel
    );

    match read_temp_c(0) {
        Ok(t) => println!("temp = {t:.1} C"),
        Err(e) => println!("temp unavailable: {e}"),
    }

    let can = CanBus::open(&can_iface)
        .map_err(|e| println!("CAN {can_iface} unavailable: {e}"))
        .ok();

    let mut reader = ButtonReader::open_nonblocking(&event_node)?;
    println!("ready; continuous redraw at ~30 fps (Ctrl-C to exit)");

    const BG: u32 = 0x00_10_10_10;
    let mut band_color: u32 = BG;
    let mut led_on = false;
    let mut frame: u32 = 0;
    let frame_period = Duration::from_millis(33); // ~30 fps

    loop {
        // --- input: drain everything queued since last frame ---
        while let Some(ev) = reader.poll_button()? {
            if let ButtonEvent::Pressed(btn) = ev {
                band_color = button_color(btn);
                if let Some(name) = &led {
                    led_on = !led_on;
                    let _ = set_led(name, if led_on { 255 } else { 0 });
                }
                if let Some(bus) = &can {
                    let _ = bus.send_std(0x100, &[btn as u8]);
                }
                println!("{btn:?} -> color 0x{band_color:06X}");
            }
        }

        // --- render the whole frame ---
        let (w, h) = (fb.width, fb.height);
        let band = h / 3;
        let bar_x = (frame * 4) % w; // sweep speed
        {
            let mut s = fb.surface();
            for y in 0..h {
                let base = if y < band { band_color } else { BG };
                for x in 0..w {
                    // white sweeping bar (6 px wide) over the lower region
                    let color = if y >= band && (x.wrapping_sub(bar_x)) < 6 {
                        0x00_FF_FF_FF
                    } else {
                        base
                    };
                    s.put_pixel(x, y, color);
                }
            }
        }

        frame = frame.wrapping_add(1);
        sleep(frame_period);
    }

    fn button_color(b: Button) -> u32 {
        match b {
            Button::F1 => 0x00_FF_00_00,
            Button::F2 => 0x00_00_FF_00,
            Button::F3 => 0x00_00_00_FF,
            Button::F4 => 0x00_FF_FF_00,
            Button::F5 => 0x00_FF_00_FF,
            Button::F6 => 0x00_00_FF_FF,
            Button::Up => 0x00_FF_FF_FF,
            Button::Down => 0x00_80_80_80,
            Button::Left => 0x00_FF_80_00,
            Button::Right => 0x00_00_80_FF,
            Button::Enter => 0x00_FF_FF_FF,
        }
    }
}

#[cfg(not(target_os = "linux"))]
fn main() {
    eprintln!("demo is Linux-only (uses SocketCAN)");
}
