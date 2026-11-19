# Win-Mirro-Overlay

[![build-windows](https://github.com/BearingSteel/Win-Mirro-Overlay/actions/workflows/build.yml/badge.svg)](https://github.com/BearingSteel/Win-Mirro-Overlay/actions/workflows/build.yml)

把任意一个正在运行的 Windows 窗口，**实时镜像**成一个独立的悬浮窗：可置顶、可缩放、可拖动、可鼠标穿透、可调透明度。

<img src="assets/app-preview.png" width="96" alt="应用图标" />

> 只支持 Windows（Windows 10 1809 / build 17763 及以上），基于 .NET 8 + WPF。

---

## 功能

| 需求 | 实现 |
| --- | --- |
| 启动后先选窗口 | 窗口 1 列出当前所有可见的顶层窗口（标题、进程名、图标），支持搜索、刷新 |
| 右上角关闭即退出 | 窗口 1 使用系统标题栏，点 X 直接退出程序 |
| 选择后出现镜像窗口 | 单击列表项 → 窗口 1 关闭，窗口 2 出现并开始实时镜像 |
| 窗口 2 可调大小 / 可移动 | 拖边框缩放；拖标题栏或画面任意位置移动（支持贴边吸附） |
| 右上角关闭 / 置顶按钮 | 自绘的无边框标题栏，`✕` 关闭程序，`📌` 切换置顶（高亮表示已置顶） |
| 右下角托盘图标 + 菜单 | 左键/右键托盘图标都会弹出菜单 |
| 菜单：置顶 | 「置顶显示」勾选项 |
| 菜单：点击穿透 | 「点击穿透」勾选项（开启后鼠标完全穿过悬浮窗） |
| 菜单：退出 | 「退出」 |
| 菜单：缩放 25%–250% | 「缩放显示」子菜单，25% 起每 25% 一档直到 250% |
| 允许重复启动 | 不使用单实例锁，每个进程各自独立、各自一个托盘图标 |

额外：托盘菜单里还有「透明度」（100% / 90% / 75% / 50% / 25%）和「重新选择源窗口」。

## 截图 / 说明

- 缩放的含义：**只缩放画面内容，窗口尺寸不变**。内容以「源窗口像素尺寸 × 百分比」居中显示——小于窗口时四周留空，大于窗口时居中裁切。
- 穿透开启后窗口不再接收任何鼠标事件，此时只能通过托盘菜单操作（关闭穿透、关闭窗口、退出）。
- 源窗口被最小化时画面会暂停并提示；源窗口关闭时会提示，可从托盘菜单重新选择。

## 环境要求

**运行**

- Windows 10 1809（build 17763）或更高 / Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（若使用 framework-dependent 版本）

**编译**

- Windows + Visual Studio 2022（勾选「.NET 桌面开发」工作负载）
- 或只装 .NET 8 SDK：`dotnet build`

## 快速开始

### 用 Visual Studio

1. `git clone https://github.com/BearingSteel/Win-Mirro-Overlay.git`
2. 双击 `Win-Mirro-Overlay.sln`
3. `F5` 运行，或在「生成 → 发布」里发布

### 用命令行

```powershell
git clone https://github.com/BearingSteel/Win-Mirro-Overlay.git
cd Win-Mirro-Overlay

# 调试运行
dotnet run --project src/WinMirroOverlay/WinMirroOverlay.csproj

# 发布（依赖已安装的 .NET 8 Desktop Runtime）
dotnet publish src/WinMirroOverlay/WinMirroOverlay.csproj -c Release -r win-x64 --self-contained false -o publish
```

发布产物 `publish/WinMirroOverlay.exe` 可直接双击运行；同一目录下的 dll 需要一起带上。

> 想做成单文件、免安装 .NET 运行时：
> `dotnet publish src/WinMirroOverlay/WinMirroOverlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-standalone`

## 使用流程

1. 双击 `WinMirroOverlay.exe`，出现窗口 1「选择需要镜像显示的窗口」。
2. 在列表里**单击**目标窗口（也可以输入关键字过滤，回车镜像选中项，Esc 退出）。
3. 窗口 1 关闭，窗口 2 出现并开始镜像。
4. 用托盘菜单或窗口右上角按钮控制置顶、穿透、透明度、缩放。
5. 关闭窗口 2 或托盘「退出」结束本次镜像。

再次双击 exe 可以开第二个独立实例（例如同时镜像两个窗口），每个实例有各自的托盘图标和菜单。

## 实现要点

- **采集**：`Windows.Graphics.Capture`（WGC）+ D3D11。WGC 取的是窗口合成后的内容，源窗口被其它窗口遮挡时依然能镜像；帧通过 `ID3D11Texture2D` → staging 纹理 → `Map` 拷到 CPU，再写入 `WriteableBitmap`，限制在约 30fps 以免空耗 CPU。
- **悬浮窗**：WPF 无边框窗口 + `AllowsTransparency`（本身就是分层窗口），因此能把 `WS_EX_TRANSPARENT` 直接加上去做真正的跨进程鼠标穿透；整体透明度用 `Window.Opacity`。
- **为什么不直接用 DWM 缩略图**：`DwmRegisterThumbnail` 往分层窗口上投射并不可靠，而穿透/半透明都必须依赖分层窗口，所以改走「采集 + 自绘」这条路。
- **无边框窗口的缩放/拖动**：自己处理 `WM_NCHITTEST` 返回 `HTLEFT/HTTOP...`，拖动走 `DragMove()`（内部是 `WM_NCLBUTTONDOWN + HTCAPTION`，因此保留系统贴边/吸附行为）。
- **进程内互操作**：`RoGetActivationFactory` 取 `GraphicsCaptureItem` 的激活工厂 → QI 出 `IGraphicsCaptureItemInterop` → `CreateForWindow`；WinRT 对象到托管对象用 CsWinRT 的 `FromAbi`（会接管引用，不能再 `Release`）。

## 已知限制

- **受保护内容**：DRM 视频（如 Netflix/部分播放器）、受保护进程的窗口会黑屏或被拒绝捕获，这是系统限制。
- **独占全屏游戏**：WGC 通常抓不到，建议游戏用「无边框窗口」模式。
- **最小化**：源窗口最小化时 DWM 停止渲染，画面会暂停（不会崩）。
- **权限**：如果目标窗口属于管理员权限进程，本程序也需要以管理员身份运行，否则捕获失败。
- **UWP / WinUI 应用**：通常可以捕获；少数应用可能返回黑帧。
- **DPI**：已声明 PerMonitorV2；跨不同缩放比例的显示器拖动时，缩放比例按当前窗口 DPI 计算。
- 点击穿透后无法再用鼠标操作悬浮窗，只能通过托盘菜单恢复。

## 目录结构

```
Win-Mirro-Overlay.sln
assets/logo.svg                # logo 设计源（512x512）
assets/app.ico                 # 应用与托盘图标（tools/make_icon.py 按 logo.svg 生成）
src/WinMirroOverlay/
  App.xaml(.cs)                # 启动流程：选择窗口 → 悬浮窗
  app.manifest                 # PerMonitorV2 DPI
  Capture/                     # WGC 采集（D3D11 → CPU 帧）
    WindowCaptureService.cs
    CaptureItemFactory.cs
  Interop/                     # Win32 P/Invoke 与窗口枚举
  Services/TrayIconController.cs
  Views/PickerWindow.*         # 窗口 1
  Views/OverlayWindow.*        # 窗口 2
tools/make_icon.py             # 纯 Python 生成多尺寸 ico
.github/workflows/build.yml    # windows-latest 上编译 + 发布
```

## 第三方依赖

- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)（Direct3D11 / DXGI 绑定，MIT）

## License

[MIT](LICENSE)
