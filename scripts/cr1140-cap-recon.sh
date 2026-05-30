#!/bin/sh
# cr1140-cap-recon.sh — read-only capability recon for ADR-0001 conditional items.
# Buzzer / hardware watchdog / ambient-light / retain(FRAM). Non-destructive.
sec() { echo; echo "==================== $1 ===================="; }

sec "BUZZER / BEEPER (EV_SND bit, pwm-beeper/gpio-beeper)"
echo "--- /proc/bus/input/devices (look for 'EV=' with bit 0x.. and SND= line) ---"
cat /proc/bus/input/devices 2>/dev/null
echo "--- sysfs/ dmesg ---"
ls /sys/class/ 2>/dev/null | grep -i beep
dmesg 2>/dev/null | grep -iE 'beep|buzzer|pwm-beeper'

sec "HARDWARE WATCHDOG (imx2-wdt expected; who owns /dev/watchdog)"
ls -l /dev/watchdog* 2>/dev/null || echo "(no /dev/watchdog*)"
for w in /sys/class/watchdog/*/identity /sys/class/watchdog/*/timeout /sys/class/watchdog/*/state; do
  [ -e "$w" ] && printf '%s = ' "$w" && cat "$w" 2>/dev/null
done
echo "--- systemd RuntimeWatchdog (is it already claimed?) ---"
grep -i 'RuntimeWatchdog\|RebootWatchdog' /etc/systemd/system.conf 2>/dev/null || echo "(no RuntimeWatchdog set)"
command -v lsof >/dev/null 2>&1 && lsof /dev/watchdog 2>/dev/null
command -v fuser >/dev/null 2>&1 && fuser -v /dev/watchdog 2>/dev/null
dmesg 2>/dev/null | grep -iE 'wdt|watchdog'

sec "AMBIENT-LIGHT SENSOR (IIO illuminance channel; prior: absent)"
ls /sys/bus/iio/devices/ 2>/dev/null || echo "(no IIO devices)"
for c in /sys/bus/iio/devices/iio:device*/name /sys/bus/iio/devices/iio:device*/in_illuminance*; do
  [ -e "$c" ] && printf '%s = ' "$c" && cat "$c" 2>/dev/null
done
dmesg 2>/dev/null | grep -iE 'illuminance|ambient|light-sensor|isl29|opt300|tsl25|apds9'

sec "RETAIN / FRAM (software-only daemon vs nvmem/FRAM hardware)"
echo "--- what does ifm-retain-srv actually persist, and where? ---"
systemctl cat ifm-retain-srv.service 2>/dev/null || echo "(no ifm-retain-srv unit)"
echo "--- nvmem / FRAM / nvram device nodes ---"
ls /sys/bus/nvmem/devices/ 2>/dev/null || echo "(no nvmem devices)"
ls -l /dev/fram* /dev/nvram* 2>/dev/null || echo "(no /dev/fram* /dev/nvram*)"
dmesg 2>/dev/null | grep -iE 'fram|fm24|fm25|mb85|nvmem|nvram' | head

echo; echo "==================== DONE ===================="
