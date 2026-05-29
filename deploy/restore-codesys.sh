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
systemctl daemon-reload
echo "CODESYS restored to stock (unmasked, disabled); cr1140-app removed."
