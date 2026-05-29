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

# The splash/launcher also draws to the framebuffer; stop running instances so
# our app owns /dev/fb0. (Best-effort; names from device-facts.md.)
systemctl stop 'ifm-ecopanel@*' 2>/dev/null || true

install -d "$APPDIR"
install -m 0644 /tmp/cr1140-app.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now cr1140-app.service
echo "CODESYS masked; cr1140-app enabled."
