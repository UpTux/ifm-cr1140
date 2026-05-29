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
- Backlight: `/sys/class/backlight/backlight/` (`max_brightness=400`).
- Display fd currently held by `ifm-local-setup` (pid 264); no active CODESYS /
  weston / ecopanel visu.

## Input  [live ✓]

- Keypad: **`ifm-keypad` → `/dev/input/event1`** (gpio-keys). (event0 =
  snvs-powerkey, event2 = bd718xx-pwrkey — both ignored.)
- Physical → keycode map (F1–F6, arrows, Enter): **[live, Task 3.3]** — needs
  physical button presses to capture.

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

## Recovery & restore  [live ✓ — Task 0.3]

- **Stock state of `codesys.service`: `disabled` + `inactive`** (fresh delivery,
  no CODESYS project loaded). `ifm-retain-srv.service` is `enabled`.
- ⚠️ Therefore "restore to stock" = `systemctl unmask codesys` and leave it
  **disabled** (NOT `enable --now`). `restore-codesys.sh` updated accordingly.
- Ultimate fallback: re-flash stock firmware via the delivery `.swu`
  (USB-stick update path per `sw-description`).
