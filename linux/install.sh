#!/bin/sh
# PadDesk for Raspberry Pi OS / Debian: installs the background service and the configuration UI.
# Usage: sudo ./install.sh
set -eu
HERE=$(cd "$(dirname "$0")" && pwd)
[ "$(id -u)" -eq 0 ] || { echo "请用 sudo 运行: sudo $0"; exit 1; }

echo "==> 安装依赖 (python3-evdev)"
apt-get update -qq || true
apt-get install -y python3 python3-evdev
apt-get install -y libglib2.0-bin playerctl || true  # gdbus / playerctl for play-pause
apt-get install -y v4l-utils || true                 # cec-ctl for projector power over HDMI-CEC
if command -v fc-list >/dev/null && [ -z "$(fc-list :lang=zh 2>/dev/null | head -n1)" ]; then
  echo "==> 安装中文字体"
  apt-get install -y fonts-wqy-microhei || true
fi

echo "==> 复制程序到 /opt/paddesk"
systemctl stop paddesk 2>/dev/null || true
rm -rf /opt/paddesk/paddesk
mkdir -p /opt/paddesk
cp -r "$HERE/paddesk" /opt/paddesk/
find /opt/paddesk -name __pycache__ -prune -exec rm -rf {} +
install -m 0755 "$HERE/packaging/paddesk.sh" /usr/local/bin/paddesk
install -m 0644 "$HERE/packaging/paddesk.desktop" /usr/share/applications/paddesk.desktop
install -m 0644 "$HERE/packaging/paddesk-agent.desktop" /etc/xdg/autostart/paddesk-agent.desktop

echo "==> 创建服务用户并开放 uinput"
id paddesk >/dev/null 2>&1 || useradd --system --no-create-home --home-dir /var/lib/paddesk --shell /usr/sbin/nologin paddesk
usermod -a -G input paddesk
usermod -a -G video paddesk   # /dev/cec* (HDMI-CEC)
install -m 0644 "$HERE/packaging/70-paddesk-uinput.rules" /etc/udev/rules.d/70-paddesk-uinput.rules
echo uinput > /etc/modules-load.d/paddesk.conf
modprobe uinput || true
udevadm control --reload-rules
udevadm trigger --subsystem-match=misc --sysname-match=uinput || true
[ -e /dev/uinput ] && chgrp input /dev/uinput && chmod 0660 /dev/uinput || true

echo "==> 注册并启动 systemd 服务"
install -m 0644 "$HERE/packaging/paddesk.service" /etc/systemd/system/paddesk.service
systemctl daemon-reload
systemctl enable --now paddesk
sleep 1
if systemctl is-active --quiet paddesk; then
  echo
  echo "PadDesk 已安装并在后台运行（开机自启）。"
  echo "打开配置界面：应用菜单 → 首选项 / 附件 → PadDesk 手柄控制台，或终端执行 paddesk"
  echo "关闭配置窗口不会停止映射。查看日志：journalctl -u paddesk -f"
else
  echo "服务未能启动，请查看：journalctl -u paddesk -e"; exit 1
fi
