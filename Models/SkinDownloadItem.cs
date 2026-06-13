using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChipLauncher.Models;

/// <summary>皮肤排序模式</summary>
public enum SkinSortMode
{
    /// <summary>A-Z（按名称升序）</summary>
    NameAz,

    /// <summary>Z-A（按名称降序）</summary>
    NameZa,

    /// <summary>ID 升序</summary>
    Id,

    /// <summary>下载量（降序）</summary>
    Downloads,

    /// <summary>文件大小（降序）</summary>
    Size,

    /// <summary>上传时间（降序）</summary>
    UploadTime
}

/// <summary>皮肤显示模式</summary>
public enum SkinDisplayMode
{
    /// <summary>瀑布流（网格）</summary>
    Waterfall,

    /// <summary>列表</summary>
    List
}

/// <summary>
///     皮肤下载项 — 表示皮肤站上的一个可下载皮肤
/// </summary>
public class SkinDownloadItem : INotifyPropertyChanged
{
    private bool _isDownloaded;
    private bool _isChecked;
    private bool _isDownloading;
    private string? _thumbnailCachePath;
    private object? _thumbnailImage;
    private List<object?>? _galleryImages;

    /// <summary>皮肤唯一数字 ID</summary>
    public int Id { get; init; }

    /// <summary>皮肤名称</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>作者名称</summary>
    public string? Author { get; init; }

    /// <summary>下载量</summary>
    public long Downloads { get; init; }

    /// <summary>文件大小（字节）</summary>
    public long Size { get; init; }

    /// <summary>上传时间</summary>
    public DateTime UploadTime { get; init; }

    /// <summary>皮肤站上的缩略图 URL</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>本地缩略图缓存路径（由 SkinCache 管理，支持属性通知）</summary>
    public string? ThumbnailCachePath
    {
        get => _thumbnailCachePath;
        set
        {
            if (_thumbnailCachePath == value) return;
            _thumbnailCachePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThumbnailSource));
        }
    }

    /// <summary>缩略图图像对象（Avalonia Bitmap，由 UI 层设置）</summary>
    public object? ThumbnailImage
    {
        get => _thumbnailImage;
        set
        {
            if (_thumbnailImage == value) return;
            _thumbnailImage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>皮肤分类名称（如 "Funny", "Gunsaw", "Species" 等）</summary>
    public string? Category { get; init; }

    /// <summary>是否已下载到本地 skins 目录</summary>
    public bool IsDownloaded
    {
        get => _isDownloaded;
        set
        {
            if (_isDownloaded == value) return;
            _isDownloaded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>是否正在下载中（下载按钮变为进度条）</summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (_isDownloading == value) return;
            _isDownloading = value;
            OnPropertyChanged();
        }
    }

    /// <summary>复选框是否选中（用于批量操作，同 ModInfo.IsChecked）</summary>
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

    /// <summary>本地皮肤目录的完整路径（由 ScanLocalSkins 填充，用于删除等操作）</summary>
    public string? DirectoryPath { get; init; }

    /// <summary>缩略图源（优先本地缓存路径，其次远程 URL）</summary>
    public string? ThumbnailSource => ThumbnailCachePath ?? ThumbnailUrl;

    /// <summary>格式化的文件大小（如 "1.2 MB"）</summary>
    public string SizeDisplay => FormatSize(Size);

    /// <summary>格式化的下载量</summary>
    public string DownloadsDisplay => FormatNumber(Downloads);

    /// <summary>画廊图片 URL 列表（皮肤站 carousel 中的多张图片）</summary>
    public List<string>? GalleryUrls { get; init; }

    /// <summary>画廊图片 Bitmap 列表（由 LoadThumbnailAsync 填充）</summary>
    public List<object?>? GalleryImages
    {
        get => _galleryImages;
        set
        {
            if (_galleryImages == value) return;
            _galleryImages = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
        };
    }

    private static string FormatNumber(long value)
    {
        return value switch
        {
            < 1000 => value.ToString(),
            < 1_000_000 => $"{value / 1000.0:F1}K",
            _ => $"{value / 1_000_000.0:F1}M"
        };
    }
}