# PadDesk · 手柄控制台

使用游戏手柄操作 Windows 鼠标和键盘。提供实体按键校准、按住／弹起映射、左摇杆连续鼠标移动，以及实时输入可视化。

界面以原生 WPF 3D 手柄为中心，右侧分为按键映射与移动设置。支持 Xbox 标准布局（A 下、B 右、X 左、Y 上），以及照片参考黑色手柄（B 下、A 右、Y 左、X 上）。通过舞台右上角切换，每个设备分别保存，校准向导使用相同模型。照片模型是外观与布局近似，不表示自动识别品牌。无需下载模型或安装额外运行库。

适用于 Windows 10 / 11 **64 位**，使用 Windows 自带的 .NET Framework 4.x。手柄输入使用开源 **SDL 2.32.10**。无需安装 Python 或额外的 .NET SDK。

## 直接运行

1. 下载仓库或 Releases 中的便携压缩包并完整解压。
2. 双击 `PadDesk.exe`。`Main.xaml` 和 `SDL2.dll` 必须与 exe 位于同一文件夹。托盘、窗口与程序图标已内嵌，无需单独分发 ICO。
3. 首次使用点击 **全按键校准**。完全松开按键、扳机和摇杆后，点击 **记录松开状态**，再按提示按下实体键。没有的按键可以跳过。
4. 校准后选中某一行，选择 **鼠标左键／鼠标右键／键盘按键**，点击 **应用**。键盘支持单键及 Ctrl、Alt、Shift 等组合键。
5. 如只想识别某一个实体键，点击 **按键定位**，按下它后在列表中设置输出或确认名称。

校准以手柄实际报告的原始按钮、方向帽或轴为准。库提供的初始字母名称仅作为待确认提示，不会自动给未校准的普通按键分配点击。向导确认实体 A 时，若它没有输出，会默认分配鼠标左键。

## 操控方式

- **3D 手柄**：点击模型、映射行或新按下已对准的实体键，镜头平滑聚焦对应位置，右侧同步选中映射。按住 A 再按 B 会转向 B，松开保留最后焦点；连续按键可在镜头移动途中切换目标。编辑名称、打开输出下拉框及校准期间不会自动抢走编辑或提示焦点。拖动模型可以旋转，“全貌”返回总览；按键下沉并亮起，摇杆跟随输入倾斜。
- **紧凑布局**：顶部合并设备信息与状态；右侧上方编辑当前按键，下方滚动查看映射。Switch Pro 风格模型细化外壳、握把纹理、摇杆和肩键，沿用原有 Photo 配置标识。
- **可视校准**：全按键校准与主界面共用模型，发光标记当前目标；肩键、扳机自动切换上缘视角。识别成功后短暂显示确认，再进入下一键；必须松开后才能录入下一步。点击模型不会代替实体输入。扩展键显示文字提示，可跳过。
- **布局说明**：提供 Xbox 与照片参考两种布局，手动切换不会改变已有按键映射。保留已校准的实体名称及输出；未知名称、尚未对准的普通键不会错误点亮已知键。右摇杆方向展示使用 SDL 标准轴，未识别的设备不猜测轴号。
- **按下／弹起同步**：手柄键按住时保持鼠标或键盘按下，松开时弹起。支持鼠标拖拽。
- **左摇杆**：连续移动鼠标，水平、垂直使用相同速度；支持速度、回中死区和操控曲线。首次需要记录中立状态，或使用“校准左摇杆方向与中心”完成校准。
- **方向键**：保持步进移动，不改为其他输出。在“移动设置”中填写水平、垂直步长（1–5000 px 整数），点击“应用步长”保存。新配置默认左右 30、上下 20 物理像素，长按 350 ms 后每 90 ms 重复。若同级 `GamepadMouse/settings.json` 存在，则导入旧程序的当前步长及重复配置。
- **暂停／恢复**：`Ctrl+Alt+F12`，或控制台、托盘菜单。
- **后台运行**：关闭主窗口会收起到托盘，右键托盘图标选择“退出”结束程序。未设置开机自启。

校准、键盘录入、输入配置编辑、暂停或设备断开时停止输出并尝试释放程序持有的键与鼠标按钮。Windows 权限规则可能阻止普通程序操作管理员窗口；这种情况下暂停并返回普通窗口，让程序重试释放。

## 配置与设备支持

每种设备的配置保存在 `profiles/<设备 GUID>.json`。此目录、备份、日志和运行状态均不进入 Git 仓库；更新程序或重新克隆时，请自行保留个人 `profiles` 目录。

优先支持 SDL 已识别的标准手柄；也可读取具有方向帽的普通手柄。未识别且方向键仅报告为按钮或轴的手柄，固定方向键功能可能需要额外适配。设备没有上报给 Windows／SDL 的特殊按键无法在本程序中识别。

## 从源码编译

在 Windows PowerShell 或 PowerShell 7 中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

编译器使用系统自带的 `.NET Framework64\v4.0.30319\csc.exe`。代码使用 WPF 界面；`Main.xaml` 在运行时加载。发布时一并携带 exe、XAML、SDL DLL 和许可证。

## 测试

只运行逻辑测试，不操作实际鼠标或键盘：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -LogicOnly
```

运行完整集成测试前，从托盘退出 PadDesk，连接手柄，并在可交互的 Windows 桌面执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
```

集成测试会打开专用测试窗口，检查真实鼠标移动、左右键按下／弹起、键盘按下／弹起及释放清理。完成后恢复鼠标位置；测试配置写入单独的临时目录。

验证 3D 视图和校准流程（不发送实际鼠标／键盘输出，不写入个人配置）：

```powershell
New-Item -ItemType Directory -Force artifacts/preview | Out-Null
.\build.ps1 -OutputPath artifacts/preview/PadDesk.exe
Copy-Item Main.xaml,SDL2.dll artifacts/preview
.\test-visual.ps1
```

测试会检查聚焦、按压、断开、切换设备、校准松键与跳过行为，并将界面渲染预览保存到 `artifacts/preview`。

## 代码结构

| 文件 | 用途 |
| --- | --- |
| `App.cs`、`Main.xaml`、`Dialogs.cs` | 控制台、托盘、校准向导和键盘录入 |
| `ControllerView.cs`、`ControllerLayout.cs` | 原生 3D 网格、材质、相机、按压动画与实体名称对应 |
| `Device.cs` | SDL 原始输入与默认设备识别 |
| `Model.cs` | 输入、映射和本地配置 |
| `Engine.cs` | 按键保持状态、方向键步进、摇杆速度计算 |
| `NativeOutput.cs` | Windows 鼠标及键盘事件、按键释放 |
| `Tests.cs`、`IntegrationTests.cs` | 自动化逻辑测试和实机集成测试 |
| `VisualTests.cs`、`test-visual.ps1` | 3D 视图与校准行为检查、界面渲染 |

## 开源许可

PadDesk 源码及应用使用 [MIT License](LICENSE.txt)。

随附的 `SDL2.dll` 来自 [SDL 官方 2.32.10 发行包](https://github.com/libsdl-org/SDL/releases/tag/release-2.32.10)，保持原样，使用 zlib 许可，见 [LICENSE-SDL.txt](LICENSE-SDL.txt) 和 [README-SDL.txt](README-SDL.txt)。
