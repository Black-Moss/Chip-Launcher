using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChipLauncher.Models;

/// <summary>
///     表示一个游戏皮肤选项（占位符模型，后续实现完整功能）
/// </summary>
public class SkinInfo : INotifyPropertyChanged
{
    private bool _isEnabledForLaunch;

    /// <summary>皮肤显示名称</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>缩略图路径</summary>
    public string? ThumbnailPath { get; init; }

    /// <summary>皮肤描述</summary>
    public string? Description { get; init; }

    /// <summary>皮肤所在文件夹路径</summary>
    public string? DirectoryPath { get; init; }

    /// <summary>"启动游戏时使用" 开关</summary>
    public bool IsEnabledForLaunch
    {
        get => _isEnabledForLaunch;
        set
        {
            if (_isEnabledForLaunch == value) return;
            _isEnabledForLaunch = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
