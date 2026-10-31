# PlainSpaceflightLauncher (PSL)

Spaceflight Simulator 的第三方启动器。界面风格参考自 [Plain Craft Launcher 2 (PCL2)](https://github.com/Meloong-Git/PCL)，
底层为完全独立实现（C# / WPF / .NET Framework 4.8，与 PCL 同一运行时），不含 PCL 的源代码。

> 本项目的界面设计参考了龙腾猫跃开发的 Plain Craft Launcher 2，在此致谢。
> PSL 与 PCL 官方、Spaceflight Simulator 开发商均无隶属或合作关系。

## 功能

| 模块 | 说明 |
| --- | --- |
| 版本选择 | **左栏选游戏文件夹、右栏选版本**。游戏目录（`.sfs`）根目录本身即一个文件夹，也可以用「添加大版本文件夹」把别的父目录加进左栏（只登记路径、不动文件）；每个文件夹下「根目录含 `Spaceflight Simulator.exe`」的直接子文件夹即一个版本。点右栏的版本即选中并返回。从主页左栏的「版本选择」按钮进入 |
| 启动游戏 | 直接启动版本目录下的 `Spaceflight Simulator.exe` |
| 模组 | DLL 模组管理（`Mods\<模组名>\`），支持拖动 `.dll` 进窗口一键安装、启用 / 禁用、卸载 |
| 涂装包 | `Mods\Custom_Assets\Texture Packs\` |
| 零件包 | `Mods\Custom_Assets\Parts\`（`.pack`） |
| 星系包 | `Spaceflight Simulator_Data\Custom Solar Systems\` |
| 蓝图 | `Saving\Blueprints\` |
| 下载 | 数据源可在 **SFS 汉化模组站**（sfszhmod.pages.dev）与 **SFS 中文模组站**（sfscnmod.7813.cc.cd）之间切换，两者数据结构一致；提供 DLL 模组 / 涂装包 / 零件包 / 蓝图，有直链时多线程分段下载，没有直链时用浏览器打开蓝奏云。游戏本体下载为 WIP 占位 |
| 设置 | 游戏目录目录、下载线程数、下载方式、数据源、汉化版功能开关、关于、日志 |
| 友站 | 顶栏「友站」内置浏览器，可切换 SFS 工具站 / 涂装包制作器 / 星系包生成器 / 涂装包 / 整合包编辑器 |

### 与 PCL2 的差异

按要求移除 / 调整的部分：

- 去掉所有与 Minecraft 强绑定的功能（Java 版本、Forge/Fabric、正版登录、皮肤等）
- `.minecraft` → `.sfs`
- 版本定位：不再使用 `versions\<版本>` 结构，直接在游戏目录根目录下识别版本文件夹
- 账号登录：UI 入口隐藏
- 帮助页：WIP
- 下载页的「游戏本体下载」：WIP
- 可执行文件不带自定义图标

## 系统要求

- Windows 10 / 11（自带 .NET Framework 4.8，**无需另装运行时**）
- WebView2 运行时（仅「友站」页的内置浏览器需要，Win10/11 一般已自带）
- Spaceflight Simulator **1.6 及以上**版本（DLL 模组仅 PC / Steam 版可用）

## 构建

```bat
build.bat
```

或手动：

```bat
dotnet build PSL\PSL.csproj -c Release
```

产物：`PSL\bin\Release\net48\PlainSpaceflightLauncher.exe`（约 2 MB，**单文件**）

托管依赖由 Costura 嵌入、WebView2 原生加载器作为嵌入资源在启动时释放到 `PSL\` 数据目录，
所以构建产物目录里只有这一个 exe，直接分发即可。

## 目录结构

```
PlainSpaceflightLauncher/
├─ LICENSE                  GPL-3.0
├─ build.bat
├─ global.json
└─ PSL/
   ├─ App.xaml(.cs)         应用入口
   ├─ MainWindow.xaml(.cs)  自绘标题栏 + 导航 + 拖拽安装
   ├─ Core/                 路径、配置、版本、资源、下载、数据源
   ├─ Controls/             对话框（选择、进度）
   ├─ Themes/               色板、矢量图标、控件样式
   └─ Pages/                主页 / 下载 / 设置 / 帮助（+ 版本选择 / 版本设置两个副页面）
```

主题文件分工：

| 文件 | 内容 |
| --- | --- |
| `Themes/Colors.xaml` | 色板（沿用 PCL 界面准则的角色色板 ColorBrush1–8 / ColorObject1–8、灰阶、红阶） |
| `Themes/PclIcons.xaml` | 全部矢量图标（Geometry），取自 PCL 界面所用的图标资源 |
| `Themes/Controls.xaml` | 控件样式：左栏列表项、卡片、按钮、文本框、复选框、下拉框、滚动条 |

运行时数据：

```
<程序目录>/
├─ PSL/
│  ├─ config.json           配置
│  ├─ Log/                  日志
│  └─ Cache/                数据缓存与下载临时文件
└─ .sfs/                    默认游戏目录目录（可在设置里更改）
```

## 开源协议

本项目采用 [GPL-3.0-only](LICENSE)：允许商用，但任何基于本项目的修改与衍生作品都必须以相同协议开源，并保留版权声明。

### 关于 PCL 的授权说明

PCL2 未以常规开源协议发布，其授权为《PCL 分发有限许可》。该许可把使用分为两类：

- **轻度使用**：参考了较大量的内容，但软件与 PCL 的实质功能**不存在任何重叠** → 需署名，且不得与 PCL 混淆。
- **重度使用**：基于 PCL 仓库修改，或参考较大量内容重新实现 PCL 的部分实质功能（例如启动或下载 Java 版 Minecraft、下载或管理 Mod）→ 额外的名称、Minecraft 劝导等要求。

PSL 的功能是 Spaceflight Simulator 的启动与资源管理，**不涉及 Minecraft 的启动、Java 下载或 Mod 管理**，与 PCL 的实质功能无重叠，因此按**轻度使用**处理，并已完成：

1. **署名**：关于页「鸣谢」栏首条给出龙腾猫跃与 PCL2 的署名及仓库链接，README 同样标注。
2. **不混淆**：项目名为 `PlainSpaceflightLauncher (PSL)`，界面与文案中不使用 `PCL` 字样，也不使用 PCL 的标志图形。

此外，界面主色相保持蓝色、卡片／按钮／列表的视觉参数沿用 PCL 的界面准则，以保证风格一致。

> 若你认为本项目的使用方式超出了《PCL 分发有限许可》的轻度使用范围，欢迎提 issue 沟通调整。
