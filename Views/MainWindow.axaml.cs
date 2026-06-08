using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
/// 主窗口 - 游戏启动器（Avalonia 版本）
/// </summary>
public partial class MainWindow : Window
{
    private readonly IGameService _gameService;
    private string[] _gameTexts = [];
    private int _currentTextIndex;
    private DispatcherTimer? _textTimer;

    public MainWindow()
    {
        InitializeComponent();
        Logger.Info("Chip Launcher 启动 (Avalonia)");

        _gameService = new GameService();

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

    /// <summary>标题栏拖拽处理：按下鼠标左键时开始窗口拖拽</summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>启动时后台预取资讯，结果缓存到 NewsService 中</summary>
    private static async Task PrefetchNewsAsync()
    {
        var newsService = new NewsService();
        var result = await newsService.GetNewsAsync("4576490");
        if (result != null)
            Logger.Info($"启动预取资讯成功: {result.Count} 条");
        else
            Logger.Warn("启动预取资讯失败，用户可在资讯页手动重试");
    }

    /// <summary>读取游戏本地化文件，启动定时轮播（默认 3 秒切换一条）</summary>
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

            // 显示第一条
            ShowCurrentText();

            // 启动轮播定时器
            StartTextRotation();

            Logger.Info($"已加载 {_gameTexts.Length} 条游戏文本，每 {AppConfig.Instance.TextRotationInterval} 秒轮播");
        }
        catch (Exception ex)
        {
            Logger.Warn($"加载游戏文本失败: {ex.Message}");
        }
    }

    /// <summary>在标题栏显示当前索引的文本</summary>
    private void ShowCurrentText()
    {
        if (_gameTexts.Length == 0) return;
        var text = _gameTexts[_currentTextIndex % _gameTexts.Length];
        GameInfoPanel.ItemsSource = new[] { text };
        GameInfoPanel.IsVisible = true;
    }

    /// <summary>启动/重启文本轮播定时器</summary>
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
