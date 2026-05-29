#[cfg(target_os = "linux")]
fn main() -> std::io::Result<()> {
    use cr1140_hal::can::CanBus;
    let iface = std::env::args().nth(1).unwrap_or_else(|| "can0".into());
    let bus = CanBus::open(&iface)?;
    println!("listening on {iface}");
    loop {
        let (id, data) = bus.recv()?;
        println!("RX id=0x{id:X} data={data:02X?}");
        // echo back on id+1 as a liveness check
        let _ = bus.send_std((id as u16).wrapping_add(1), &data);
    }
}

#[cfg(not(target_os = "linux"))]
fn main() {
    eprintln!("can-echo is Linux-only (SocketCAN)");
}
