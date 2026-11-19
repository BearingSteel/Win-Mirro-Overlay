# Win-Mirro-Overlay

[![build-windows](https://github.com/BearingSteel/Win-Mirro-Overlay/actions/workflows/build.yml/badge.svg)](https://github.com/BearingSteel/Win-Mirro-Overlay/actions/workflows/build.yml)

把任意一个正在运行的 Windows 窗口，**实时镜像**成一个独立的悬浮窗：可置顶、可缩放、可拖动、可鼠标穿透、可调透明度。

<img src="assets/app-preview.png" width="96" alt="应用图标" />

> 只支持 Windows（Windows 10 1809 / build 17763 及以上），基于 .NET 8 + WPF。

---

## 功能

| 需求 | 实现 |
| --- | --- |
| 启动后先选窗口 | 窗口 1 列出所有虚拟桌面的可见顶层窗口（标题、进程名、图标），可刷新 |
| 右上角关闭即退出 | 窗口 1 使用系统标题栏，点 X 直接退出程序（没有悬浮窗时） |
| 关闭镜像窗后重新选 | 关闭窗口 2 不会退出程序，会重新弹出窗口 1 供你换一个窗口 |
| 选择后出现镜像窗口 | 单击列表项 → 窗口 1 关闭，窗口 2 出现并开始实时镜像 |
| 窗口 2 可调大小 / 可移动 | 拖边框缩放；拖标题栏移动窗口（支持贴边吸附）；拖**画面内部**平移镜像内容 |
| 右上角关闭 / 置顶按钮 | 自绘的无边框标题栏，`✕` 关闭镜像，`📌` 直接切到「置顶（点击穿透）」 |
| 右下角托盘图标 + 菜单 | 左键/右键托盘图标都会弹出菜单 |
| 菜单：置顶（三选一） | 「置顶」/「置顶（点击穿透）」/「取消置顶」，单选勾选 |
| 菜单：退出 | 「退出」 |
| 缩放 | 连续 10%–1000%：托盘菜单里的滑块，或窗口内 Ctrl+滚轮（以光标为中心） |
| 透明度 | 连续 10%–100%：托盘菜单里的滑块（实时生效） |
| 允许重复启动 | 不使用单实例锁，每个进程各自独立、各自一个托盘图标 |

托盘菜单里还有「适应窗口（重置缩放与位置）」「重新选择源窗口」「打开日志文件夹」「显示诊断信息」。置顶状态是三选一的单选勾选，缩放/透明度都是连续滑块。

## 截图 / 说明

- 缩放的含义：**只缩放画面内容，窗口尺寸不变**。内容以「源窗口像素尺寸 × 百分比」居中显示——小于窗口时四周留空，大于窗口时居中裁切并可用鼠标拖动平移。
- 窗口内交互：拖标题栏 = 移动窗口；拖画面 = 平移画面；Ctrl+滚轮 = 以光标为锚点缩放；Ctrl+0 复位 100%，Ctrl+加号/减号微调。
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
5. 关闭窗口 2 会回到窗口 1，可以再选一个窗口继续镜像；托盘「退出」或关闭窗口 1 才真正结束程序。

再次双击 exe 可以开第二个独立实例（例如同时镜像两个窗口），每个实例有各自的托盘图标和菜单。

## 实现要点

- **采集**：`Windows.Graphics.Capture`（WGC）+ D3D11。WGC 取的是窗口合成后的内容，源窗口被其它窗口遮挡时依然能镜像；帧通过 `ID3D11Texture2D` → staging 纹理 → `Map` 拷到 CPU，再写入 `WriteableBitmap`，限制在约 30fps 以免空耗 CPU。
- **三级兜底**：WGC `FrameArrived` 事件在个别机器上不触发 → 2.5 秒后主动从帧池轮询取帧；帧池也取不到 → 自动切到 `PrintWindow(PW_RENDERFULLCONTENT)` 兼容通道（标题栏会显示「· 兼容模式」）。
- **悬浮窗**：WPF 无边框窗口 + `AllowsTransparency`（本身就是分层窗口），因此能把 `WS_EX_TRANSPARENT` 直接加上去做真正的跨进程鼠标穿透；整体透明度用 `Window.Opacity`。
- **为什么不直接用 DWM 缩略图**：`DwmRegisterThumbnail` 往分层窗口上投射并不可靠，而穿透/半透明都必须依赖分层窗口，所以改走「采集 + 自绘」这条路。
- **无边框窗口的缩放/拖动**：自己处理 `WM_NCHITTEST` 返回 `HTLEFT/HTTOP...`；标题栏拖动走 `DragMove()`（内部是 `WM_NCLBUTTONDOWN + HTCAPTION`，保留系统贴边/吸附）；画面内拖动则改 `TranslateTransform` 平移内容，并做越界钳制。
- **进程内互操作**：`RoGetActivationFactory` 取 `GraphicsCaptureItem` 的激活工厂 → QI 出 `IGraphicsCaptureItemInterop` → `CreateForWindow`；WinRT 对象到托管对象用 CsWinRT 的 `FromAbi`（会接管引用，不能再 `Release`）。

## 排查日志

程序会把关键步骤写到 `%LOCALAPPDATA%\Win-Mirro-Overlay\app.log`（`开始镜像 / WGC 启动 / 首帧 / QI 失败 / 切换通道 / 停止` 等）。

托盘菜单里的「打开日志文件夹」可以直接打开该目录；反馈问题时把这个 `app.log` 发出来即可。

## 已知限制

- **受保护内容**：DRM 视频（如 Netflix/部分播放器）、受保护进程的窗口会黑屏或被拒绝捕获，这是系统限制。
- **独占全屏游戏**：WGC 通常抓不到，建议游戏用「无边框窗口」模式。
- **最小化**：源窗口最小化时 DWM 停止渲染，画面会暂停（不会崩）。
- **其它虚拟桌面**：窗口列表会列出其它桌面上的窗口（标注「其他虚拟桌面」），但 Windows 一般不允许跨桌面抓取，选中后可能抓不到画面，需要先切到该桌面。
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
