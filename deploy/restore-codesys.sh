#!/bin/sh
# Undo install.sh: remove our app and bring the stock CODESYS runtime back.
# Run ON THE DEVICE as root.
set -e

CODESYS=codesys.service

echo "Disabling cr1140-app ..."
systemctl disable --now cr1140-app.service || true
rm -f /etc/systemd/system/cr1140-app.service

# Stock state on this device is unmasked + DISABLED + inactive (no CODESYS
# project loaded). Restore to exactly that — do NOT enable/start it.
echo "Unmasking $CODESYS (leaving it disabled, per stock state) ..."
systemctl unmask "$CODESYS" || true
systemctl disable "$CODESYS" 2>/dev/null || true

# ifm-retain-srv stock state is enabled + active; it reinitializes its EEPROM
# segments from CODESYS RAM on the next CODESYS run. Unmask + restart so stock
# restore is clean. See ADR-0002.
echo "Restoring ifm-retain-srv (stock: enabled+active) ..."
systemctl unmask ifm-retain-srv || true
systemctl enable --now ifm-retain-srv 2>/dev/null || true

# app-launcher.service stock state is enabled + active — re-enable and start it
# so the ifm setup screen / CODESYS chooser returns.
echo "Restoring app-launcher.service (stock: enabled+active) ..."
systemctl unmask app-launcher.service || true
systemctl enable --now app-launcher.service || true

systemctl daemon-reload
echo "CODESYS restored to stock (unmasked, disabled); app-launcher restored; cr1140-app removed."
