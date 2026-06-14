using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ChipLauncher.Models;

/// <summary>
///     模组下载项 — 表示托管服务器上的一个可下载模组
/// </summary>
public class ModDownloadItem : INotifyPropertyChanged
{
    private bool _isDownloaded;
    private bool _isDownloading;
    private double _downloadProgress;

    /// <summary>模组唯一 ID</summary>
    public int Id { get; init; }

    /// <summary>模组英文原名</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>汉化名称</summary>
    [JsonPropertyName("chineseName")]
    public string ChineseName { get; init; } = string.Empty;

    /// <summary>模组描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>最新版本号</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>分类（如 UI、功能、皮肤前置等）</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>下载直链</summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>来源页面（如 N 网）</summary>
    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>预览图链接</summary>
    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; init; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; init; }

    /// <summary>是否已安装到本地</summary>
    [JsonIgnore]
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

    /// <summary>是否正在下载</summary>
    [JsonIgnore]
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (_isDownloading == value) return;
            _isDownloading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowProgress));
        }
    }

    /// <summary>是否显示进度（正在下载时）</summary>
    [JsonIgnore]
    public bool ShowProgress => IsDownloading;

    /// <summary>下载进度（0-100）</summary>
    [JsonIgnore]
    public double DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            if (Math.Abs(_downloadProgress - value) < 0.5) return;
            _downloadProgress = value;
            OnPropertyChanged();
        }
    }

    /// <summary>格式化后的文件大小</summary>
    [JsonIgnore]
    public string FileSizeDisplay => FormatSize(FileSize);

    /// <summary>显示名称（中文名 + 英文名）</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(ChineseName) ? Name : $"{ChineseName} ({Name})";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

/// <summary>托管服务器响应的根对象</summary>
public class ModListResponse
{
    public int Total { get; init; }
    public int Page { get; init; }
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; init; }
    public List<ModDownloadItem> Items { get; init; } = [];
}
