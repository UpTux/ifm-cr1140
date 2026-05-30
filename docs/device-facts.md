# CR1140 / CR1141 — Device Facts

Ground truth for the HAL. Sources tagged `[offline]` (from the `.swu` rootfs,
extracted via `debugfs`) or `[live]` (from on-device recon). Fill `[live]`
sections by running `cr1140-recon.sh` on the device (Task 0.2).

> Do NOT commit the SSH password. Record it in a local note outside git.

## Arch & libc  [offline ✓]

- SoC: NXP **i.MX 8M Nano** → **aarch64** (Cortex-A53).
- Kernel: **5.19.16-stable-standard**.
- libc: **glibc 2.35** (`ld-linux-aarch64.so.1`, `libc.so.6`).
- OS: `eDB2 ecomatDisplay 2.0.0.11`, Yocto (`DISTRO_CODENAME=embedded-linux`), **systemd** PID 1.
- **Rust targets:**
  - default static: `aarch64-unknown-linux-musl`
  - glibc escape hatch (cargo-zigbuild): `aarch64-unknown-linux-gnu.2.35`

## Init / overlay  [offline ✓]

- Boot: `init=/sbin/ifm-overlay.sh`.
- `ifm-overlay.sh`: mounts **`/dev/mmcblk0p2`** at `/overlay`, builds an
  overlayfs with `lowerdir=/` (read-only rootfs from p1) +
  `upperdir=/overlay/root/upper`, `pivot_root`s into it, then `exec`s
  `/bin/ifm-splash` → `/lib/systemd/systemd`.
- **Consequence:** `/` is a writable overlay. Files written under `/` (e.g.
  `/home/cds-apps`, `/usr/local/bin`) **persist** via the p2 upper layer.
- eMMC partition roles (revised from earlier guess):
  - `p1` = read-only rootfs base (lowerdir)
  - `p2` = **writable overlay** upper/work (this is where `/` writes land)
  - `p3` = role unconfirmed; not in `/etc/fstab`. **[live]** — check `mount`/`df`.
- U-Boot env lives in `mmcblk0boot0/1` (`/etc/fw_env.config`); `fw_setenv` can
  set `ifm_boot_backlight`, `ifm_orientation`, `ifm_boot_status_led`.

## CODESYS launch (service to disable)  [offline ✓]

- Unit: **`codesys.service`** → `/opt/CODESYSControl/codesyscontrol /opt/CODESYSControl/CODESYSControl.cfg`.
- ⚠️ **`WatchdogSec=2s`, `StartLimitBurst=1`/`StartLimitIntervalSec=60`,
  `StartLimitAction=reboot-force`.** Killing/crashing the runtime can
  **force-reboot the device**. Always `systemctl disable --now` **and**
  `systemctl mask` — never just `kill`.
- `[Install] WantedBy=multi-user.target`, but the enable symlink is NOT in the
  base rootfs — it lives in the p2 overlay. Confirm with `systemctl is-enabled
  codesys` **[live]**.
- Related ifm services (enabled in base rootfs): `ifm-retain-srv.service`
  (codesys `Wants=`/`After=` this), `ifm-dev-healthd.service`,
  `ifm-touch-autoconf.service`, `ifmnetworkmanager.service`,
  `ifm-ecopanel@.service` (the splash/launcher; `/usr/bin/ifm-ecopanel`).
  Decide per-service whether to keep (e.g. networkmanager) when replacing CODESYS.

## App / persistence locations  [offline ✓ / live ✓]

- CODESYS apps: `/home/cds-apps` (root-owned, currently empty).
- Mounts (live): `/` is the rw **overlay** (upper on `/dev/mmcblk0p2`, 1.1 GB
  free); read-only base is `/dev/root` at `/rom`. Dedicated **data partition
  `/dev/mmcblk0p3` mounted at `/mnt/updata`** (ext4 rw, ~796 MB free).
- **Deploy dir chosen: `/home/cds-apps`** (persists via the p2 overlay; verified
  writable, our `hello` binary ran from there). Alternative: `/mnt/updata` (p3).
- SSH login confirmed: user **`root`** (uid 0), password kept out of git.
  Hostname `ecomat-display`.

## Display  [offline ✓ / live ✓]

- **fbdev**, **800×480, 32 bpp, stride 3200** (`/dev/fb0`, name `mxsfb-drmdrmfb`
  — mxsfb DRM driver with fbdev emulation; `/dev/dri/card0` also present, DPI-1
  connector). Matches HAL xRGB8888 `Surface` exactly.
- HAL uses the **fbdev backend**. **DRM backend (plan Task 2.3) SKIPPED.**
- **No fbdev double-buffering.** `/sys/class/graphics/fb0/virtual_size` =
  `800,480` (virtual == physical), and on `drmfb` (DRM fbdev emulation)
  `FBIOPUT_VSCREENINFO` cannot grow `yres_virtual` past 480 — so panning-based
  double buffering is unavailable. `FbDisplay::open_double_buffered` correctly
  detects this and falls back to single buffer (`(1 buffer(s))`), confirmed live
  2026-05-29. Tear-free flipping would need the DRM/KMS path (DUMB buffer +
  atomic page-flip on `/dev/dri/card0`). The `ifm-local-setup` race is mitigated
  by continuous full-buffer redraw, not flipping.
- Backlight: `/sys/class/backlight/backlight/` (`max_brightness=400`).
- Display fd held by `ifm-local-setup` (a respawning helper, not a systemd
  unit; empty `/proc/PID/cmdline`). It also writes `/dev/fb0`, so it **races**
  with a native app that only redraws on demand — it can paint over our output
  between redraws. For exclusive display ownership: have the app redraw
  continuously (double-buffer/refresh loop) and/or neutralize `ifm-local-setup`.
  Verified our writes land and persist (fb read-back matched HAL output);
  no active CODESYS / weston / ecopanel visu.

## Input  [live ✓]

- Keypad: **`ifm-keypad` → `/dev/input/event1`** (gpio-keys). (event0 =
  snvs-powerkey, event2 = bd718xx-pwrkey — both ignored.)
- Physical → keycode map **[live ✓, Task 3.3 — confirmed key-by-key]**, all
  standard Linux KEY_* codes:
  F1=59, F2=60, F3=61, F4=62, F5=63, F6=64,
  Up=103, Down=108, Left=105, Right=106, Enter=28.
  Physical labels match the standard codes 1:1 (`Button::F1` = physical F1, etc.).

## CAN  [live ✓]

- **`can0` only**, driver **mcp251xfd** (SPI CAN-FD controller, 40 MHz clock,
  on `spi2.0`). Currently DOWN/STOPPED. Bring up: `ip link set can0 up type can
  bitrate 250000`.
- Ethernet: `eth0` UP (fec, `30be0000.ethernet`, MAC 00:02:01:ab:bd:49).

## LEDs  [live ✓]

- `/sys/class/leds/`:
  - **status LED** (binary, `max_brightness=1`): `red:status`, `green:status`,
    `blue:status`.
  - **keypad backlight** (`max_brightness=255`): `red:kbd_backlight`,
    `green:kbd_backlight`, `blue:kbd_backlight`.
  - `mmc0::` (activity).
- For a visible brightness ramp use a `*:kbd_backlight`; status LEDs are on/off.

## Capability recon (CODESYS FB checklist)  [live ✓ — 2026-05-30]

Settles the conditional capabilities in
[`../cr1140-hal/CONTEXT.md`](../cr1140-hal/CONTEXT.md) ("Capability scope") and
[`adr/0001-codesys-fb-capability-scope.md`](adr/0001-codesys-fb-capability-scope.md).
Recon via `cr1140-cap-recon.sh` (read-only). Re-run command set:

```sh
# buzzer / beeper — EV_SND capability bit, pwm-beeper/gpio-beeper
cat /proc/bus/input/devices
ls /sys/class/ | grep -i beep; dmesg | grep -i 'beep\|buzzer'
# hardware watchdog — which owner is free (imx2-wdt expected)
ls -l /dev/watchdog*; grep -i watchdog /etc/systemd/system.conf; dmesg | grep -i wdt
# ambient-light sensor — IIO illuminance channel (prior: absent)
ls /sys/bus/iio/devices/ 2>/dev/null; dmesg | grep -iE 'illuminance|als|light-sensor'
# retain — nvmem inventory + what ifm-retain-srv persists and where
systemctl cat ifm-retain-srv.service; ls /sys/bus/nvmem/devices/ /dev/fram* 2>/dev/null
```

- **Buzzer: ABSENT → dropped.** `ifm-keypad` reports `EV=0x100003` (SYN + KEY + REP
  only); the `EV_SND` bit (`0x12`) is not set. No `pwm-beeper`/`gpio-beeper` in sysfs
  or dmesg, no separate beeper input node. No buzzer exposed via any standard kernel
  interface on this SKU.
- **Watchdog: PRESENT + unclaimed → in scope.** `/dev/watchdog` (10,130) and
  `/dev/watchdog0` (248,0), `imx2-wdt`. `system.conf` has `#RuntimeWatchdogSec=off`
  (commented → systemd is **not** petting it), so the HW watchdog is free for either
  owner. Plan stands: systemd-owned by default (`RuntimeWatchdogSec` + a
  `Type=notify`/`WatchdogSec=` unit), opt-in `/dev/watchdog` HAL primitive for
  app-owned liveness.
- **Ambient light: ABSENT → dropped.** `/sys/bus/iio/devices/` empty; no illuminance
  channel; nothing in dmesg.
- **Retain: PRESENT as real NV hardware → reopened (see below).** Not a file daemon:
  `ifm-retain-srv` persists three segments to an **SPI EEPROM** —
  `ExecStart=/usr/bin/ifm-retain-srv -s 1 0 8104 -s 2 16384 232 -s 3 16880 7936
  /sys/bus/spi/devices/spi1.0/eeprom` (exposed as nvmem `spi1.00`). Additional
  battery/EEPROM-backed NV present: **RV-3028 RTC** with NVRAM + EEPROM
  (`rv3028_nvram0`, `rv3028_eeprom0`), i.MX **SNVS low-power GP registers**
  (`snvs-lpgpr0`, coin-cell-backed scratch), two I²C EEPROMs (`0-0050*`), and
  `imx-ocotp0` (read-only fuses). The RV-3028 is also the system RTC (OS owns
  wall-clock via `/dev/rtc` — RTC stays out of scope; only its NVRAM is retain-relevant).

### nvmem / EEPROM map  [live ✓ — 2026-05-30]

Verified read-only (`cr1140-eeprom-verify.sh` / `-dump.sh`). **Factory data lives on
the two I²C EEPROMs; the SPI EEPROM is free CODESYS-retain scratch.**

| nvmem device | Bus / size | Contents | Role |
|--------------|-----------|----------|------|
| `spi1.00`       | SPI `spi1.0`, **32 KB** | CODESYS retain blobs (`92 19 a8 1f…` then zeros), **no factory data, no MAC** | **free — our retain target** |
| `0-00513`       | I²C 0x51, 16 KB | `vhip` magic, article `100008599862`, `CR1140`, `ecomatDisplay/4.3"/STD./E`, asset `pdm3_4_001`, serial `7998407`, date `28.03.2025`, **MAC `00:02:01:ab:bd:49`** | factory device identity — **read-only** |
| `0-00502`       | I²C 0x50, 16 KB | `adm-icn6211-9904454_Modul1-notouch-v1.0` (ICN6211 DSI bridge; `notouch` ⇒ keypad-only SKU) | factory panel/board info — **read-only** |
| `rv3028_eeprom0`| RTC, 43 B | RV-3028 config EEPROM | RTC-owned |
| `rv3028_nvram0` | RTC, 2 B  | RV-3028 user NVRAM | tiny scratch |
| `snvs-lpgpr0`   | SoC, 16 B | i.MX SNVS low-power GP regs (coin-cell-backed SRAM) | tiny fast retain |
| `imx-ocotp0`    | SoC, 1 KB | OCOTP fuses (MAC not shadowed here) | read-only |

**Retain decision (settled by recon):** own the **SPI EEPROM (`spi1.00`, 32 KB)** for a
reflash-surviving retain store (factory calibration / network config). `mask
ifm-retain-srv` (currently `active`) alongside `codesys.service`; restore-to-stock
unmasks it. Factory identity (serial/MAC/article) is **read-only** on the I²C EEPROMs and
needs no write path. Remaining design (on-EEPROM layout/integrity, HAL-vs-SDK API,
`.swu` reflash-survival verification) tracked in the HAL CONTEXT scope table.

## Firmware update (`.swu`) — what survives a reflash  [offline ✓ — 2026-05-30]

From the delivery `sw-description` (`ifm_ecomatDisplay43inch_cds_2.0.0.11.swu`,
extracted via `cpio`). The package carries **one image** (the p1 rootfs,
`core-image-…ext4.gz` → `/dev/mmcblk0p1`, raw) plus an `emmc_part` Lua embedded
script. Reflash behavior:

- **p1** (rootfs): overwritten with the new image.
- **p2** (overlay — `/home/cds-apps`, `/etc`, our writes): **`mkfs.ext4 -F`'d on every
  update**, *including* the "partition table already correct" fast path. **Always wiped.**
- **p3** (`/mnt/updata`): survives the fast path, but is **`mkfs.ext4 -F`'d when the
  partition table changes** (the script's whole purpose). **Not a guaranteed survivor.**
- **EEPROMs** (SPI `spi1.00`, I²C `0-0050*`) and U-Boot env: **not touched** by the update.
- U-Boot env vars are *set* by `bootenv` (`bootdelay`, `bootargs=init=/sbin/ifm-overlay.sh`,
  `ifm_boot_status_led`, `fw_version`, …) but the EEPROMs are untouched.

**Consequence for persistence:** the **SPI EEPROM is the only writable storage that
survives every update.** `config::Store` on p2 does **not** survive a reflash; data that
must (factory calibration, network/IP config) belongs in the EEPROM retain store. Live
network config under `/etc` (p2) is wiped on update → must be re-applied on boot from
retain.

## Recovery & restore  [live ✓ — Task 0.3]

- **Stock state of `codesys.service`: `disabled` + `inactive`** (fresh delivery,
  no CODESYS project loaded). `ifm-retain-srv.service` is `enabled`.
- ⚠️ Therefore "restore to stock" = `systemctl unmask codesys` and leave it
  **disabled** (NOT `enable --now`). `restore-codesys.sh` updated accordingly.
- Ultimate fallback: re-flash stock firmware via the delivery `.swu`
  (USB-stick update path per `sw-description`).
