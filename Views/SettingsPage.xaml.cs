using System.Windows;
using System.Windows.Controls;
using ChipLauncher.Services;
using Microsoft.Win32;

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

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Title = "选择游戏可执行文件",
        };

        if (dialog.ShowDialog() == true)
            AppConfig.Instance.GamePath = dialog.FileName;
    }
}
