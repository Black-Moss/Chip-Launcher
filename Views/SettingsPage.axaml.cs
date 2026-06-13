using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChipLauncher.Services;
using SukiUI.Controls;
using SukiUI.Dialogs;

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

        // 启动页面 ComboBox 下拉关闭后，确保设置页侧边栏项仍然选中
        // （防止 SukiSideMenu 在下拉交互中误触其他菜单项）
        StartupPageCombo.DropDownClosed += OnStartupPageComboDropDownClosed;

        // 游戏路径输入框：若用户未手动设置路径但 Steam 自动检测到游戏，显示淡色占位文本
        if (string.IsNullOrEmpty(AppConfig.Instance.GamePath))
        {
            var detectedDir = GameLocalization.GetGameDirectory();
            if (!string.IsNullOrEmpty(detectedDir))
                GamePathTextBox.Watermark = detectedDir;
        }
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
        SettingsAutoUpdateRow.IsVisible = MatchSetting(keyword, "自动更新", "更新", "检查更新", "GitHub");
        SettingsDeleteConfirmRow.IsVisible = MatchSetting(keyword, "删除确认", "删除", "确认", "二次确认", "Delete", "Confirm");
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
        SettingsAutoUpdateRow.IsVisible = true;
        SettingsDeleteConfirmRow.IsVisible = true;
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

    /// <summary>
    ///     ComboBox 下拉关闭后，重新确保"设置"侧边栏项处于选中状态。
    ///     防止 SukiSideMenu 在下拉交互中误触其他菜单项（如模组管理）。
    /// </summary>
    private void OnStartupPageComboDropDownClosed(object? sender, EventArgs e)
    {
        // 通过视觉树找到主窗口，重新设置"设置"侧边栏项为选中
        if (TopLevel.GetTopLevel(this) is not SukiWindow window) return;
        var sideMenuSettings = window.FindControl<SukiSideMenuItem>("SideMenuSettings");
        if (sideMenuSettings is { IsSelected: false })
            sideMenuSettings.IsSelected = true;
    }

    /// <summary>手动检查更新</summary>
    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        // 禁用按钮防重复点击
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            btn.Content = "检查中…";
        }

        try
        {
            var result = await UpdateService.CheckForUpdateAsync();

            if (result == null)
            {
                // 没有新版本 → Toast 提示
                AppNotification.Show("已是最新版本");
                return;
            }

            var updateInfo = result.Value;
            var version = updateInfo.Version;
            var url = updateInfo.Url;

            // 有更新 → 弹出 SukiUI Dialog
            await MainWindow.DialogManager.CreateDialog()
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
                        Logger.Error($"打开下载页失败: {ex.Message}");
                    }
                }, true, "Flat", "Accent")
                .TryShowAsync();
        }
        catch (Exception ex)
        {
            AppNotification.Show("检查更新失败", NotificationType.Error);
            Logger.Error($"手动检查更新失败: {ex.Message}");
        }
        finally
        {
            if (sender is Button btn2)
            {
                btn2.IsEnabled = true;
                btn2.Content = "🔍 检查更新";
            }
        }
    }

    /// <summary>重置所有设置为默认值</summary>
    private async void OnResetSettingsClick(object? sender, RoutedEventArgs e)
    {
        // 确认对话框
        var result = await MainWindow.DialogManager.CreateDialog()
            .WithTitle("重置设置")
            .WithContent("确定要将所有设置恢复为默认值吗？此操作不可撤销。")
            .WithActionButton("取消", _ => { }, true)
            .WithActionButton("确定重置", _ =>
            {
                AppConfig.Instance.Reset();
                AppNotification.Show("设置已重置为默认值", NotificationType.Success);
            }, true, "Flat", "Accent")
            .TryShowAsync();
    }
}