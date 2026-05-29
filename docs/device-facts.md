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

## App / persistence locations  [offline ✓ / live TBD]

- CODESYS apps: `/home/cds-apps` (root-owned).
- Proposed deploy dir for our binary: **`/home/cds-apps`** or `/usr/local/bin`
  (both persist via overlay). Plan/justfile placeholder `/run-app` → replace
  with the chosen dir. **[live]** confirm it is writable and survives reboot.

## Display  [offline ✓]

- **fbdev (linuxfb)**, **800×480**, panel 142×82 mm. CODESYS runs Qt with
  `QT_QPA_PLATFORM=linuxfb:size=800x480:mmsize=142x82`.
- → HAL uses the **fbdev backend**; `/dev/fb0` expected. **DRM backend (plan
  Task 2.3) is SKIPPED.**
- Backlight sysfs path + bits-per-pixel/stride: **[live]** (confirm `/dev/fb0`,
  `/sys/class/graphics/fb0/*`, `/sys/class/backlight/*`).

## Input  [live TBD]

- Keypad event node `/dev/input/eventN`: **[live]** from `/proc/bus/input/devices`.
- Physical → keycode map (F1–F6, arrows, Enter): **[live]** captured in Task 3.3.

## CAN  [live TBD]

- Interfaces `can0`/`can1` and default bitrate: **[live]** (`ls /sys/class/net/`,
  recon §3). ifm factory default expected 250 kbit/s.

## LEDs  [live TBD]

- Entries under `/sys/class/leds/`: **[live]**. Status LED also controllable via
  U-Boot env `ifm_boot_status_led` and possibly `fw_setenv`.

## Recovery & restore  [live TBD — Task 0.3]

- Stock state of `codesys.service` (`is-enabled`/`is-active`): **[live]**.
- Re-enable command: `systemctl unmask codesys && systemctl enable --now codesys`.
- Ultimate fallback: re-flash stock firmware via the delivery `.swu`
  (USB-stick update path per `sw-description`).
- **Do not disable CODESYS until this section is filled.**
