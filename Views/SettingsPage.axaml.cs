using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
///     设置页面 — 绑定到 AppConfig.Instance，修改自动保存
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        SettingsSearchBox.TextChanged += OnSettingsSearchChanged;
    }

    /// <summary>搜索框文本变化 → 筛选显示匹配的设置项</summary>
    private void OnSettingsSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var keyword = SettingsSearchBox.Text?.Trim();

        if (string.IsNullOrEmpty(keyword))
        {
            ShowAllSettings();
            return;
        }

        SettingsGamePathRow.IsVisible = MatchSetting(keyword, "游戏路径", "路径", "浏览", "GamePath");
        SettingsRetryRow.IsVisible = MatchSetting(keyword, "重试次数", "重试", "资讯", "加载");
        SettingsRotationRow.IsVisible = MatchSetting(keyword, "轮播间隔", "轮播", "文本", "游戏文本", "切换");
        SettingsStartupRow.IsVisible = MatchSetting(keyword, "启动页面", "启动", "页面", "默认");
        SettingsDeleteConfirmRow.IsVisible = MatchSetting(keyword, "删除确认", "删除", "确认", "二次确认", "Delete", "Confirm");
        SettingsNexusDomainRow.IsVisible = MatchSetting(keyword, "NexusMods 域名", "域名", "NexusMods", "domain", "游戏域名");
        SettingsCacheRow.IsVisible = MatchSetting(keyword, "清除缓存", "缓存", "清除", "资讯缓存");
    }

    /// <summary>检查关键词是否匹配任意一个设置项关键词</summary>
    private static bool MatchSetting(string keyword, params string[] terms)
    {
        return terms.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                              || keyword.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>显示所有设置项</summary>
    private void ShowAllSettings()
    {
        SettingsGamePathRow.IsVisible = true;
        SettingsRetryRow.IsVisible = true;
        SettingsRotationRow.IsVisible = true;
        SettingsStartupRow.IsVisible = true;
        SettingsDeleteConfirmRow.IsVisible = true;
        SettingsNexusDomainRow.IsVisible = true;
        SettingsCacheRow.IsVisible = true;
    }

    /// <summary>清除本地缓存的 Steam 资讯</summary>
    private void OnClearCacheClick(object? sender, RoutedEventArgs e)
    {
        NewsService.ClearCache();
        Logger.Info("用户手动清除了资讯缓存");
    }
    
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择游戏可执行文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("可执行文件 (*.exe)") { Patterns = ["*.exe"] },
                new FilePickerFileType("所有文件 (*.*)") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0) AppConfig.Instance.GamePath = files[0].Path.LocalPath;
    }
}