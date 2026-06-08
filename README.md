# Chip Launcher

> 教人装模组教疾苦搞出来的神人玩意

《未知伤亡》（Casualties Unknown）的模组管理器。基于 Avalonia UI 构建，支持模组的安装、启用/禁用、配置编辑、批量操作等功能。

## 功能

### 📦 模组管理

- 自动扫描 `BepInEx/plugins` 目录子文件夹，识别已安装模组
- 智能读取 BepInEx 插件的 `[BepInPlugin]` 特性元数据（GUID、名称、版本）
- 安装模组（支持 `.dll` 文件和 `.zip`/`.7z`/`.rar` 等压缩包）
- 拖放安装：直接拖拽文件到窗口即可安装
- 启用/禁用模组（通过重命名 `.disabled` 后缀）
- 删除模组（含确认弹窗）
- 模组搜索与排序

### ⚙️ BepInEx 配置编辑

- 可视化编辑 `BepInEx/config` 下的 `.cfg` 配置文件
- 支持修改配置项的值
- 一键重置配置为默认值
- 直接在文件管理器中打开配置文件夹

### 🔄 批量操作

- ✅ 多选模式（点击复选框或按住 Shift 范围选择）
- 批量启用、批量禁用
- 批量删除（含确认弹窗）
- 反选
- 全选/全不选

### 🖥️ 启动页管理

- 自定义启动页面（新闻、模组管理、设置、关于）
- 游戏信息轮播显示（从游戏本地化文件中提取文本）

### 📰 新闻资讯

- 从 Steam RSS 拉取游戏新闻
- 离线缓存
- 新闻搜索

### 🔔 自动更新检测

- 启动时自动检查 GitHub Releases 新版本
- 可在设置中关闭自动检测
- 手动检测更新

### 🎮 游戏启动

- 一键启动游戏
- 自动检测 Steam 游戏路径
- 检测 BepInEx 是否已安装

## 技术栈

| 技术                                                             | 版本       |
|----------------------------------------------------------------|----------|
| [Avalonia](https://avaloniaui.net/)                            | 11.3.14  |
| [SukiUI](https://github.com/kikipoulet/SukiUI)                 | 6.1.1    |
| .NET                                                           | 8.0      |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | 0.49.1   |
| System.Reflection.Metadata                                     | PEReader |

## 构建与发布

### 前置要求

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 调试构建

```bash
dotnet build
```

### 发布（单文件）

```bash
dotnet publish -c Release -p:PublishDir=publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:SelfContained=true -p:RuntimeIdentifier=win-x64
```

发布产物位于 `publish/ChipLauncher.exe`（约 95 MB，单个 exe 文件，双击即用）。

也可使用 Rider 内置的 **Publish** 运行配置（已配置好）。

### 版本号

版本号在 [`Models/LauncherInfo.cs`](Models/LauncherInfo.cs:5) 中定义，发布新版本前请手动更新。

## 项目结构

```
ChipLauncher/
├── Models/                 # 数据模型
│   ├── GameInfo.cs
│   ├── LauncherInfo.cs     # 启动器版本号
│   ├── ModInfo.cs          # 模组信息（实现 INotifyPropertyChanged）
│   └── NewsItem.cs
├── Services/               # 业务逻辑服务
│   ├── AppConfig.cs        # 应用配置（自动保存 JSON）
│   ├── AppNotification.cs  # Toast 通知
│   ├── BepInExConfig.cs    # BepInEx .cfg 配置文件解析
│   ├── GameLocalization.cs # 游戏本地化文本提取
│   ├── GameService.cs      # 游戏启动服务
│   ├── Logger.cs           # 文件日志
│   ├── ModInstaller.cs     # 模组安装器（含 BepInPlugin 元数据读取）
│   ├── NewsService.cs      # 新闻 RSS 抓取与缓存
│   └── UpdateService.cs    # GitHub 自动更新检测
├── Views/                  # 页面视图
│   ├── AboutPage.axaml     # 关于页面
│   ├── MainWindow.axaml    # 主窗口（含侧边栏导航）
│   ├── ModsPage.axaml      # 模组管理页面
│   ├── NewsPage.axaml      # 新闻页面
│   └── SettingsPage.axaml  # 设置页面
├── App.axaml               # 应用入口
├── Program.cs              # 启动代码
└── ChipLauncher.csproj     # 项目文件
```

## 链接

- [GitHub](https://github.com/Black-Moss/Chip-Launcher)
- [QQ 群](https://qm.qq.com/q/z0ow84QXde)
- [Steam 商店页 - 未知伤亡](https://store.steampowered.com/app/4576490/_/)

## 许可

MIT
