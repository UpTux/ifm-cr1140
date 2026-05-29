use cr1140_hal::input::event::EV_KEY;
use cr1140_hal::input::InputEvent;
use std::fs::File;
use std::io::Read;

fn main() -> std::io::Result<()> {
    let path = std::env::args()
        .nth(1)
        .unwrap_or_else(|| "/dev/input/event0".into());
    let mut f = File::open(&path)?;
    println!("reading {path}; press each button (F1-F6, arrows, Enter), Ctrl-C to stop");
    let mut buf = [0u8; InputEvent::SIZE];
    loop {
        f.read_exact(&mut buf)?;
        if let Some(ev) = InputEvent::decode(&buf) {
            if ev.type_ == EV_KEY && ev.value == 1 {
                println!("KEY code={}", ev.code);
            }
        }
    }
}
