#!/bin/sh
# cr1140-recon.sh — read-only reconnaissance of the ifm ecomatDisplay CR1140/CR1141
# Goal: identify Ethernet, buttons (F1-F6/arrows/enter), CAN and the display path
# WITHOUT relying on tools that a hardened Yocto image may not ship. Everything
# here is read-only and non-destructive. The two interactive steps (button
# mapping, CAN bring-up) are deliberately NOT in this script — see notes at end.
#
# Run as root (or the system-password account):  sh cr1140-recon.sh 2>&1 | tee recon.txt

have() { command -v "$1" >/dev/null 2>&1; }
sec()  { echo; echo "==================== $1 ===================="; }
dump() { for f in "$@"; do [ -e "$f" ] && printf '%-44s = %s\n' "$f" "$(cat "$f" 2>/dev/null | tr '\n' ' ')"; done; }

sec "0. BASELINE: kernel, SoC, memory, toolbox"
uname -a
[ -r /etc/os-release ] && cat /etc/os-release
echo "--- /proc/cpuinfo ---"; cat /proc/cpuinfo
echo "--- arch (decides Go GOARCH/GOARM and input_event size) ---"; uname -m
echo "--- memory ---"; head -5 /proc/meminfo
echo "--- kernel cmdline (console=, fb=, etc.) ---"; cat /proc/cmdline
echo "--- which optional tools exist ---"
for t in ip ifconfig ethtool ethtool evtest getevent fbset modetest can-utils candump cansend lsof fuser dmesg systemctl busybox; do
  if have "$t"; then echo "  yes  $t"; else echo "  --   $t"; fi
done
have busybox && { echo "--- busybox applets ---"; busybox --list 2>/dev/null | tr '\n' ' '; echo; }

sec "0b. BOOT LOG — single richest source; grep per-subsystem below"
if have dmesg && dmesg >/dev/null 2>&1; then DMESG="dmesg"; else DMESG="cat /var/log/dmesg"; fi
$DMESG 2>/dev/null | grep -iE 'fb|drm|panel|lcd|backlight|mxsfb|imx-drm|ili9|otm|gpio|keys|matrix|input|can|flexcan|mcp25|eth|fec|phy|mii' || \
  echo "(dmesg not accessible to this user — try as root, or check /var/log)"

sec "0c. LOADED DRIVERS (names tell you the display/CAN/keys hardware)"
[ -r /proc/modules ] && cut -d' ' -f1 /proc/modules | sort || lsmod 2>/dev/null

sec "0d. DEVICE TREE (embedded gold: SoC + every peripheral as wired)"
[ -r /proc/device-tree/model ] && { printf 'model = '; cat /proc/device-tree/model; echo; }
[ -r /proc/device-tree/compatible ] && { printf 'compatible = '; tr '\0' ' ' < /proc/device-tree/compatible; echo; }
[ -d /proc/device-tree ] && echo "--- top-level DT nodes ---" && ls /proc/device-tree

sec "1. DISPLAY — fbdev vs DRM decides the whole rendering approach"
echo "--- device nodes ---"; ls -l /dev/fb* /dev/dri/* 2>/dev/null || echo "(no /dev/fb* or /dev/dri/*)"
echo "--- fbdev sysfs (if present) ---"
dump /sys/class/graphics/fb0/virtual_size /sys/class/graphics/fb0/bits_per_pixel \
     /sys/class/graphics/fb0/stride /sys/class/graphics/fb0/modes \
     /sys/class/graphics/fb0/name /sys/class/graphics/fb0/rotate
have fbset && { echo "--- fbset -i ---"; fbset -i 2>/dev/null; }
echo "--- DRM sysfs (if present) ---"
for c in /sys/class/drm/card*/status; do [ -e "$c" ] && dump "$c" "${c%status}modes" "${c%status}enabled"; done
have modetest && { echo "--- modetest -c (connectors/modes) ---"; modetest -c 2>/dev/null | head -40; }
echo "--- backlight ---"
dump /sys/class/backlight/*/max_brightness /sys/class/backlight/*/brightness /sys/class/backlight/*/type
echo "--- who currently holds the framebuffer (this is the CODESYS visu) ---"
have lsof  && lsof /dev/fb0 /dev/dri/card0 2>/dev/null
have fuser && fuser -v /dev/fb0 /dev/dri/card0 2>/dev/null

sec "2. BUTTONS — enumerate input devices and their KEY capability bitmaps"
# /proc/bus/input/devices lists each device: Name, Handlers (eventN), and the
# EV/KEY bitmaps = which event codes it CAN emit. The keypad is almost certainly
# a gpio-keys / matrix-keypad node. Physical->keycode mapping needs key presses
# (step A below); this just tells you WHICH event node and the candidate codes.
echo "--- /proc/bus/input/devices ---"; cat /proc/bus/input/devices 2>/dev/null
echo "--- /dev/input ---"; ls -l /dev/input/ 2>/dev/null
echo "--- per-node names ---"
for n in /sys/class/input/event*/device/name; do [ -e "$n" ] && printf '%s: ' "${n%/device/name}" && cat "$n"; done
echo "--- RGB keypad backlight likely lives here ---"
ls /sys/class/leds/ 2>/dev/null && dump /sys/class/leds/*/max_brightness

sec "3. CAN — interface presence and driver (SocketCAN expected)"
echo "--- net interfaces (canX appears here even without iproute2) ---"; ls /sys/class/net/
for i in /sys/class/net/can*; do
  [ -e "$i" ] || continue
  echo "--- $i ---"
  dump "$i/operstate" "$i/mtu" "$i/type"
  dump "$i/can_bittiming/bitrate" "$i/can_state"
done
have ip && { echo "--- ip -d -s link (bitrate/state/counters) ---"; ip -d -s link show 2>/dev/null; }
[ -r /proc/net/can/stats ] && { echo "--- /proc/net/can ---"; cat /proc/net/can/stats; }

sec "4. ETHERNET — confirm iface, driver, link (this is how you SSH'd in)"
for i in /sys/class/net/eth* /sys/class/net/en* /sys/class/net/usb*; do
  [ -e "$i" ] || continue
  echo "--- $i ---"; dump "$i/operstate" "$i/speed" "$i/address" "$i/carrier"
done
have ip       && ip addr show 2>/dev/null
have ifconfig && ifconfig -a 2>/dev/null
have ethtool  && for d in eth0 eth1; do echo "--- ethtool $d ---"; ethtool "$d" 2>/dev/null; done
echo "--- /proc/net/dev counters ---"; cat /proc/net/dev

sec "5. WHO OWNS THE HARDWARE / how to stop the CODESYS runtime later"
echo "--- processes ---"; (ps aux 2>/dev/null || ps -ef 2>/dev/null || ps) | grep -iE 'codesys|cds|plc|visu' | grep -v grep
echo "--- init system ---"
have systemctl && systemctl list-units --type=service 2>/dev/null | grep -iE 'codesys|cds|plc'
[ -d /etc/init.d ] && { echo "--- /etc/init.d ---"; ls /etc/init.d/; }
for u in /lib/systemd/system /etc/systemd/system; do [ -d "$u" ] && ls "$u" | grep -iE 'codesys|cds|plc'; done
echo "--- runtime install dir + app layout ---"
ls -la /home/cds-apps/ 2>/dev/null
ls -la /var/opt/codesys 2>/dev/null

sec "6. FILESYSTEM — where can you persist your own binary across reboots?"
echo "--- mounts (look for rw, overlay, a /data or /home partition) ---"
cat /proc/mounts 2>/dev/null || mount
echo "--- free space ---"; df -h 2>/dev/null || df

sec "DONE — read-only recon complete. Two interactive steps remain (see header)."
echo "A) Button mapping: run the Go evdev reader (cr1140-evread) against the"
echo "   eventN node identified in section 2, press F1-F6/arrows/enter, record codes."
echo "B) CAN bring-up (state-changing, reversible):"
echo "     ip link set can0 up type can bitrate 250000   # 250k = ifm factory default"
echo "     candump can0     # or a raw SocketCAN reader if can-utils is absent"
echo "     ip link set can0 down"
