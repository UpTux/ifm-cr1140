//! All-module demo: fills the screen, reads buttons, drives an LED, reads
//! temperature, and best-effort opens CAN. Replaces the CODESYS visu.
//!
//! Usage: demo [event-node] [led-name] [can-iface]
//!   defaults: /dev/input/event0  (no led)  can0

#[cfg(target_os = "linux")]
fn main() -> std::io::Result<()> {
    use cr1140_hal::can::CanBus;
    use cr1140_hal::display::FbDisplay;
    use cr1140_hal::input::{Button, ButtonEvent, ButtonReader};
    use cr1140_hal::sys::{read_temp_c, set_led};

    let mut args = std::env::args().skip(1);
    let event_node = args.next().unwrap_or_else(|| "/dev/input/event0".into());
    let led = args.next();
    let can_iface = args.next().unwrap_or_else(|| "can0".into());

    let mut fb = FbDisplay::open("/dev/fb0")?;
    println!("display {}x{} bpp {}", fb.width, fb.height, fb.bits_per_pixel);
    fb.surface().fill(0x00_10_10_10); // near-black background

    match read_temp_c(0) {
        Ok(t) => println!("temp = {t:.1} C"),
        Err(e) => println!("temp unavailable: {e}"),
    }

    let can = CanBus::open(&can_iface)
        .map_err(|e| println!("CAN {can_iface} unavailable: {e}"))
        .ok();

    let mut reader = ButtonReader::open(&event_node)?;
    println!("ready; press buttons (Ctrl-C to exit)");

    let mut led_on = false;
    loop {
        if let ButtonEvent::Pressed(btn) = reader.next_button()? {
            let color = button_color(btn);
            // Draw a band across the top third in the button's color.
            let band = fb.height / 3;
            {
                let mut s = fb.surface();
                for y in 0..band {
                    for x in 0..s.width {
                        s.put_pixel(x, y, color);
                    }
                }
            }
            if let Some(name) = &led {
                led_on = !led_on;
                let _ = set_led(name, if led_on { 255 } else { 0 });
            }
            if let Some(bus) = &can {
                let _ = bus.send_std(0x100, &[btn as u8]);
            }
            println!("{btn:?} -> color 0x{color:06X}");
        }
    }

    // helper kept inside cfg(linux) main scope
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
