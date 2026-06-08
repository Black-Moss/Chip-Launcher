using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
/// 设置页面 — 绑定到 AppConfig.Instance，修改自动保存
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "选择游戏可执行文件",
            AllowMultiple = false,
        };
        dialog.Filters.Add(new FileDialogFilter
        {
            Name = "可执行文件 (*.exe)",
            Extensions = { "exe" },
        });
        dialog.Filters.Add(new FileDialogFilter
        {
            Name = "所有文件 (*.*)",
            Extensions = { "*" },
        });

        var result = await dialog.ShowAsync(parentWindow);
        if (result is { Length: > 0 })
        {
            AppConfig.Instance.GamePath = result[0];
        }
    }
}
