use cr1140_hal::sys::{read_temp_c, set_led};
use std::thread::sleep;
use std::time::Duration;

fn main() -> std::io::Result<()> {
    let led = std::env::args()
        .nth(1)
        .expect("usage: blink-led <led-name>");
    println!("temp = {:.1} C", read_temp_c(0)?);
    for _ in 0..5 {
        set_led(&led, 255)?;
        sleep(Duration::from_millis(300));
        set_led(&led, 0)?;
        sleep(Duration::from_millis(300));
    }
    Ok(())
}
