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