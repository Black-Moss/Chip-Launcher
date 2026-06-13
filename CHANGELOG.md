# 更新日志

## [1.2.0] - 2025-xx-xx

### ✨ 新增功能

#### 🎨 皮肤系统（全新）

- **皮肤管理页** — 新增 [`SkinPage`](Views/SkinPage.axaml) 视图，支持本地皮肤浏览、启用/停用、删除、搜索与排序
- **皮肤下载页** — 新增 [`SkinDownloadPage`](Views/SkinDownloadPage.axaml) 视图，可从网站浏览和下载皮肤，支持网格/列表两种展示模式、分页加载、分类筛选、图文预览
- **皮肤同步到游戏** — 新增 [`SkinSyncService`](Services/SkinSyncService.cs)，自动将 `skins/` 下的所有皮肤复制到
  `BepInEx/plugins/CustomSprites/{皮肤名}/Body/`，启动游戏时自动同步
- **自动应用皮肤** — 启动游戏时写入 [`com.Bytechey.skinsync.cfg`](Services/SkinSyncService.cs:163) 的 `CurrentSkin`
  配置，游戏内自动穿戴
- **下载进度条** — 皮肤下载按钮点击后变为进度条显示下载状态
- **同步进度条** — 搜索/刷新皮肤时显示同步到游戏的进度覆盖层（实时显示当前皮肤名和百分比）
- **下载状态自动刷新** — 切换到下载页时自动刷新已下载状态（删除皮肤后重新下载不再被缓存阻塞）
- **SkinSync 模组检测** — 皮肤系统仅兼容 SkinSync 模组，未安装时弹出非持久性提示对话框，引导用户前往 [GitHub 下载](https://github.com/Bytechey/SkinSync/releases/latest)；每会话仅弹出一次

#### 🧩 模组管理优化

- **改进扫描顺序** — 模组文件夹优先扫描 `.dll.disabled` 再扫描 `.dll`，避免主模组被禁用时前置 DLL 被误识别为主模组

### 🔧 问题修复

- **StackOverflowException** — 修复 [`UpdateEnableButtonState()`](Views/SkinPage.axaml.cs:327) 与 [
  `RefreshAllStates()`](Views/SkinPage.axaml.cs:569) 之间的无限递归调用
- **皮肤页在 SukiUI 中不加载** — 将不可靠的 `Loaded` 事件替换为 [`OnAttachedToVisualTree`](Views/SkinPage.axaml.cs:71)
  生命周期方法
- **下载解压不完整** — 修订 [`DownloadAndInstallSkinAsync()`](Views/SkinDownloadPage.axaml.cs:422)，解压到临时目录后递归扫描所有
  `.png`，跳过缩略图复制到 `skins/{Id}/Body/`
- **删除模组未清理 `.disabled` 文件** — [`DeleteMod()`](Views/ModsPage.axaml.cs:780) 在删除目录前先显式删除
  `PluginFilePath`
- **皮肤按钮颜色不刷新** — [`RefreshAllStates()`](Views/SkinPage.axaml.cs:569) 强制置空 `ItemsSource` 再重新绑定，触发
  Avalonia 重建列表项容器使转换器重新求值

### ⚠️ 破坏性变更

- **移除皮肤切换确认对话框** — 删除 [`AppConfig.ConfirmSkinSwitch`](Services/AppConfig.cs) 属性及对应的 UI
  覆盖层，启用皮肤不再需要二次确认
- **按钮文本统一** — 皮肤列表操作按钮改为「使用」/「停用」，下载页已下载皮肤按钮改为「▶ 使用此皮肤」并点击跳转至管理页

### 📦 新增文件

| 文件                                                                     | 说明                                    |
|------------------------------------------------------------------------|---------------------------------------|
| [`Services/SkinSyncService.cs`](Services/SkinSyncService.cs)           | 皮肤同步服务（复制到 CustomSprites + 更新 CFG 配置） |
| [`Services/LocalSkinReader.cs`](Services/LocalSkinReader.cs)           | 本地皮肤扫描与读取                             |
| [`Services/SkinCache.cs`](Services/SkinCache.cs)                       | 皮肤缩略图缓存                               |
| [`Services/SkinsMetadataService.cs`](Services/SkinsMetadataService.cs) | 皮肤元数据持久化                              |
| [`Services/SkinWebsiteService.cs`](Services/SkinWebsiteService.cs)     | 皮肤网站数据抓取与解析                           |
| [`Models/SkinDownloadItem.cs`](Models/SkinDownloadItem.cs)             | 皮肤数据模型（含 INotifyPropertyChanged 支持）   |
| [`Models/SkinInfo.cs`](Models/SkinInfo.cs)                             | 皮肤信息模型                                |
| [`Views/SkinPage.axaml`](Views/SkinPage.axaml)                         | 皮肤管理页面                                |
| [`Views/SkinPage.axaml.cs`](Views/SkinPage.axaml.cs)                   | 皮肤管理页逻辑                               |
| [`Views/SkinDownloadPage.axaml`](Views/SkinDownloadPage.axaml)         | 皮肤下载页面                                |
| [`Views/SkinDownloadPage.axaml.cs`](Views/SkinDownloadPage.axaml.cs)   | 皮肤下载页逻辑                               |

### 📄 修改文件

| 文件                                                           | 变更                            |
|--------------------------------------------------------------|-------------------------------|
| [`Models/LauncherInfo.cs`](Models/LauncherInfo.cs:5)         | 版本号更新至 1.1.0                  |
| [`Views/ModsPage.axaml.cs`](Views/ModsPage.axaml.cs:118)     | 模组扫描顺序反转 + 删除模组清理 `.disabled` |
| [`Services/AppConfig.cs`](Services/AppConfig.cs)             | 移除 `ConfirmSkinSwitch` 属性     |
| [`Views/MainWindow.axaml.cs`](Views/MainWindow.axaml.cs:627) | 启动游戏前同步皮肤                     |
