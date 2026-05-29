use cr1140_hal::display::FbDisplay;

fn main() -> std::io::Result<()> {
    let mut fb = FbDisplay::open("/dev/fb0")?;
    println!(
        "fb {}x{} stride {} bpp {}",
        fb.width, fb.height, fb.stride, fb.bits_per_pixel
    );
    let mut s = fb.surface();
    s.fill(0x00_00_80_FF); // xRGB: blue-ish
    println!("filled");
    Ok(())
}
