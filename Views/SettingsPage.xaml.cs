using System.Windows;
using System.Windows.Controls;
using ChipLauncher.Services;
using Microsoft.Win32;

namespace ChipLauncher.Views;

/// <summary>
/// 设置页面
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly AppConfig _config;

    public SettingsPage()
    {
        InitializeComponent();
        _config = AppConfig.Load();
        LoadSettings();
    }

    private void LoadSettings()
    {
        AppIdBox.Text = _config.SteamAppId;
        GamePathBox.Text = _config.GamePath ?? string.Empty;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Title = "选择游戏可执行文件",
        };

        if (dialog.ShowDialog() == true)
            GamePathBox.Text = dialog.FileName;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _config.SteamAppId = AppIdBox.Text;
        _config.GamePath = GamePathBox.Text;
        _config.Save();

        MessageBox.Show("设置已保存！", "Chip Launcher",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
