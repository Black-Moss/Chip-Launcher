using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
/// 主窗口 - 游戏启动器（纯黑圆角风格）
/// </summary>
public partial class MainWindow : Window
{
    private const int WindowCornerRadius = 10;

    private readonly IGameService _gameService;
    private string[] _gameTexts = [];
    private int _currentTextIndex;
    private DispatcherTimer? _textTimer;

    public MainWindow()
    {
        InitializeComponent();
        Logger.Info("Chip Launcher 启动");

        _gameService = new GameService();

        // 窗口显示后应用 Win32 圆角区域
        Opened += (_, _) => ApplyRoundCorners();

        // 窗口尺寸变化 / 状态变化时重新应用圆角区域
        Resized += OnWindowResized;
        PropertyChanged += OnWindowPropertyChanged;

        // 启动时后台预取资讯 + 加载游戏文本
        _ = PrefetchNewsAsync();
        LoadGameLocalization();

        // 导航按钮事件
        BtnPlay.Click += (_, _) => LaunchGame();
        BtnMods.Click += (_, _) =>
        {
            Logger.Info("导航到: 模组管理");
            ContentFrame.Content = new ModsPage();
        };
        BtnNews.Click += (_, _) =>
        {
            Logger.Info("导航到: 游戏资讯");
            ContentFrame.Content = new NewsPage();
        };
        BtnSettings.Click += (_, _) =>
        {
            Logger.Info("导航到: 设置");
            ContentFrame.Content = new SettingsPage();
        };

        // 默认显示资讯页
        ContentFrame.Content = new NewsPage();

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

    // ===== Win32 圆角窗口实现 =====

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    /// <summary>
    /// 应用或移除圆角窗口区域。
    /// 最大化时移除区域（窗口应为矩形全屏），普通/最小化时应用圆角。
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
        {
            // SetWindowRgn 接管了 HRGN 所有权，不需要手动 DeleteObject
            SetWindowRgn(hwnd, hRgn, true);
        }
    }

    /// <summary>窗口大小变化时重新应用圆角区域</summary>
    private void OnWindowResized(object? sender, EventArgs e)
    {
        ApplyRoundCorners();
    }

    /// <summary>窗口状态变化（最大化/还原）时重新应用圆角区域</summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            ApplyRoundCorners();
        }
    }

    // ===== 标题栏拖拽 =====

    /// <summary>标题栏拖拽处理</summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    // ===== 资讯预取 =====

    /// <summary>启动时后台预取资讯</summary>
    private static async Task PrefetchNewsAsync()
    {
        var newsService = new NewsService();
        var result = await newsService.GetNewsAsync("4576490");
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

    private void ShowCurrentText()
    {
        if (_gameTexts.Length == 0) return;
        var text = _gameTexts[_currentTextIndex % _gameTexts.Length];
        GameInfoPanel.ItemsSource = new[] { text };
        GameInfoPanel.IsVisible = true;
    }

    private void StartTextRotation()
    {
        _textTimer?.Stop();
        _textTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AppConfig.Instance.TextRotationInterval),
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
