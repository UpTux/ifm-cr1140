sec() { echo; echo "==================== $1 ===================="; }
NVD=/sys/bus/nvmem/devices
look() {
  d="$NVD/$1/nvmem"; [ -e "$d" ] || { echo "($1 absent)"; return; }
  echo "--- $1 (size=$(wc -c < "$d")) : first 512B ---"
  dd if="$d" bs=512 count=1 2>/dev/null | hexdump -C | head -n 34
  echo "[printable strings >=4]"
  tr -c '[:print:]' '\n' < "$d" | grep -E '.{4,}' | sort -u | head -n 40
}
sec "SPI EEPROM spi1.00 — must be free of factory/calibration"
look spi1.00
sec "I2C 0x51 (0-00513) — confirmed factory home (has MAC); what else?"
look 0-00513
sec "I2C 0x50 (0-00502)"
look 0-00502
