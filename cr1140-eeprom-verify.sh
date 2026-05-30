#!/bin/sh
# cr1140-eeprom-verify.sh — read-only: is factory data on the SPI EEPROM (spi1.00),
# or does it live in the I2C EEPROMs / OCOTP? Decides whether we can own spi1.00.
sec() { echo; echo "==================== $1 ===================="; }
NVD=/sys/bus/nvmem/devices

dumpnv() {  # $1 = nvmem device dir name
  d="$NVD/$1/nvmem"
  if [ ! -e "$d" ]; then echo "($1: no nvmem node)"; return; fi
  sz=$(wc -c < "$d" 2>/dev/null)
  echo "--- $1  (size=${sz} bytes) ---"
  echo "[head 256B hex]"
  dd if="$d" bs=1 count=256 2>/dev/null | hexdump -C 2>/dev/null | head -20
  echo "[printable strings >=4 across whole device]"
  tr -c '[:print:]' '\n' < "$d" 2>/dev/null | grep -E '.{4,}' | head -30
  echo "[MAC 00:02:01:ab:bd:49 present? grep raw bytes]"
  if hexdump -v -e '1/1 "%02x"' "$d" 2>/dev/null | grep -qi '000201abbd49'; then
    echo "  >>> MAC FOUND in $1"
  else
    echo "  (MAC not in $1)"
  fi
}

sec "nvmem inventory"
ls -1 "$NVD" 2>/dev/null

sec "SPI EEPROM — our candidate (must be free of factory data)"
dumpnv spi1.00

sec "I2C EEPROM #1 (0x50) — usual factory home"
dumpnv 0-00502

sec "I2C EEPROM #2 (0x51)"
dumpnv 0-00513

sec "RV-3028 EEPROM / NVRAM (RTC) + SNVS LPGPR"
dumpnv rv3028_eeprom0
dumpnv rv3028_nvram0
dumpnv 30370000.snvs:snvs-lpgpr0

sec "OCOTP fuses (where i.MX MAC is often shadowed)"
dumpnv imx-ocotp0

sec "Is ifm-retain-srv actually running right now?"
command -v systemctl >/dev/null 2>&1 && systemctl is-active ifm-retain-srv 2>/dev/null
command -v fuser >/dev/null 2>&1 && fuser -v /sys/bus/spi/devices/spi1.0/eeprom 2>/dev/null

sec "Where does the kernel say eth0's MAC comes from?"
dmesg 2>/dev/null | grep -iE 'fec|macaddr|mac address|using random|nvmem-cell' | head

echo; echo "==================== DONE ===================="
