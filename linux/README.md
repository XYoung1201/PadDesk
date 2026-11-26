# PadDesk for Raspberry Pi 5 / Linux

用游戏手柄、红外遥控器、蓝牙遥控器、翻页笔或电视遥控器（HDMI-CEC）操作树莓派的鼠标和键盘。功能与 Windows 版一致：按住／弹起同步、组合键、媒体键、按键序列（含 HOG 字母热键）、左摇杆连续移动鼠标、方向键步进移动、遥控外设学习。

程序分成两部分：

- **后台服务** `paddesk.service`：systemd 系统服务，开机自动启动，不需要登录桌面。它通过 evdev 读取手柄和按键外设，再通过 uinput 创建虚拟键盘和鼠标输出。Wayland（Raspberry Pi OS Bookworm 默认）、X11 和纯命令行都能用。
- **前台配置界面**：在应用菜单中打开“PadDesk 手柄控制台”，或终端执行 `paddesk`。界面是只监听本机（127.0.0.1:7381）的网页，用 Chromium 以独立窗口打开。**关闭窗口后映射照常运行**，再次打开即可继续修改。

适用于 Raspberry Pi 5（64 位 Raspberry Pi OS Bookworm / Trixie），其他运行 systemd 的 Debian 系 Linux（含树莓派 4、x86 PC）同样可用。只需要 `python3` 和 `python3-evdev`，安装脚本会自动安装。

## 安装

```sh
git clone https://github.com/XYoung1201/PadDesk.git
cd PadDesk/linux
sudo ./install.sh
```

安装脚本会：

1. 安装 `python3-evdev`（缺少中文字体时一并安装 `fonts-wqy-microhei`）；
2. 把程序复制到 `/opt/paddesk`，命令 `paddesk` 放到 `/usr/local/bin`，应用菜单添加“PadDesk 手柄控制台”；
3. 创建系统用户 `paddesk`（属于 `input` 组），添加 udev 规则让该组可以使用 `/dev/uinput`，并开机加载 `uinput` 模块；
4. 注册并启动 `paddesk.service`。

更新：`git pull` 后再次执行 `sudo ./install.sh`，配置不受影响。卸载：`sudo ./uninstall.sh`（配置保留在 `/var/lib/paddesk`，加 `--purge` 一并删除）。

## 使用

1. 连接手柄（USB 或在系统蓝牙设置中配对）。服务每 1.5 秒检测一次新设备，界面顶部会显示手柄名称。
2. 首次连接会生成默认配置：左摇杆移动鼠标，方向键（方向帽或 `BTN_DPAD_*`）步进移动。其余按键显示内核报告的名称（A、B、L1 等），标为“待确认”。
3. 点击 3D 示意图上的按键或列表中的一行，选择输出：鼠标左／右／中键、键盘按键（可勾选 Ctrl／Alt／Shift／Super 组成组合）、媒体键、按键序列或鼠标步进，点击 **应用**。
4. 不确定是哪个键时点击 **按键定位**，按下实体键后列表自动选中它。
5. 遥控器、翻页笔等外设：切换到“遥控 / 蓝牙外设”，点击 **学习按键**，按一下外设上的键即可加入列表。
6. “移动设置”调整摇杆速度、死区、曲线、轴与方向，以及方向键步长和长按重复。
7. 暂停／恢复：`Ctrl+Alt+F12`、界面右上角按钮，或终端 `paddesk toggle`。

命令行：

```sh
paddesk            # 打开配置窗口
paddesk status     # 查看状态
paddesk pause | resume | toggle
journalctl -u paddesk -f    # 服务日志
```

配置保存在 `/var/lib/paddesk/profiles/`，每个手柄一个文件（按 USB VID:PID 和名称区分），遥控外设统一保存在 `remotes.json`。

## 与 Windows 版的差异

| 功能 | 树莓派 / Linux 版 |
| --- | --- |
| 手柄识别 | 使用 Linux 内核驱动（xpad、hid-playstation、hid-nintendo 等）报告的 evdev 按键码，而不是 SDL。Xbox、DualShock／DualSense、Switch Pro、8BitDo 等常见手柄可直接使用。默认名称来自内核按键码，个别手柄（例如部分 Xbox 驱动版本）的 X／Y 名称可能互换，用“按键定位”确认即可。Windows 的 `profiles/*.json` 不能直接导入，需要在树莓派上重新设置一次。 |
| 3D 手柄视图 | 改为 2D 示意图（Xbox／任天堂两种布局），同样实时显示按下、摇杆位置，点击可选中按键。没有 WPF 3D 镜头动画和分步校准向导，改用“按键定位”逐个确认。 |
| 左摇杆中心 | 内核会报告轴范围，无需先“记录中立状态”。摇杆有漂移时可在“移动设置”中点击“松开摇杆后记录中心”。 |
| 鼠标移动 | 通过虚拟相对鼠标输出，系统的指针加速设置会作用于它，所以方向键步长和摇杆速度是“近似像素”，与 Windows 的精确像素不同。若需要精确步长，可在桌面设置里关闭鼠标加速。 |
| 遥控／蓝牙外设 | 支持范围更广：任何以键盘或媒体键形式出现的设备，包括 USB 红外接收器、树莓派 GPIO 红外接收器（`gpio-ir`）、蓝牙遥控器、翻页笔，以及通过 HDMI-CEC 连接的电视遥控器。新增“独占”选项：勾选后外设原本的功能被屏蔽，只执行映射（Windows 版只能旁听）。 |
| HOG 热键 | “按键序列”的 HOG 模式照常发送字母序列（`*`／`+` 为左／右 Ctrl）。但 **HOG 目前没有 Linux 版**，所以树莓派上的这些序列只有在运行着能识别它们的程序时才有意义；如果把 HOG.conf 放到 `/etc/HOG/HOG.conf` 或在“设置”中填写可读的路径，界面会列出其中的热键。服务以 `paddesk` 用户运行，无法读取权限为 700 的家目录。 |
| Ctrl+Alt+F12 | 可以暂停／恢复，但按键不会被拦截，当前窗口也会收到 F12。 |
| 托盘图标 | 没有托盘。服务独立于桌面运行，配置窗口随时打开或关闭。 |
| 管理员窗口限制 | 不存在：uinput 是内核级输入，所有程序（包括登录界面和命令行）都能收到。 |
| 开机自启 | 默认开机自启（Windows 版未设置）。不需要时执行 `sudo systemctl disable paddesk`。 |

## 安全

配置接口只监听 `127.0.0.1`，只接受本机地址的 Host，并且所有修改请求都必须带自定义请求头，浏览器中的其他网页无法调用它。服务以无登录权限的 `paddesk` 系统用户运行，只额外拥有 `input` 组权限（读取输入设备、创建虚拟键鼠），并启用了 systemd 的文件系统保护。注意：同一台树莓派上登录的任何本地用户都能打开配置界面。

## 开发与测试

```sh
cd linux
python3 -m unittest discover -s tests -v        # 逻辑、服务和本机 API 测试，无需硬件
python3 tests/demo_server.py 7390               # 用模拟设备运行界面: http://127.0.0.1:7390/
sudo systemctl stop paddesk
sudo python3 -m unittest tests.test_hardware -v # 在树莓派上：用 uinput 虚拟手柄做端到端测试
sudo systemctl start paddesk
```

| 文件 | 用途 |
| --- | --- |
| `paddesk/keys.py` | Linux 按键码、显示名称、按键名解析 |
| `paddesk/model.py` | 输入源、映射、按键序列、HOG 配置读取、配置存储 |
| `paddesk/engine.py` | 按住状态、媒体键重复、序列播放、方向键步进、摇杆速度（移植自 `Engine.cs`） |
| `paddesk/devices.py` | evdev 设备发现、分类、热插拔、独占 |
| `paddesk/output.py` | uinput 虚拟键盘和鼠标 |
| `paddesk/service.py` | 后台主循环、学习按键、暂停热键、供界面调用的操作 |
| `paddesk/server.py` | 本机 HTTP 接口与静态页面 |
| `paddesk/web/` | 配置界面 |
| `packaging/`、`install.sh`、`uninstall.sh` | systemd 单元、udev 规则、桌面菜单项、安装与卸载 |
