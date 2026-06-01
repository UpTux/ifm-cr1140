# cr1140-sdk

App-building conveniences layered on top of `cr1140-hal`.

`cr1140-sdk` provides the "batteries" a native CR1140 application needs,
deliberately UI-framework agnostic (no Slint, no rendering) regardless of how it
draws: keypad LED animation modes (`led`), generic Linux telemetry — CPU,
memory, load, temperature, uptime — via a `Telemetry` collector returning an
aggregated `Snapshot` (`metrics`), device/OS identity and network state
(`device`), an RAII `ShutdownGuard` that restores backlight/LED on exit
(`guard`), and persistence stores (`config`, `retain`). Errors surface as
`SdkError`.

## Target

Mostly device-agnostic logic layered on `cr1140-hal`, built for the ifm
**CR1140 / CR1141** ecomatDisplay (NXP i.MX 8M Nano, aarch64, Yocto Linux).
Linux-only at runtime (sysfs, SPI-EEPROM, `nmcli`); the pure-logic parts build
and test on a macOS/Linux host. Typical target triple:
`aarch64-unknown-linux-musl` (static) or `aarch64-unknown-linux-gnu`.

## Install

```toml
[dependencies]
cr1140-sdk = "0.1"
```

## Features

| Feature   | Default | What it adds |
|-----------|:-------:|--------------|
| `config`  | yes | Atomic TOML `Store` persistence on the p2 overlay (`/home/cds-apps`). |
| `signals` | yes | Opt-in SIGINT/SIGTERM handling for `ShutdownGuard` — standalone binaries only. |
| `retain`  | yes | Reflash-surviving A/B `RetainStore` on the SPI EEPROM (A/B + CRC32, `postcard`). |
| `net`     | no  | Host network-config apply via `nmcli` (NetworkManager) — `net::apply`. |

**SDK is a guest** — it emits via the `tracing` facade only, never installs a
subscriber; signals are opt-in; lean builds via `default-features = false`.

## Example

Sample system telemetry:

```rust
use cr1140_sdk::Telemetry;

let mut telemetry = Telemetry::new();
let snapshot = telemetry.sample();
println!("CPU {:?}%  SoC {:?}°C", snapshot.cpu_percent, snapshot.soc_temp_c);
```

Persist app state across reflashes on the SPI EEPROM (illustrative):

```rust
use cr1140_sdk::RetainStore;
use cr1140_hal::sys::Nvmem;

let store: RetainStore<MyState> = RetainStore::open(Nvmem::open_retain()?)?;
let state = store.load_or_default()?;
store.save(&state)?;
```

## License

Licensed under **GPL-3.0-only**, or a **commercial license** from UpTux UG for
closed-source use — see
[`LICENSING.md`](https://github.com/UpTux/ifm-cr1140/blob/main/LICENSING.md).

## Repository

<https://github.com/UpTux/ifm-cr1140>
