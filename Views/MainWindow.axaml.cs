using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChipLauncher.Services;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace ChipLauncher.Views;

public partial class MainWindow : SukiWindow
{
    // ===== 窗口级拖放安装模组 =====

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".zip", ".rar", ".7z"
    };

    private readonly GameService _gameService;
    private int _currentTextIndex;
    private string[] _gameTexts = [];
    private DispatcherTimer? _textTimer;
    private ISukiDialog? _bepInExDialog;
    private bool _isBepInExDialogActive;

    public MainWindow()
    {
        InitializeComponent();
        Logger.Info("Chip Launcher 启动");

        // 设置窗口图标
        var iconPath = Path.Combine(AppContext.BaseDirectory, "ChipLauncher.ico");
        if (File.Exists(iconPath))
            Icon = new WindowIcon(iconPath);

        _gameService = new GameService();

        // 绑定 Toast 主机
        ToastHost.Manager = ToastManager;

        // 绑定 Dialog 主机
        DialogHost.Manager = DialogManager;

        // 全局通知事件 → 日志 + SukiUI Toast
        AppNotification.OnShow += (message, type) =>
        {
            // Toast 内容同步输出到日志
            switch (type)
            {
                case NotificationType.Error:
                    Logger.Error(message);
                    break;
                case NotificationType.Warning:
                    Logger.Warn(message);
                    break;
                default:
                    Logger.Info(message);
                    break;
            }

            var toast = ToastManager.CreateToast();
            switch (type)
            {
                case NotificationType.Error:
                    toast = toast.WithTitle("错误");
                    break;
                case NotificationType.Warning:
                    toast = toast.WithTitle("警告");
                    break;
                case NotificationType.Info:
                case NotificationType.Success:
                    break;
                default:
                    toast = toast.WithTitle("提示");
                    break;
            }

            toast.WithContent(message)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Queue();
        };

        // 启动游戏按钮
        // BtnPlay 在 FooterContent 内部，SukiSideMenu 可能创建独立命名范围，使用 FindControl 获取
        var btnPlay = this.FindControl<Button>("BtnPlay");
        if (btnPlay != null)
            btnPlay.Click += (_, _) => LaunchGame();
        BtnPlayCompact.Click += (_, _) => LaunchGame();

        // 启动时后台任务
        _ = PrefetchNewsAsync();
        LoadGameLocalization();

        // 初始化拖放遮罩标题
        WindowDropOverlayTitle.Text = "释放以安装模组";
    }

    public static ISukiToastManager ToastManager { get; } = new SukiToastManager();

    public static ISukiDialogManager DialogManager { get; } = new SukiDialogManager();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TransparencyLevelHint = [WindowTransparencyLevel.None];

        // 确保 SukiSideMenu 完全初始化后再设置默认选中页
        var defaultPage = AppConfig.Instance.DefaultPage;
        SelectSideMenuItem(defaultPage);

        // 启动后检查更新（延迟让 UI 先渲染完成）
        if (AppConfig.Instance.AutoCheckUpdates)
            _ = CheckForUpdatesAsync();

        // 检查 BepInEx 状态（窗口就绪后弹窗）
        _ = CheckBepInExAsync();
    }

    private void SelectSideMenuItem(string page)
    {
        // 先取消所有侧边栏项的选中状态，防止 SukiSideMenu 默认选中第一项
        SideMenuMods.IsSelected = false;
        SideMenuNews.IsSelected = false;
        SideMenuSettings.IsSelected = false;
        SideMenuAbout.IsSelected = false;

        var target = page switch
        {
            "Mods" => SideMenuMods,
            "News" => SideMenuNews,
            "Settings" => SideMenuSettings,
            _ => SideMenuAbout
        };
        target.IsSelected = true;
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await UpdateService.CheckForUpdateAsync();
            if (result == null) return;

            var (version, url) = result.Value;

            // 延迟一点让对话框管理器就绪
            await Task.Delay(500);

            await DialogManager.CreateDialog()
                .WithTitle("发现新版本")
                .WithContent($"Chip Launcher v{version} 已发布，是否前往下载？")
                .WithActionButton("稍后再说", _ => { }, true)
                .WithActionButton("前往下载", _ =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("打开下载页失败", ex);
                    }
                }, true, "Flat", "Accent")
                .TryShowAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn("自动检查更新失败", ex);
        }
    }

    // ReSharper disable once UnusedMember.Local
    private void OnWindowDragEnter(object? _, DragEventArgs e)
    {
        if (!HasCompatibleFile(e)) return;
        // SukiUI Dialog 在 SukiWindow.Hosts 层（高于主 Grid），对话框可见时不显示拖放遮罩
        if (_isBepInExDialogActive) return;
        e.DragEffects = DragDropEffects.Copy;

#pragma warning disable CS0618
        var files = e.Data.GetFiles()?.ToList();
#pragma warning restore CS0618
        if (files == null || files.Count == 0) return;

        // 检查是否有 BepInEx 压缩包
        var isBepInEx = files.Any(f =>
        {
            var path = f.Path.LocalPath;
            return IsBepInExFileName(path);
        });

        if (files.Count == 1)
        {
            var fileName = Path.GetFileName(files[0].Path.LocalPath);
            WindowDropFileName.Text = $"{fileName}";
        }
        else
        {
            var firstFileName = Path.GetFileName(files[0].Path.LocalPath);
            WindowDropFileName.Text = $"{firstFileName}  (+{files.Count - 1} 个文件)";
        }

        WindowDropOverlayTitle.Text = isBepInEx
            ? "释放以安装 BepInEx"
            : "释放以安装模组";
        WindowDropOverlay.IsVisible = true;
    }

    // ReSharper disable once UnusedMember.Local
    // ReSharper disable once UnusedParameter.Local
    private void OnWindowDragLeave(object? _, RoutedEventArgs __)
    {
        WindowDropOverlay.IsVisible = false;
    }

    // ReSharper disable once UnusedMember.Local
    private async void OnWindowDrop(object? _, DragEventArgs e)
    {
        try
        {
            WindowDropOverlay.IsVisible = false;
            if (!HasCompatibleFile(e)) return;

#pragma warning disable CS0618
            var files = e.Data.GetFiles()?.ToList();
#pragma warning restore CS0618
            if (files == null || files.Count == 0) return;
            var paths = files
                .Select(f => f.Path.LocalPath)
                .Where(_ => true)
                .ToList();

            if (paths.Count == 0) return;

            // 扫描每个文件，按实际内容分流：BepInEx 安装包 vs 模组包
            string? bepInExPath = null;
            foreach (var path in paths.Where(path => IsArchiveExtension(Path.GetExtension(path))))
            {
                var type = await InspectArchiveTypeAsync(path);
                if (type != "bepinex") continue;
                bepInExPath = path;
                break;
            }

            if (bepInExPath != null)
            {
                await InstallBepInExFromFileAsync(bepInExPath);
                return;
            }

            SideMenuMods.IsSelected = true;
            await Task.Delay(100);
            if (SideMenuMods.PageContent is ModsPage modsPage)
                await modsPage.InstallFilesAsync(paths);
        }

        catch (Exception ex)
        {
            Logger.Error("拖放处理异常", ex);
        }
    }

    private static bool IsBepInExFileName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName)) return false;
        return fileName.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) &&
               (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsArchiveExtension(string ext) => ext switch
    {
        ".zip" or ".rar" or ".7z" => true,
        _ => false
    };

    private static bool IsValidArchive(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[8];
            var read = fs.Read(header);
            if (read < 4) return false;

            // ZIP: PK\x03\x04 / PK\x05\x06 / PK\x07\x08
            if (header[0] == 'P' && header[1] == 'K') return true;
            // 7z: 7z\xBC\xAF\x27\x1C
            if (header[0] == '7' && header[1] == 'z' && header[2] == 0xBC) return true;
            // RAR: Rar!\x1A\x07
            if (header[0] == 'R' && header[1] == 'a' && header[2] == 'r' && header[3] == '!') return true;
            // GZip: \x1F\x8B
            if (header[0] == 0x1F && header[1] == 0x8B) return true;
            // BZip2: BZ
            return header[0] == 'B' && header[1] == 'Z';
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> InspectArchiveTypeAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions());
                var hasBepInExDir = false;
                var hasWinHttpDll = false;
                var hasDoorstopVersion = false;
                var hasDllFile = false;

                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    var path = entry.Key?.Replace('\\', '/') ?? "";

                    if (path.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase))
                        hasBepInExDir = true;
                    else if (path.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase))
                        hasWinHttpDll = true;
                    else if (path.Equals(".doorstop_version", StringComparison.OrdinalIgnoreCase))
                        hasDoorstopVersion = true;
                    else if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        hasDllFile = true;
                }

                // BepInEx 安装包特征：包含 BepInEx/ 目录 / winhttp.dll / .doorstop_version
                if (hasBepInExDir
                    || hasWinHttpDll
                    || hasDoorstopVersion)
                    return "bepinex";

                // 包含 DLL 文件 → 模组包
                return hasDllFile
                    ? "mod"
                    : "unknown";
            }
            catch
            {
                return "unknown";
            }
        });
    }

    private static bool HasCompatibleFile(DragEventArgs e)
    {
#pragma warning disable CS0618
        if (!e.Data.Contains(DataFormats.Files)) return false;
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        if (files == null) return false;

        return files.Any(f =>
        {
            var path = f.Path.LocalPath;
            var ext = Path.GetExtension(path);
            if (!AllowedExtensions.Contains(ext)) return false;
            // 压缩包扩展名需要额外验证魔数，防止非存档文件误触发拖放遮罩
            return !IsArchiveExtension(ext) || IsValidArchive(path);
        });
    }

    private static async Task PrefetchNewsAsync()
    {
        try
        {
            var newsService = new NewsService();
            _ = await newsService.GetNewsAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn("预取资讯失败", ex);
        }
    }

    private void LoadGameLocalization()
    {
        try
        {
            _gameTexts = GameLocalization.GetDisplayTexts().ToArray();
            if (_gameTexts.Length == 0) return;

            ShowCurrentText();
            StartTextRotation();
        }
        catch (Exception ex)
        {
            Logger.Warn("加载游戏文本失败", ex);
        }
    }

    private void OnBepInExWarningClick(object? _, RoutedEventArgs e)
    {
        var __ = CheckBepInExAsync();
    }

    private async Task CheckBepInExAsync()
    {
        if (GameLocalization.IsBepInExInstalled())
        {
            Logger.Info("BepInEx 已安装");
            BepInExWarning.IsVisible = false;
            return;
        }

        Logger.Warn("BepInEx 未安装 — 模组管理功能不可用");
        BepInExWarning.IsVisible = true;
        WindowDropOverlay.IsVisible = false;

        // 使用原生 SukiUI Dialog，从按钮回调中捕获 ISukiDialog 引用以便安装后关闭
        _isBepInExDialogActive = true;
        await DialogManager.CreateDialog()
            .WithTitle("BepInEx 未安装")
            .WithContent("BepInEx 是游戏模组加载框架，未安装时模组管理功能不可用。\n\n" +
                         "你可以：\n" +
                         "① 点击「前往下载」从 GitHub 获取 BepInEx\n" +
                         "② 点击「选择文件」选择本地 BepInEx 压缩包\n" +
                         "③ 或将 BepInEx 压缩包直接拖入窗口自动安装")
            .WithActionButton("关闭", dialog => _bepInExDialog = dialog, true)
            .WithActionButton("选择文件", dialog =>
            {
                _bepInExDialog = dialog;
                _ = PickAndInstallBepInExAsync();
            }, true, "Flat", "Accent")
            .WithActionButton("前往下载", dialog =>
            {
                _bepInExDialog = dialog;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/BepInEx/BepInEx/releases/latest",
                    UseShellExecute = true
                });
            }, true, "Flat", "Accent")
            .TryShowAsync();
        _isBepInExDialogActive = false;
    }

    private async Task PickAndInstallBepInExAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 BepInEx 压缩包",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("BepInEx 压缩包")
                    {
                        Patterns = ["*.zip", "*.7z", "*.rar"]
                    }
                ]
            });

            if (files.Count == 0) return;

            await InstallBepInExFromFileAsync(files[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            Logger.Error("安装 BepInEx 失败", ex);
            await DialogManager.CreateDialog()
                .WithTitle("安装失败")
                .WithContent($"安装 BepInEx 时出错：{ex.Message}")
                .WithActionButton("知道了", _ => { }, true)
                .TryShowAsync();
        }
    }

    private async Task InstallBepInExFromFileAsync(string filePath)
    {
        WindowDropOverlay.IsVisible = false;

        // 检查是否为有效的 BepInEx 压缩包（需包含 .doorstop_version + BepInEx/ + winhttp.dll）
        bool isBepInEx;
        try
        {
            await using var stream = File.OpenRead(filePath);
            using var archive = ArchiveFactory.OpenArchive(stream,
                new ReaderOptions());
            var entries = archive.Entries.Select(e => e.Key?.Replace('\\', '/') ?? "").ToHashSet();
            isBepInEx = entries.Any(k => k.Equals(".doorstop_version", StringComparison.OrdinalIgnoreCase)) &&
                        entries.Any(k => k.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) &&
                        entries.Any(k => k.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            isBepInEx = false;
        }

        if (!isBepInEx)
        {
            await DialogManager.CreateDialog()
                .WithTitle("文件格式错误")
                .WithContent("所选文件不是有效的 BepInEx 压缩包（未找到 .doorstop_version、BepInEx/ 目录或 winhttp.dll）。\n\n" +
                             "请从 GitHub 下载正确的 BepInEx 版本。")
                .WithActionButton("知道了", _ => { }, true)
                .TryShowAsync();
            return;
        }

        // 解压到游戏目录
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir))
        {
            await DialogManager.CreateDialog()
                .WithTitle("无法安装")
                .WithContent("未找到游戏安装目录，请先在设置中配置游戏路径。")
                .WithActionButton("知道了", _ => { }, true)
                .TryShowAsync();
            return;
        }

        // 后台解压
        await Task.Run(() =>
        {
            using var stream = File.OpenRead(filePath);
            using var archive = ArchiveFactory.OpenArchive(stream,
                new ReaderOptions());
            var archiveDir = archive.Entries.First().Key?.Replace('\\', '/') ?? "";
            var baseDir = archiveDir.Contains('/')
                ? archiveDir[..archiveDir.IndexOf('/')]
                : "";

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var relPath = entry.Key?.Replace('\\', '/') ?? "";
                if (!string.IsNullOrEmpty(baseDir)
                    && relPath.StartsWith(baseDir + "/"))
                    relPath = relPath[(baseDir.Length + 1)..];

                var destPath = Path.Combine(gameDir, relPath);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                entry.WriteToFile(destPath, new ExtractionOptions
                    { ExtractFullPath = true, Overwrite = true });
            }
        });

        // 重新检查状态
        if (GameLocalization.IsBepInExInstalled())
        {
            // 关闭 BepInEx 未安装提示对话框（已通过按钮回调捕获引用）
            if (_bepInExDialog != null)
            {
                DialogManager.TryDismissDialog(_bepInExDialog);
                _bepInExDialog = null;
            }

            BepInExWarning.IsVisible = false;
            AppNotification.Show("BepInEx 安装成功！", NotificationType.Success);
        }
        else
        {
            AppNotification.Show("BepInEx 安装失败，请检查文件是否正确", NotificationType.Error);
        }
    }

    private async void ShowCurrentText()
    {
        try
        {
            if (_gameTexts.Length == 0) return;

            if (GameInfoPanel.IsVisible)
                await FadeGameInfoAsync(1, 0, 300);

            var text = _gameTexts[_currentTextIndex % _gameTexts.Length];
            GameInfoPanel.ItemsSource = new[] { text };
            GameInfoPanel.IsVisible = true;

            await FadeGameInfoAsync(0, 1, 300);
        }
        catch (Exception ex)
        {
            Logger.Error("切换游戏文本显示异常", ex);
        }
    }

    private async Task FadeGameInfoAsync(double from, double to, int durationMs)
    {
        const int steps = 15;
        for (var i = 0; i <= steps; i++)
        {
            GameInfoPanel.Opacity = from + (to - from) * i / steps;
            await Task.Delay(durationMs / steps);
        }
    }

    private void StartTextRotation()
    {
        _textTimer?.Stop();
        _textTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AppConfig.Instance.TextRotationInterval)
        };
        _textTimer.Tick += (_, _) =>
        {
            _currentTextIndex = (_currentTextIndex + 1) % _gameTexts.Length;
            ShowCurrentText();
        };
        _textTimer.Start();
    }

    private void LaunchGame()
    {
        var config = AppConfig.Instance;
        if (!string.IsNullOrEmpty(config.GamePath))
        {
            Logger.Info($"启动游戏（本地路径）: {config.GamePath}");
            _gameService.LaunchDirectly(config.GamePath);
        }
        else
        {
            Logger.Info("启动游戏（Steam）");
            _gameService.LaunchViaSteam();
        }
    }
}