#!/bin/sh
# Reversibly replace the CODESYS runtime with our native app.
# Run ON THE DEVICE as root, AFTER confirming docs/device-facts.md §Recovery.
#
# codesys.service has WatchdogSec=2s + StartLimitAction=reboot-force, so we
# must disable+mask it (never just kill) to avoid a forced reboot.
set -e

CODESYS=codesys.service
APPDIR=/home/cds-apps

echo "Stopping + masking $CODESYS ..."
systemctl disable --now "$CODESYS" || true
systemctl mask "$CODESYS" || true

# ifm-retain-srv owns the SPI EEPROM we now use as our retain store (it writes 3
# CODESYS-retain segments to spi1.0/eeprom). Mask it BEFORE our app first writes
# the EEPROM so the daemon can't race our writes. Meaningless without CODESYS;
# restore-codesys.sh unmasks it. See ADR-0002.
echo "Stopping + masking ifm-retain-srv (frees the SPI retain EEPROM) ..."
systemctl mask --now ifm-retain-srv || true

# app-launcher.service runs /opt/ifm/app-launcher/run-app.sh, which (with no
# CODESYS .app present) launches ifm-local-setup — the "setup screen" that also
# writes /dev/fb0 and would race our app. Mask it and kill any live instance so
# our app owns the framebuffer. Stock state is enabled+active (restore re-enables).
echo "Stopping + masking app-launcher.service (frees the framebuffer) ..."
systemctl disable --now app-launcher.service || true
systemctl mask app-launcher.service || true
pkill -f run-app.sh 2>/dev/null || true
pkill ifm-local-setup 2>/dev/null || true
systemctl stop 'ifm-ecopanel@*' 2>/dev/null || true

mkdir -p "$APPDIR"
cp /tmp/cr1140-app.service /etc/systemd/system/cr1140-app.service
chmod 0644 /etc/systemd/system/cr1140-app.service
systemctl daemon-reload
systemctl enable --now cr1140-app.service
echo "CODESYS masked; cr1140-app enabled."
