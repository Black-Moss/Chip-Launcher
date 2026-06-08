using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChipLauncher.Models;

/// <summary>
///     表示 BepInEx plugins 目录下的一个模组
///     结构：plugins/模组名/Mod.dll（启用）或 Mod.disabled（禁用）
/// </summary>
public class ModInfo : INotifyPropertyChanged
{
    private bool _isChecked;

    /// <summary>模组显示名称（目录名）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>模组目录的完整路径</summary>
    public string DirectoryPath { get; init; } = string.Empty;

    /// <summary>当前插件文件路径（.dll 或 .disabled）</summary>
    public string PluginFilePath { get; set; } = string.Empty;

    /// <summary>BepInPlugin GUID，用于匹配配置文件</summary>
    public string? Guid { get; init; }

    /// <summary>是否已启用（文件后缀为 .dll）</summary>
    public bool IsEnabled
    {
        get => PluginFilePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        set
        {
            /* 由切换逻辑控制 */
        }
    }

    /// <summary>复选框是否选中（用于批量操作）</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}