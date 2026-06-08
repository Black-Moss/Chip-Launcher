using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ChipLauncher.Models;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
///     主窗口 - 游戏启动器（纯黑圆角风格）
/// </summary>
public partial class MainWindow : Window
{
    private const int WindowCornerRadius = 10;

    private readonly IGameService _gameService;
    private string? _currentNav; // 当前导航页面标识
    private int _currentTextIndex;
    private string[] _gameTexts = [];
    private DispatcherTimer? _textTimer;

    // ── Toast 通知系统 ─────────────────────────────────────────
    private readonly ObservableCollection<ToastItem> _toasts = [];

    public MainWindow()
    {
        InitializeComponent();
        Logger.Info("Chip Launcher 启动");

        // 设置窗口图标
        var iconPath = Path.Combine(AppContext.BaseDirectory, "ChipLauncher.ico");
        if (File.Exists(iconPath))
            Icon = new WindowIcon(iconPath);

        _gameService = new GameService();

        // 窗口显示后应用 Win32 圆角区域
        Opened += (_, _) => ApplyRoundCorners();

        // 窗口尺寸变化 / 状态变化时重新应用圆角区域
        Resized += OnWindowResized;
        PropertyChanged += OnWindowPropertyChanged;

        // 启动时后台预取资讯 + 加载游戏文本 + 检查 BepInEx
        _ = PrefetchNewsAsync();
        LoadGameLocalization();
        CheckBepInExStatus();

        // Toast 容器绑定
        ToastContainer.ItemsSource = _toasts;

        // 全局通知事件 — 新消息插入到顶部，旧消息自动下移
        AppNotification.OnShow += (message, type) =>
        {
            var toast = new ToastItem(message, type);
            _toasts.Insert(0, toast);
            toast.StartFadeOut(() => { Dispatcher.UIThread.Post(() => _toasts.Remove(toast)); });
        };

        BtnPlay.PointerPressed += (_, _) => LaunchGame();
        BtnMods.PointerPressed += (_, _) => NavigateTo("Mods", () => new ModsPage());
        BtnNews.PointerPressed += (_, _) => NavigateTo("News", () => new NewsPage());
        BtnSettings.PointerPressed += (_, _) => NavigateTo("Settings", () => new SettingsPage());
        BtnAbout.PointerPressed += (_, _) => NavigateTo("About", () => new AboutPage());

        // 根据配置显示默认页面
        var defaultPage = AppConfig.Instance.DefaultPage;
        ContentFrame.Content = defaultPage switch
        {
            "Mods" => new ModsPage(),
            "About" => new AboutPage(),
            "Settings" => new SettingsPage(),
            _ => new NewsPage()
        };
        HighlightNav(defaultPage);

        // 窗口控制按钮
        BtnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        BtnMaximize.Click += (_, _) =>
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        };
        BtnClose.Click += (_, _) => Close();

        // 标题栏拖拽
        TitleBar.PointerPressed += OnTitleBarPointerPressed;
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

        var files = e.Data.GetFiles()?.ToList();
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

        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0) return;

        // 收集所有文件路径
        var paths = files
            .Select(f => f.Path?.LocalPath)
            .Where(p => p != null)
            .Cast<string>()
            .ToList();

        if (paths.Count == 0) return;

        // 切换到模组页面（如果尚未在模组页）
        NavigateTo("Mods", () => new ModsPage());

        // 等待页面渲染完成
        await Task.Delay(100);

        // 一次性批量安装，LoadMods 只调用一次
        if (ContentFrame.Content is ModsPage modsPage)
            await modsPage.InstallFilesAsync(paths);
    }

    /// <summary>检查拖放数据中是否包含兼容的文件</summary>
    private static bool HasCompatibleFile(DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return false;
        var files = e.Data.GetFiles();
        if (files == null) return false;

        return files.Any(f =>
        {
            var path = f.Path?.LocalPath;
            if (path == null) return false;
            var ext = Path.GetExtension(path);
            return AllowedExtensions.Contains(ext);
        });
    }

    // ===== Win32 圆角窗口实现 =====

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    /// <summary>
    ///     应用或移除圆角窗口区域。
    ///     最大化时移除区域（窗口应为矩形全屏），普通/最小化时应用圆角。
    /// </summary>
    private void ApplyRoundCorners()
    {
        var handle = TryGetPlatformHandle()?.Handle;
        if (handle == null || handle.Value == IntPtr.Zero)
            return;

        var hwnd = handle.Value;

        if (WindowState == WindowState.Maximized)
        {
            // 最大化时：移除自定义区域，让窗口恢复矩形全屏
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        var width = (int)ClientSize.Width;
        var height = (int)ClientSize.Height;
        if (width <= 0 || height <= 0)
            return;

        var hRgn = CreateRoundRectRgn(0, 0, width, height, WindowCornerRadius, WindowCornerRadius);
        if (hRgn != IntPtr.Zero)
            // SetWindowRgn 接管了 HRGN 所有权，不需要手动 DeleteObject
            SetWindowRgn(hwnd, hRgn, true);
    }

    /// <summary>窗口大小变化时重新应用圆角区域</summary>
    private void OnWindowResized(object? sender, EventArgs e)
    {
        ApplyRoundCorners();
    }

    /// <summary>窗口状态变化（最大化/还原）时重新应用圆角区域</summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty) ApplyRoundCorners();
    }

    // ===== 标题栏拖拽 =====

    /// <summary>标题栏拖拽处理</summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }

    // ===== 资讯预取 =====

    /// <summary>启动时后台预取资讯</summary>
    private static async Task PrefetchNewsAsync()
    {
        var newsService = new NewsService();
        var result = await newsService.GetNewsAsync();
        if (result != null)
            Logger.Info($"启动预取资讯成功: {result.Count} 条");
        else
            Logger.Warn("启动预取资讯失败，用户可在资讯页手动重试");
    }

    // ===== 游戏文本轮播 =====

    /// <summary>读取游戏本地化文件，启动定时轮播</summary>
    private void LoadGameLocalization()
    {
        try
        {
            var texts = GameLocalization.GetDisplayTexts();
            if (texts.Count == 0)
            {
                Logger.Info("未找到游戏本地化文本（游戏可能未安装）");
                return;
            }

            _gameTexts = [.. texts];
            _currentTextIndex = 0;

            ShowCurrentText();
            StartTextRotation();

            Logger.Info($"已加载 {_gameTexts.Length} 条游戏文本，每 {AppConfig.Instance.TextRotationInterval} 秒轮播");
        }
        catch (Exception ex)
        {
            Logger.Warn($"加载游戏文本失败: {ex.Message}");
        }
    }

    /// <summary>启动时检查游戏是否安装了 BepInEx，未安装则在标题栏显示警告</summary>
    private void CheckBepInExStatus()
    {
        var installed = GameLocalization.IsBepInExInstalled();
        if (installed)
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

    /// <summary>显示当前文本，带淡入/淡出过渡（300ms）</summary>
    private async void ShowCurrentText()
    {
        if (_gameTexts.Length == 0) return;

        // 如果有旧文本正在显示，先淡出
        if (GameInfoPanel.IsVisible)
            await FadeGameInfoAsync(1, 0, 300);

        var text = _gameTexts[_currentTextIndex % _gameTexts.Length];
        GameInfoPanel.ItemsSource = new[] { text };
        GameInfoPanel.IsVisible = true;

        // 淡入新文本
        await FadeGameInfoAsync(0, 1, 300);
    }

    /// <summary>GameInfoPanel 透明度渐变</summary>
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

    // ===== 导航逻辑 =====

    /// <summary>导航到指定页面，如果已在则跳过</summary>
    private void NavigateTo(string page, Func<Control> createPage)
    {
        if (_currentNav == page)
        {
            Logger.Info($"已在 {page} 页面，跳过导航");
            return;
        }

        Logger.Info($"导航到: {page}");
        _currentNav = page;
        ContentFrame.Content = createPage();
        HighlightNav(page);
    }

    // ── 导航栏统一样式 ──────────────────────────────────────────

    private static readonly Brush NavHoverBg = new SolidColorBrush(Color.FromRgb(42, 42, 42)); // 鼠标悬浮：灰背景
    private static readonly Brush NavSelectedBg = new SolidColorBrush(Color.FromRgb(51, 51, 51)); // 选中：淡灰背景
    private static readonly Brush NavNormalBg = new SolidColorBrush(Colors.Transparent); // 普通：透明背景
    private static readonly Brush NavSelectedText = new SolidColorBrush(Colors.White);
    private static readonly Brush NavNormalText = new SolidColorBrush(Color.FromRgb(204, 204, 204));

    /// <summary>导航栏鼠标进入 → 灰色背景</summary>
    private void NavItemPointerEnter(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
            border.Background = NavHoverBg;
    }

    /// <summary>导航栏鼠标离开 → 恢复（选中项保持淡灰）</summary>
    private void NavItemPointerLeave(object? sender, PointerEventArgs e)
    {
        if (sender is not Border border) return;

        var isSelected = (border == BtnMods && _currentNav == "Mods")
                         || (border == BtnNews && _currentNav == "News")
                         || (border == BtnSettings && _currentNav == "Settings")
                         || (border == BtnAbout && _currentNav == "About");

        border.Background = isSelected ? NavSelectedBg : NavNormalBg;
    }

    /// <summary>更新导航栏高亮状态</summary>
    private void HighlightNav(string? page)
    {
        // 所有按钮恢复默认样式
        ResetNavStyle(BtnPlay);
        ResetNavStyle(BtnMods);
        ResetNavStyle(BtnNews);
        ResetNavStyle(BtnSettings);
        ResetNavStyle(BtnAbout);

        // 高亮当前页面按钮
        var target = page switch
        {
            "Mods" => BtnMods,
            "News" => BtnNews,
            "Settings" => BtnSettings,
            "About" => BtnAbout,
            _ => null
        };

        if (target == null) return;
        target.Background = NavSelectedBg;
        if (target.Child is TextBlock tb)
            tb.Foreground = NavSelectedText;
    }

    /// <summary>将导航按钮重置为默认样式</summary>
    private static void ResetNavStyle(Border border)
    {
        border.Background = NavNormalBg;
        if (border.Child is TextBlock tb)
            tb.Foreground = NavNormalText;
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

/// <summary>Toast 通知项 — 包含消息、颜色、渐隐计时</summary>
public class ToastItem : INotifyPropertyChanged
{
    private double _opacity = 1;

    public Guid Id { get; } = Guid.NewGuid();
    public string Message { get; }
    public Brush TextColor { get; }
    public Brush BackgroundColor { get; }

    public double Opacity
    {
        get => _opacity;
        set
        {
            _opacity = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
        }
    }

    public ToastItem(string message, NotificationType type)
    {
        Message = message;
        var (textHex, bgHex) = type switch
        {
            NotificationType.Success => ("#4CAF50", "#cc1a3a1a"),
            NotificationType.Warning => ("#e67e22", "#cc3d1a00"),
            NotificationType.Error => ("#e74c3c", "#cc3d0000"),
            _ => ("#ffffff", "#cc000000")
        };
        TextColor = new SolidColorBrush(Color.Parse(textHex));
        BackgroundColor = new SolidColorBrush(Color.Parse(bgHex));
    }

    /// <summary>3 秒后渐变消失（300ms 内 Opacity→0），完成后回调</summary>
    public async void StartFadeOut(Action onComplete)
    {
        await Task.Delay(2800); // 停留 2.8 秒

        // 300ms 渐变消失（每 30ms 降 0.1）
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(30);
            Opacity = 1.0 - (i + 1) * 0.1;
        }

        onComplete();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}