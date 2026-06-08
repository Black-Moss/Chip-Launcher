using System.Windows;
using ChipLauncher.Services;
using ChipLauncher.Views;

namespace ChipLauncher;

/// <summary>
/// 主窗口 - 游戏启动器
/// </summary>
public partial class MainWindow
{
    private readonly IGameService _gameService;

    public MainWindow()
    {
        InitializeComponent();
        Logger.Info("Chip Launcher 启动");

        _gameService = new GameService();

        // 启动时后台预取资讯（失败也不阻塞，用户可在资讯页手动重试）
        _ = PrefetchNewsAsync();

        BtnPlay.Click += (_, _) => LaunchGame();
        BtnMods.Click += (_, _) =>
        {
            Logger.Info("导航到: 模组管理");
            ContentFrame.Navigate(new ModsPage());
        };
        BtnNews.Click += (_, _) =>
        {
            Logger.Info("导航到: 游戏资讯");
            ContentFrame.Navigate(new NewsPage());
        };
        BtnSettings.Click += (_, _) =>
        {
            Logger.Info("导航到: 设置");
            ContentFrame.Navigate(new SettingsPage());
        };

        // 默认显示资讯页
        ContentFrame.Navigate(new NewsPage());
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

        // 可选：启动后关闭启动器
        // Application.Current.Shutdown();
    }
}
