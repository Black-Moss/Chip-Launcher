using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ChipLauncher.Models;
using ChipLauncher.Services;
using ChipLauncher.Views;
using SukiUI.Controls;
using SukiUI.Toasts;

namespace ChipLauncher.Views;

/// <summary>
///     主窗口 - 游戏启动器（SukiUI Flat 主题）
/// </summary>
public partial class MainWindow : SukiWindow
{
    private readonly IGameService _gameService;
    private int _currentTextIndex;
    private string[] _gameTexts = [];
    private DispatcherTimer? _textTimer;

    /// <summary>SukiUI Toast 管理器（全局可访问）</summary>
    public static ISukiToastManager ToastManager { get; } = new SukiToastManager();

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

        // SukiToastManager 不支持通知类型颜色，直接通过内容前缀区分

        // 全局通知事件 → SukiUI Toast
        AppNotification.OnShow += (message, type) =>
        {
            var toast = ToastManager.CreateToast();
            switch (type)
            {
                case NotificationType.Error:
                    toast = toast.WithTitle("错误");
                    break;
                case NotificationType.Warning:
                    toast = toast.WithTitle("警告");
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
        CheckBepInExStatus();
    }

    /// <inheritdoc />
    /// <summary>
    ///     窗口打开后覆盖 SukiWindow 内部设置的亚克力效果，确保完全不透明。
    ///     XAML 中已设置 TransparencyLevelHint="None"，但 SukiWindow.OnApplyTemplate
    ///     可能在加载过程中重新设置，因此在 OnOpened 中再次覆盖。
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TransparencyLevelHint = [WindowTransparencyLevel.None];

        // 确保 SukiSideMenu 完全初始化后再设置默认选中页
        var defaultPage = AppConfig.Instance.DefaultPage;
        SelectSideMenuItem(defaultPage);
    }

    /// <summary>根据配置选择 SukiSideMenu 默认项，先取消所有项选中</summary>
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
            "About" => SideMenuAbout,
            "Settings" => SideMenuSettings,
            _ => SideMenuNews
        };
        target.IsSelected = true;
    }

    // ===== 窗口级拖放安装模组 =====

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2"
    };

    private void OnWindowDragEnter(object? sender, DragEventArgs e)
    {
        if (!HasCompatibleFile(e)) return;
        e.DragEffects = DragDropEffects.Copy;

#pragma warning disable CS0618
        var files = e.Data.GetFiles()?.ToList();
#pragma warning restore CS0618
        if (files == null || files.Count == 0) return;

        if (files.Count == 1)
        {
            var fileName = Path.GetFileName(files[0].Path?.LocalPath);
            WindowDropFileName.Text = fileName != null ? $"📄 {fileName}" : "";
        }
        else
        {
            var firstFileName = Path.GetFileName(files[0].Path?.LocalPath);
            WindowDropFileName.Text = firstFileName != null
                ? $"📄 {firstFileName}  (+{files.Count - 1} 个文件)"
                : $"📄 共 {files.Count} 个文件";
        }

        WindowDropOverlay.IsVisible = true;
    }

    private void OnWindowDragLeave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowDropOverlay.IsVisible = false;
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        WindowDropOverlay.IsVisible = false;
        if (!HasCompatibleFile(e)) return;

#pragma warning disable CS0618
        var files = e.Data.GetFiles()?.ToList();
#pragma warning restore CS0618
        if (files == null || files.Count == 0) return;

        var paths = files
            .Select(f => f.Path?.LocalPath)
            .Where(p => p != null)
            .Cast<string>()
            .ToList();

        if (paths.Count == 0) return;

        SideMenuMods.IsSelected = true;
        await Task.Delay(100);

        if (SideMenuMods.PageContent is ModsPage modsPage)
            await modsPage.InstallFilesAsync(paths);
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
            var path = f.Path?.LocalPath;
            if (path == null) return false;
            var ext = Path.GetExtension(path);
            return AllowedExtensions.Contains(ext);
        });
    }

    // ===== 启动时后台任务 =====

    private static async Task PrefetchNewsAsync()
    {
        try
        {
            var newsService = new NewsService();
            _ = await newsService.GetNewsAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn($"预取资讯失败: {ex.Message}");
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
            Logger.Warn($"加载游戏文本失败: {ex.Message}");
        }
    }

    private void CheckBepInExStatus()
    {
        if (GameLocalization.IsBepInExInstalled())
        {
            Logger.Info("BepInEx 已安装");
            BepInExWarning.IsVisible = false;
        }
        else
        {
            Logger.Warn("BepInEx 未安装 — 模组管理功能不可用");
            BepInExWarning.IsVisible = true;
        }
    }

    private async void ShowCurrentText()
    {
        if (_gameTexts.Length == 0) return;

        if (GameInfoPanel.IsVisible)
            await FadeGameInfoAsync(1, 0, 300);

        var text = _gameTexts[_currentTextIndex % _gameTexts.Length];
        GameInfoPanel.ItemsSource = new[] { text };
        GameInfoPanel.IsVisible = true;

        await FadeGameInfoAsync(0, 1, 300);
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

    // ===== 游戏启动 =====

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

