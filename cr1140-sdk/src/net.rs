// SPDX-License-Identifier: GPL-3.0-only
//! Host network-config apply via `nmcli` (feature `net`, off by default).
//!
//! Live network config lives under `/etc` on the p2 overlay, which a firmware
//! update wipes (ADR-0002). So the source of truth is whatever the app persists
//! in [`crate::retain`]; this module re-applies it to NetworkManager. The app
//! owns the *timing* — call [`apply`] during boot init and/or from a UI handler
//! when the user changes settings.
//!
//! This is deliberately **off by default**: shelling out to `nmcli` bakes in a
//! NetworkManager host assumption that has no place in the guest-minimal build
//! (see the "SDK is a guest" principle). D-Bus (`zbus`) is the recorded future
//! upgrade path if the subprocess dependency becomes a problem.
//!
//! [`apply`] is **idempotent**: it modifies the named connection if it already
//! exists, otherwise adds it, then brings it up — so it is safe to call on every
//! boot *and* from a UI handler with the same config.

use serde::{Deserialize, Serialize};

use crate::error::{SdkError, SdkResult};

/// IPv4 addressing method for a [`NetworkConfig`].
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Method {
    /// Obtain an address via DHCP (`ipv4.method auto`).
    Dhcp,
    /// Use a fixed address/prefix (`ipv4.method manual`).
    Static,
}

/// A minimal network connection profile an app can embed in its retain struct
/// and re-apply via NetworkManager. Keep it small; extend as needs grow.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct NetworkConfig {
    /// NetworkManager connection name (the stable handle for modify/up).
    pub connection: String,
    /// Interface to bind, e.g. `eth0`.
    pub interface: String,
    /// DHCP or static addressing.
    pub method: Method,
    /// Static address (e.g. `192.168.1.50`); required when `method` is static.
    #[serde(default)]
    pub address: Option<String>,
    /// Static prefix length (e.g. `24`); required when `method` is static.
    #[serde(default)]
    pub prefix: Option<u8>,
    /// Optional default gateway for a static connection.
    #[serde(default)]
    pub gateway: Option<String>,
    /// DNS servers; applied for either method when non-empty.
    #[serde(default)]
    pub dns: Vec<String>,
}

/// Captured outcome of one `nmcli` invocation.
struct CmdOutput {
    success: bool,
    stderr: String,
}

/// Abstraction over running `nmcli`, so unit tests can assert the argument
/// vectors without shelling out.
trait CommandRunner {
    fn run(&self, args: &[&str]) -> SdkResult<CmdOutput>;
}

/// The real runner: spawns `nmcli` and captures its exit status + stderr.
struct SystemRunner;

impl CommandRunner for SystemRunner {
    fn run(&self, args: &[&str]) -> SdkResult<CmdOutput> {
        let out = std::process::Command::new("nmcli")
            .args(args)
            .output()
            .map_err(|e| SdkError::Net(format!("could not run nmcli (is it installed?): {e}")))?;
        Ok(CmdOutput {
            success: out.status.success(),
            stderr: String::from_utf8_lossy(&out.stderr).into_owned(),
        })
    }
}

/// The `ipv4.*` settings appended to an `nmcli connection add|modify`.
///
/// For DHCP, static fields are cleared so re-applying over a previously-static
/// connection is correct (idempotent). For static, address+prefix are required.
fn ip_args(cfg: &NetworkConfig) -> SdkResult<Vec<String>> {
    let mut v: Vec<String> = Vec::new();
    match cfg.method {
        Method::Dhcp => {
            v.extend(["ipv4.method".into(), "auto".into()]);
            // Clear any leftover static config so DHCP truly takes over.
            v.extend(["ipv4.addresses".into(), String::new()]);
            v.extend(["ipv4.gateway".into(), String::new()]);
        }
        Method::Static => {
            let address = cfg
                .address
                .as_deref()
                .ok_or_else(|| SdkError::Net("static method requires an address".into()))?;
            let prefix = cfg
                .prefix
                .ok_or_else(|| SdkError::Net("static method requires a prefix".into()))?;
            v.extend(["ipv4.method".into(), "manual".into()]);
            v.extend(["ipv4.addresses".into(), format!("{address}/{prefix}")]);
            if let Some(gw) = &cfg.gateway {
                v.extend(["ipv4.gateway".into(), gw.clone()]);
            }
        }
    }
    if !cfg.dns.is_empty() {
        v.extend(["ipv4.dns".into(), cfg.dns.join(" ")]);
    }
    Ok(v)
}

/// Apply `cfg` to NetworkManager via `nmcli`: modify-or-add the named connection,
/// then bring it up. Idempotent — safe to call at boot and from a UI handler.
///
/// Errors (nmcli missing, a non-zero exit, or an invalid config) surface as
/// [`SdkError::Net`], carrying the captured stderr where available.
pub fn apply(cfg: &NetworkConfig) -> SdkResult<()> {
    apply_with(cfg, &SystemRunner)
}

fn apply_with(cfg: &NetworkConfig, runner: &dyn CommandRunner) -> SdkResult<()> {
    let ip = ip_args(cfg)?;

    // Does a connection with this name already exist?
    let exists = runner
        .run(&["connection", "show", &cfg.connection])?
        .success;

    let mut args: Vec<String> = if exists {
        vec!["connection".into(), "modify".into(), cfg.connection.clone()]
    } else {
        vec![
            "connection".into(),
            "add".into(),
            "type".into(),
            "ethernet".into(),
            "con-name".into(),
            cfg.connection.clone(),
            "ifname".into(),
            cfg.interface.clone(),
        ]
    };
    args.extend(ip);

    let argv: Vec<&str> = args.iter().map(String::as_str).collect();
    let out = runner.run(&argv)?;
    if !out.success {
        let verb = if exists { "modify" } else { "add" };
        return Err(SdkError::Net(format!(
            "nmcli connection {verb} failed: {}",
            out.stderr.trim()
        )));
    }

    let up = runner.run(&["connection", "up", &cfg.connection])?;
    if !up.success {
        return Err(SdkError::Net(format!(
            "nmcli connection up failed: {}",
            up.stderr.trim()
        )));
    }

    tracing::info!(connection = %cfg.connection, "applied retained network config");
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::RefCell;

    /// A runner that records every argv and answers existence/failure as told.
    struct RecordingRunner {
        calls: RefCell<Vec<Vec<String>>>,
        exists: bool,
        fail_apply: bool,
    }

    impl RecordingRunner {
        fn new(exists: bool, fail_apply: bool) -> Self {
            Self {
                calls: RefCell::new(Vec::new()),
                exists,
                fail_apply,
            }
        }
        fn calls(&self) -> Vec<Vec<String>> {
            self.calls.borrow().clone()
        }
    }

    impl CommandRunner for RecordingRunner {
        fn run(&self, args: &[&str]) -> SdkResult<CmdOutput> {
            let argv: Vec<String> = args.iter().map(|s| s.to_string()).collect();
            self.calls.borrow_mut().push(argv.clone());

            // Existence probe.
            if argv.first().map(String::as_str) == Some("connection")
                && argv.get(1).map(String::as_str) == Some("show")
            {
                return Ok(CmdOutput {
                    success: self.exists,
                    stderr: String::new(),
                });
            }
            // add/modify failure injection.
            let verb = argv.get(1).map(String::as_str);
            if self.fail_apply && (verb == Some("add") || verb == Some("modify")) {
                return Ok(CmdOutput {
                    success: false,
                    stderr: "nmcli: boom".into(),
                });
            }
            Ok(CmdOutput {
                success: true,
                stderr: String::new(),
            })
        }
    }

    fn dhcp_cfg() -> NetworkConfig {
        NetworkConfig {
            connection: "lan".into(),
            interface: "eth0".into(),
            method: Method::Dhcp,
            address: None,
            prefix: None,
            gateway: None,
            dns: vec![],
        }
    }

    fn static_cfg() -> NetworkConfig {
        NetworkConfig {
            connection: "lan".into(),
            interface: "eth0".into(),
            method: Method::Static,
            address: Some("192.168.1.50".into()),
            prefix: Some(24),
            gateway: Some("192.168.1.1".into()),
            dns: vec!["1.1.1.1".into(), "8.8.8.8".into()],
        }
    }

    #[test]
    fn dhcp_ip_args_use_auto() {
        let args = ip_args(&dhcp_cfg()).unwrap();
        let joined = args.join(" ");
        assert!(joined.contains("ipv4.method auto"), "got {joined:?}");
    }

    #[test]
    fn static_ip_args_carry_address_gateway_dns() {
        let args = ip_args(&static_cfg()).unwrap();
        let joined = args.join(" ");
        assert!(joined.contains("ipv4.method manual"), "got {joined:?}");
        assert!(
            joined.contains("ipv4.addresses 192.168.1.50/24"),
            "got {joined:?}"
        );
        assert!(
            joined.contains("ipv4.gateway 192.168.1.1"),
            "got {joined:?}"
        );
        assert!(
            joined.contains("ipv4.dns 1.1.1.1 8.8.8.8"),
            "got {joined:?}"
        );
    }

    #[test]
    fn static_without_address_errors() {
        let mut cfg = static_cfg();
        cfg.address = None;
        assert!(matches!(ip_args(&cfg), Err(SdkError::Net(_))));
    }

    #[test]
    fn apply_adds_when_connection_absent() {
        let runner = RecordingRunner::new(false, false);
        apply_with(&static_cfg(), &runner).unwrap();
        let calls = runner.calls();
        // show probe, then add, then up.
        assert_eq!(calls[0][0..2], ["connection", "show"]);
        assert_eq!(calls[1][0..2], ["connection", "add"]);
        assert!(calls[1].contains(&"ifname".to_string()));
        assert!(calls[1].contains(&"eth0".to_string()));
        assert_eq!(calls[2][0..3], ["connection", "up", "lan"]);
    }

    #[test]
    fn apply_modifies_when_connection_exists() {
        let runner = RecordingRunner::new(true, false);
        apply_with(&dhcp_cfg(), &runner).unwrap();
        let calls = runner.calls();
        assert_eq!(calls[1][0..3], ["connection", "modify", "lan"]);
        // modify must not re-declare type/ifname.
        assert!(!calls[1].contains(&"add".to_string()));
    }

    #[test]
    fn apply_surfaces_nmcli_failure_with_stderr() {
        let runner = RecordingRunner::new(false, true);
        let err = apply_with(&dhcp_cfg(), &runner).unwrap_err();
        match err {
            SdkError::Net(msg) => assert!(msg.contains("boom"), "got {msg}"),
            other => panic!("expected Net error, got {other:?}"),
        }
    }

    #[cfg(feature = "retain")]
    #[test]
    fn network_config_serde_round_trips() {
        // Exercises the Serialize/Deserialize derives via the retain encoder.
        let cfg = static_cfg();
        let bytes = postcard::to_stdvec(&cfg).unwrap();
        let back: NetworkConfig = postcard::from_bytes(&bytes).unwrap();
        assert_eq!(cfg, back);
    }
}
