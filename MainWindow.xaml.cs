using System.Windows;
using ChipLauncher.Services;
using ChipLauncher.Views;

namespace ChipLauncher;

/// <summary>
/// 主窗口 - 游戏启动器
/// </summary>
public partial class MainWindow : Window
{
    private readonly IGameService _gameService;
    private readonly AppConfig _config;

    public MainWindow()
    {
        InitializeComponent();
        Logger.Info("Chip Launcher 启动");

        _config = AppConfig.Load();
        _gameService = new GameService();

        BtnPlay.Click += (_, _) => LaunchGame();
        BtnMods.Click += (_, _) =>
        {
            Logger.Info("导航到: 模组管理");
            ContentFrame.Navigate(new ModsPage());
        };
        BtnNews.Click += (_, _) =>
        {
            Logger.Info("导航到: 游戏资讯");
            ContentFrame.Navigate(new Views.NewsPage());
        };
        BtnSettings.Click += (_, _) =>
        {
            Logger.Info("导航到: 设置");
            ContentFrame.Navigate(new SettingsPage());
        };

        // 默认显示资讯页
        ContentFrame.Navigate(new Views.NewsPage());
    }

    private void LaunchGame()
    {
        var appId = _config.SteamAppId;

        if (!string.IsNullOrEmpty(_config.GamePath))
        {
            Logger.Info($"启动游戏（本地路径）: {_config.GamePath}");
            _gameService.LaunchDirectly(_config.GamePath);
        }
        else
        {
            Logger.Info($"启动游戏（Steam）: AppId={appId}");
            _gameService.LaunchViaSteam(appId);
        }

        // 可选：启动后关闭启动器
        // Application.Current.Shutdown();
    }
}
