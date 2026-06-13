using System.Text.Json;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
///     皮肤元数据统一管理 — 在 <c>skins/skins_metadata.json</c> 中集中存储所有已下载皮肤的云端数据
///     （名称、作者、缩略图 URL、下载量、大小等），供皮肤管理页读取展示。
/// </summary>
public static class SkinsMetadataService
{
    private static readonly string MetadataFilePath;
    private static readonly object Lock = new();

    private static Dictionary<string, SkinMetadata>? _cache;

    static SkinsMetadataService()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        MetadataFilePath = Path.Combine(baseDir, "skins", "skins_metadata.json");
    }

    /// <summary>获取所有皮肤的元数据</summary>
    public static Dictionary<string, SkinMetadata> GetAll()
    {
        EnsureLoaded();
        return new Dictionary<string, SkinMetadata>(_cache!);
    }

    /// <summary>获取指定皮肤 ID 的元数据（若无返回 null）</summary>
    public static SkinMetadata? Get(int skinId)
    {
        EnsureLoaded();
        return _cache!.TryGetValue(skinId.ToString(), out var meta) ? meta : null;
    }

    /// <summary>保存指定皮肤的云端元数据（下载完成后调用）</summary>
    public static void Save(SkinDownloadItem skin)
    {
        EnsureLoaded();
        var key = skin.Id.ToString();
        _cache![key] = new SkinMetadata
        {
            Name = skin.Name,
            Author = skin.Author,
            ThumbnailUrl = skin.ThumbnailUrl,
            GalleryUrls = skin.GalleryUrls,
            Downloads = skin.Downloads,
            Size = skin.Size
        };
        Flush();
    }

    /// <summary>
    ///     保存元数据并确保皮肤目录（skins/{id}/）存在。
    ///     下载完成后调用，确保后续缩略图保存等操作不因目录缺失而失败。
    /// </summary>
    public static void SaveAndEnsureDir(SkinDownloadItem skin)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var skinDir = Path.Combine(baseDir, "skins", skin.Id.ToString());
        Directory.CreateDirectory(skinDir);
        Save(skin);
    }

    /// <summary>删除指定皮肤的元数据后，同时清理对应的皮肤目录</summary>
    public static void RemoveAndCleanDir(int skinId)
    {
        Remove(skinId);
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var skinDir = Path.Combine(baseDir, "skins", skinId.ToString());
        if (Directory.Exists(skinDir))
        {
            try { Directory.Delete(skinDir, recursive: true); }
            catch { /* 可能被占用，忽略 */ }
        }
    }

    /// <summary>删除指定皮肤的元数据（删除皮肤时调用）</summary>
    public static void Remove(int skinId)
    {
        EnsureLoaded();
        var key = skinId.ToString();
        if (_cache!.Remove(key))
            Flush();
    }

    /// <summary>清除所有元数据</summary>
    public static void Clear()
    {
        _cache = new Dictionary<string, SkinMetadata>();
        Flush();
    }

    /// <summary>确保缓存已从文件加载</summary>
    private static void EnsureLoaded()
    {
        if (_cache != null) return;
        lock (Lock)
        {
            if (_cache != null) return;

            if (File.Exists(MetadataFilePath))
            {
                try
                {
                    var json = File.ReadAllText(MetadataFilePath);
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var dict = new Dictionary<string, SkinMetadata>();
                    if (root.TryGetProperty("skins", out var skins))
                    {
                        foreach (var entry in skins.EnumerateObject())
                        {
                            var meta = new SkinMetadata();
                            var val = entry.Value;
                            if (val.TryGetProperty("name", out var n)) meta.Name = n.GetString();
                            if (val.TryGetProperty("author", out var a)) meta.Author = a.GetString();
                            if (val.TryGetProperty("thumbnailUrl", out var t)) meta.ThumbnailUrl = t.GetString();
                            if (val.TryGetProperty("galleryUrls", out var g))
                            {
                                var urls = new List<string>();
                                foreach (var u in g.EnumerateArray())
                                {
                                    if (u.GetString() is { } str) urls.Add(str);
                                }
                                meta.GalleryUrls = urls;
                            }
                            if (val.TryGetProperty("downloads", out var d)) meta.Downloads = d.GetInt64();
                            if (val.TryGetProperty("size", out var s)) meta.Size = s.GetInt64();
                            dict[entry.Name] = meta;
                        }
                    }
                    _cache = dict;
                    return;
                }
                catch
                {
                    // 文件损坏则重新创建
                }
            }

            _cache = new Dictionary<string, SkinMetadata>();
        }
    }

    /// <summary>将缓存写入磁盘</summary>
    private static void Flush()
    {
        if (_cache == null) return;

        var dir = Path.GetDirectoryName(MetadataFilePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var data = new Dictionary<string, object>
        {
            ["skins"] = _cache.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    name = kv.Value.Name,
                    author = kv.Value.Author,
                    thumbnailUrl = kv.Value.ThumbnailUrl,
                    galleryUrls = kv.Value.GalleryUrls,
                    downloads = kv.Value.Downloads,
                    size = kv.Value.Size
                }
            )
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(MetadataFilePath, json);
    }
}

/// <summary>单个皮肤的云元数据</summary>
public class SkinMetadata
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? ThumbnailUrl { get; set; }
    public List<string>? GalleryUrls { get; set; }
    public long Downloads { get; set; }
    public long Size { get; set; }
}
