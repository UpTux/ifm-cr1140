#!/bin/sh
# Undo install.sh: remove our app and bring the stock CODESYS runtime back.
# Run ON THE DEVICE as root.
set -e

CODESYS=codesys.service

echo "Disabling cr1140-app ..."
systemctl disable --now cr1140-app.service || true
rm -f /etc/systemd/system/cr1140-app.service

echo "Unmasking + enabling $CODESYS ..."
systemctl unmask "$CODESYS"
systemctl daemon-reload
systemctl enable --now "$CODESYS"
echo "CODESYS restored; cr1140-app removed."
