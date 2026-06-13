using System.Text.RegularExpressions;

namespace ChipLauncher.Services;

/// <summary>
///     皮肤缩略图缓存管理 — 按皮肤名称缓存缩略图与高清预览图到本地 <c>skins/.cache/thumbnails/</c>
///     无论是否下载皮肤，图片都会缓存。
/// </summary>
public static partial class SkinCache
{
    private static readonly string CacheDir;
    private static readonly string PreviewDir;

    static SkinCache()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        CacheDir = Path.Combine(baseDir, "skins", ".cache", "thumbnails");
        PreviewDir = Path.Combine(baseDir, "skins", ".cache", "previews");
    }

    /// <summary>缓存目录路径</summary>
    public static string CacheDirectory => CacheDir;

    /// <summary>确保缓存目录存在</summary>
    public static void EnsureCacheDir()
    {
        if (!Directory.Exists(CacheDir))
            Directory.CreateDirectory(CacheDir);
    }

    /// <summary>根据皮肤名称获取预期的缓存文件路径</summary>
    public static string GetCachedPath(string skinName)
    {
        EnsureCacheDir();
        var safeName = SanitizeFileName(skinName);
        return Path.Combine(CacheDir, $"{safeName}.cache");
    }

    /// <summary>检查指定皮肤是否有本地缩略图缓存</summary>
    public static bool HasCache(string skinName)
    {
        var path = GetCachedPath(skinName);
        return File.Exists(path);
    }

    /// <summary>写入缩略图缓存（直接写入原始图片字节）</summary>
    public static async Task CacheThumbnailAsync(string skinName, Stream imageStream)
    {
        var path = GetCachedPath(skinName);
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await using var fs = File.Create(path);
        await imageStream.CopyToAsync(fs);
    }

    /// <summary>写入缩略图缓存（从字节数组）</summary>
    public static async Task CacheThumbnailAsync(string skinName, byte[] imageData)
    {
        var path = GetCachedPath(skinName);
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(path, imageData);
    }

    /// <summary>获取本地缓存缩略图的完整路径（若无缓存返回 null）</summary>
    public static string? GetLocalPath(string skinName)
    {
        return HasCache(skinName) ? GetCachedPath(skinName) : null;
    }

    // ── 高清预览图缓存（画廊图，用于详情面板） ────────────────

    /// <summary>获取预览图缓存路径（画廊第一张图）</summary>
    public static string GetPreviewCachedPath(string skinName)
    {
        if (!Directory.Exists(PreviewDir))
            Directory.CreateDirectory(PreviewDir);
        var safeName = SanitizeFileName(skinName);
        return Path.Combine(PreviewDir, $"{safeName}.cache");
    }

    /// <summary>检查是否有预览图缓存</summary>
    public static bool HasPreviewCache(string skinName)
    {
        var path = GetPreviewCachedPath(skinName);
        return File.Exists(path);
    }

    /// <summary>获取本地预览图缓存路径（若无返回 null）</summary>
    public static string? GetPreviewLocalPath(string skinName)
    {
        return HasPreviewCache(skinName) ? GetPreviewCachedPath(skinName) : null;
    }

    /// <summary>写入预览图缓存</summary>
    public static async Task CachePreviewAsync(string skinName, byte[] imageData)
    {
        var path = GetPreviewCachedPath(skinName);
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(path, imageData);
    }

    /// <summary>清除所有缩略图缓存</summary>
    public static void ClearCache()
    {
        if (!Directory.Exists(CacheDir)) return;
        foreach (var file in Directory.GetFiles(CacheDir, "*.cache"))
        {
            try { File.Delete(file); }
            catch { /* 忽略删除失败 */ }
        }
    }

    /// <summary>获取缓存文件大小（字节）</summary>
    public static long GetCacheSize()
    {
        if (!Directory.Exists(CacheDir)) return 0;
        return Directory.GetFiles(CacheDir, "*.cache")
                        .Sum(f =>
                        {
                            try { return new FileInfo(f).Length; }
                            catch { return 0L; }
                        });
    }

    /// <summary>清理文件名中的非法字符，防止路径注入</summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "_empty";
        return InvalidFileNameCharsRegex().Replace(name, "_");
    }

    [GeneratedRegex(@"[<>:""/\\|?*]", RegexOptions.Compiled)]
    private static partial Regex InvalidFileNameCharsRegex();
}
