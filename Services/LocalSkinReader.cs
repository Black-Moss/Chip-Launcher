using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
///     本地皮肤读取器 — 从 <c>skins/{Id}/Body/*</c> 格式读取已下载的皮肤
///     "Body" 是 Casualties Unknown 游戏期望的皮肤文件目录名。
/// </summary>
public static class LocalSkinReader
{
    static LocalSkinReader()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        SkinsDirectory = Path.Combine(baseDir, "skins");
    }

    /// <summary>本地 skins 根目录</summary>
    public static string SkinsDirectory { get; }

    /// <summary>确保 skins 目录存在</summary>
    private static void EnsureSkinsDir()
    {
        if (!Directory.Exists(SkinsDirectory))
            Directory.CreateDirectory(SkinsDirectory);
    }

    /// <summary>
    ///     扫描本地 skins 目录，返回所有已下载的皮肤信息。
    ///     格式：<c>skins/{Id}/Body/*</c>（也兼容旧格式 <c>Skin_{Id}/Body/*</c>）
    /// </summary>
    public static List<SkinDownloadItem> ScanLocalSkins()
    {
        var result = new List<SkinDownloadItem>();
        EnsureSkinsDir();

        foreach (var skinDir in Directory.GetDirectories(SkinsDirectory))
        {
            // 跳过隐藏目录（如 .cache）
            var dirName = Path.GetFileName(skinDir);
            if (dirName.StartsWith('.')) continue;

            // 兼容大小写：查找 Body 或 body 目录
            var bodyDir = Path.Combine(skinDir, "Body");
            if (!Directory.Exists(bodyDir))
                bodyDir = Path.Combine(skinDir, "body");
            if (!Directory.Exists(bodyDir)) continue;

            var files = Directory.GetFiles(bodyDir).ToList();
            if (files.Count == 0) continue;

            // 尝试从目录名解析出 ID（支持纯数字 或 Skin_{Id} 格式）
            var id = 0;
            var nameForParse = dirName;
            if (nameForParse.StartsWith("Skin_", StringComparison.OrdinalIgnoreCase))
                nameForParse = dirName[5..]; // 去掉 "Skin_" 前缀
            if (int.TryParse(nameForParse, out var parsed))
                id = parsed;

            // 从统一元数据文件 skins/skins_metadata.json 读取云端数据
            var meta = id > 0 ? SkinsMetadataService.Get(id) : null;
            var cloudName = meta?.Name;
            var cloudAuthor = meta?.Author;
            var cloudThumbnailUrl = meta?.ThumbnailUrl;
            var cloudGalleryUrls = meta?.GalleryUrls;
            var cloudDownloads = meta?.Downloads ?? 0;
            var cloudSize = meta?.Size ?? 0;

            // 缩略图：优先本地皮肤目录 thumb.*（下载时保存的），其次云端缓存
            string? thumbPath = null;
            // 1. 先查本地皮肤目录（thumb.* / preview.* / thumbnail.*）
            thumbPath = FindThumbnail(skinDir, cloudName ?? dirName);
            // 2. 无本地文件时，尝试用云端缓存
            if (thumbPath == null && cloudThumbnailUrl != null)
            {
                if (SkinCache.HasCache(cloudName ?? dirName))
                    thumbPath = SkinCache.GetLocalPath(cloudName ?? dirName);
            }

            result.Add(new SkinDownloadItem
            {
                Id = id,
                Name = cloudName ?? dirName,
                Author = cloudAuthor,
                Downloads = cloudDownloads,
                Size = cloudSize > 0
                    ? cloudSize
                    : files.Sum(f =>
                    {
                        try
                        {
                            return new FileInfo(f).Length;
                        }
                        catch
                        {
                            return 0L;
                        }
                    }),
                UploadTime = DateTime.MinValue,
                ThumbnailUrl = cloudThumbnailUrl,
                ThumbnailCachePath = thumbPath,
                GalleryUrls = cloudGalleryUrls,
                IsDownloaded = true,
                DirectoryPath = skinDir // 记录完整路径，供删除等操作使用
            });
        }

        return result;
    }

    /// <summary>检查指定皮肤 ID 是否已下载到本地</summary>
    public static bool IsSkinDownloaded(int skinId)
    {
        // 先检查纯数字目录
        var skinDir = Path.Combine(SkinsDirectory, $"{skinId}");
        if (Directory.Exists(skinDir))
        {
            var bodyDir = Path.Combine(skinDir, "Body");
            if (!Directory.Exists(bodyDir))
                bodyDir = Path.Combine(skinDir, "body");
            return Directory.Exists(bodyDir) &&
                   Directory.GetFiles(bodyDir).Length > 0;
        }

        // 回退：检查 Skin_{Id} 旧格式
        skinDir = Path.Combine(SkinsDirectory, $"Skin_{skinId}");
        if (!Directory.Exists(skinDir)) return false;
        {
            var bodyDir = Path.Combine(skinDir, "Body");
            if (!Directory.Exists(bodyDir))
                bodyDir = Path.Combine(skinDir, "body");
            return Directory.Exists(bodyDir) &&
                   Directory.GetFiles(bodyDir).Length > 0;
        }

    }

    /// <summary>获取指定 skin 目录名的本地路径（若无则返回 null）</summary>
    public static string? GetSkinDirectory(string dirName)
    {
        var skinDir = Path.Combine(SkinsDirectory, dirName);
        return Directory.Exists(skinDir) ? skinDir : null;
    }

    /// <summary>获取指定皮肤 ID 的本地路径（先找 {Id} 再回退 Skin_{Id}）</summary>
    public static string? GetSkinDirectory(int skinId)
    {
        // 先找纯数字目录
        var dir = GetSkinDirectory($"{skinId}");
        return dir ??
               // 回退 Skin_ 前缀旧格式
               GetSkinDirectory($"Skin_{skinId}");
    }

    /// <summary>获取指定 skin 目录名的 Body 目录路径（若无则返回 null）</summary>
    private static string? GetBodyDirectory(string dirName)
    {
        var bodyDir = Path.Combine(SkinsDirectory, dirName, "Body");
        return Directory.Exists(bodyDir) ? bodyDir : null;
    }

    /// <summary>获取指定皮肤 ID 的 Body 目录路径（先找 {Id} 再回退 Skin_{Id}）</summary>
    public static string? GetBodyDirectory(int skinId)
    {
        // 先找纯数字目录
        var dir = GetBodyDirectory($"{skinId}");
        return dir ??
               // 回退 Skin_ 前缀旧格式
               GetBodyDirectory($"Skin_{skinId}");
    }

    /// <summary>获取 skins 目录的总皮肤数</summary>
    public static int GetLocalSkinCount()
    {
        EnsureSkinsDir();
        return Directory.GetDirectories(SkinsDirectory)
            .Count(d =>
            {
                var name = Path.GetFileName(d);
                return !name.StartsWith('.') &&
                       Directory.Exists(Path.Combine(d, "Body"));
            });
    }

    /// <summary>
    ///     在皮肤目录中查找缩略图。
    ///     优先级：本地 thumb.*（下载时保存的） > 云端缓存。
    ///     <b>不会</b> 回退到 Body 目录的低分辨率游戏贴图。
    /// </summary>
    private static string? FindThumbnail(string skinDir, string skinName)
    {
        // 1. 先在皮肤根目录找 thumb.* 或 preview.*（下载时保存的高清缩略图）
        var candidates = Directory.GetFiles(skinDir, "thumb.*")
            .Concat(Directory.GetFiles(skinDir, "preview.*"))
            .Concat(Directory.GetFiles(skinDir, "thumbnail.*"))
            .ToList();

        if (candidates.Count > 0)
            return candidates[0];

        // 2. 检查云端缓存
        return SkinCache.HasCache(skinName)
            ? SkinCache.GetLocalPath(skinName) :
            // 3. ⚠ 不再回退到 Body 目录的贴图 — 那些是游戏内的低分辨率贴图，
            //   用作缩略图会非常模糊。云端缓存或本地 thumb.* 都不存在时返回 null，
            //   由调用方决定是否异步下载云端缩略图。
            null;
    }
}