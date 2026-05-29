// SPDX-License-Identifier: GPL-3.0-only
//! Device & OS identity and network state: things that describe *this* unit
//! (model/firmware, hostname, onboard board sensor) and its links (eth0/can0).

use std::fs;

/// Look up a `KEY=value` entry in `/etc/os-release`, stripping optional quotes.
pub fn os_release_value(content: &str, key: &str) -> Option<String> {
    for line in content.lines() {
        if let Some((k, v)) = line.split_once('=') {
            if k == key {
                return Some(v.trim().trim_matches('"').to_string());
            }
        }
    }
    None
}

/// Convenience: read `/etc/os-release` and return the value for `key`.
pub fn os_release(key: &str) -> Option<String> {
    os_release_value(&fs::read_to_string("/etc/os-release").ok()?, key)
}

pub fn hostname() -> String {
    fs::read_to_string("/proc/sys/kernel/hostname")
        .map(|s| s.trim().to_string())
        .unwrap_or_else(|_| "?".into())
}

/// Onboard lm75 board-temperature sensor (hwmon), distinct from the SoC zone.
pub fn read_board_temp_c() -> Option<f32> {
    let s = fs::read_to_string("/sys/class/hwmon/hwmon0/temp1_input").ok()?;
    cr1140_hal::sys::parse::parse_millidegrees(&s)
}

/// `operstate` of a network interface, e.g. "up" / "down" for `eth0` / `can0`.
pub fn read_operstate(iface: &str) -> String {
    fs::read_to_string(format!("/sys/class/net/{iface}/operstate"))
        .map(|s| s.trim().to_string())
        .unwrap_or_else(|_| "?".into())
}

/// The IPv4 address bound to an interface, read straight from `getifaddrs`.
/// Unlike a route-based lookup this works on an isolated LAN with no default
/// gateway (the CR1140's typical deployment).
pub fn iface_ipv4(iface: &str) -> Option<String> {
    use nix::ifaddrs::getifaddrs;
    for ifa in getifaddrs().ok()? {
        if ifa.interface_name == iface {
            if let Some(sin) = ifa.address.as_ref().and_then(|a| a.as_sockaddr_in()) {
                // SockaddrIn renders as "a.b.c.d:port" — drop the port.
                return Some(sin.to_string().split(':').next()?.to_string());
            }
        }
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn os_release_value_strips_quotes_and_matches_exactly() {
        let s = "ID=edb2-ecomat-display\nPRETTY_NAME=\"eDB2 ecomatDisplay 2.0.0.11\"\nVERSION_ID=2.0.0.11\n";
        assert_eq!(
            os_release_value(s, "PRETTY_NAME").as_deref(),
            Some("eDB2 ecomatDisplay 2.0.0.11")
        );
        assert_eq!(os_release_value(s, "VERSION_ID").as_deref(), Some("2.0.0.11"));
        // exact key match: "ID" must not match "VERSION_ID"
        assert_eq!(os_release_value(s, "ID").as_deref(), Some("edb2-ecomat-display"));
        assert_eq!(os_release_value(s, "MISSING"), None);
    }
}
