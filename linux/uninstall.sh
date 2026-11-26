#!/bin/sh
# Removes PadDesk. Your mappings in /var/lib/paddesk are kept unless you pass --purge.
set -eu
[ "$(id -u)" -eq 0 ] || { echo "请用 sudo 运行: sudo $0"; exit 1; }
systemctl disable --now paddesk 2>/dev/null || true
rm -f /etc/systemd/system/paddesk.service /etc/udev/rules.d/70-paddesk-uinput.rules /etc/modules-load.d/paddesk.conf
rm -f /usr/local/bin/paddesk /usr/share/applications/paddesk.desktop
rm -rf /opt/paddesk
systemctl daemon-reload
udevadm control --reload-rules || true
if [ "${1:-}" = "--purge" ]; then
  rm -rf /var/lib/paddesk
  userdel paddesk 2>/dev/null || true
  echo "PadDesk 及其配置已删除。"
else
  echo "PadDesk 已卸载，配置保留在 /var/lib/paddesk（加 --purge 一并删除）。"
fi
