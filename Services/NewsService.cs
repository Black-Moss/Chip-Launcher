using System.Collections.Concurrent;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
///     从 Steam 新闻 RSS 获取游戏资讯（内存 + 本地文件持久缓存）
///     文件缓存除非手动清除，否则永久保留。
/// </summary>
public partial class NewsService : INewsService
{
    // ── 内存缓存（运行时快速读取） ──────────────────────────
    private static readonly ConcurrentDictionary<string, CacheEntry> MemCache = new();
    private static readonly TimeSpan MemCacheDuration = TimeSpan.FromDays(30);

    // ── 本地文件缓存路径 ────────────────────────────────────
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChipLauncher");

    private static readonly string CacheFilePath = Path.Combine(CacheDir, "news_cache.json");

    // 用于文件读写的锁（避免并发冲突）
    private static readonly object FileLock = new();
    private readonly HttpClient _httpClient;

    public NewsService()
    {
        // 全局 SSL / 连接优化
        ServicePointManager.Expect100Continue = false;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.ServerCertificateValidationCallback =
            (_, _, _, _) => true;

        var handler = new SocketsHttpHandler
        {
            // 仅使用 TLS 1.2（Steam CDN 对 TLS 1.3 兼容性不稳定）
            SslOptions =
            {
                EnabledSslProtocols = SslProtocols.Tls12,
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            // 连接池优化
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 5
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            // 强制 HTTP/1.1
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/125.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept",
            "application/rss+xml,application/xml,text/xml,*/*");
        _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
    }

    // ════════════════════════════════════════════════════════
    //  网络获取
    // ════════════════════════════════════════════════════════

    public async Task<List<NewsItem>?> GetNewsAsync()
    {
        // 1. 检查缓存（内存 + 文件回退）
        var cached = TryGetCached();
        if (cached != null)
            return cached;

        // 2. 无任何缓存 → 发起 HTTP 请求（指定简体中文）
        var url = "https://store.steampowered.com/feeds/news/app/4576490/?l=schinese";

        try
        {
            Logger.Info("正在获取 Steam 资讯");
            var xml = await _httpClient.GetStringAsync(url);
            var items = ParseSteamRss(xml);

            // 3. 保存到内存缓存
            MemCache["4576490"] = new CacheEntry { Items = items };

            // 4. 保存到本地文件（永久保留）
            SaveToFile(items);

            Logger.Info($"获取资讯成功: {items.Count} 条 (已缓存到本地)");
            return items;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error($"网络请求失败: {url}", ex);
            return null;
        }
        catch (TaskCanceledException)
        {
            Logger.Warn("请求超时");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"解析资讯失败: {ex.Message}", ex);
            return null;
        }
    }

    // ════════════════════════════════════════════════════════
    //  缓存读取（内存 → 本地文件回退）
    // ════════════════════════════════════════════════════════

    /// <summary>
    ///     尝试获取缓存：先查内存缓存，若无效则尝试从本地文件加载。
    ///     文件缓存无过期时间（除非手动清除），加载后同时填充内存缓存。
    /// </summary>
    public static List<NewsItem>? TryGetCached()
    {
        // 1. 内存缓存命中且有效
        if (MemCache.TryGetValue("4576490", out var cached) && cached.IsValid)
            return cached.Items;

        // 2. 尝试从本地文件加载
        var fileItems = LoadFromFile();
        if (fileItems == null) return null;
        // 填充到内存缓存（标记为当前时间，使其有效）
        MemCache["4576490"] = new CacheEntry { Items = fileItems };
        Logger.Info($"从本地文件加载资讯缓存 ({fileItems.Count} 条)");
        return fileItems;
    }

    /// <summary>从本地 JSON 文件中读取指定 appId 的缓存数据</summary>
    private static List<NewsItem>? LoadFromFile()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
                return null;

            lock (FileLock)
            {
                var json = File.ReadAllText(CacheFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<NewsItem>>>(json);
                if (data != null
                    && data.TryGetValue("4576490", out var items)
                    && items.Count > 0)
                    return items;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取本地缓存文件失败: {ex.Message}");
        }

        return null;
    }

    // ════════════════════════════════════════════════════════
    //  本地文件写入
    // ════════════════════════════════════════════════════════

    /// <summary>将资讯数据写入本地 JSON 文件（永久保留）</summary>
    private static void SaveToFile(List<NewsItem> items)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);

            lock (FileLock)
            {
                Dictionary<string, List<NewsItem>> data;

                // 读取现有文件内容（增量合并）
                if (File.Exists(CacheFilePath))
                {
                    var existingJson = File.ReadAllText(CacheFilePath);
                    data = JsonSerializer.Deserialize<Dictionary<string, List<NewsItem>>>(existingJson)
                           ?? new Dictionary<string, List<NewsItem>>();
                }
                else
                {
                    data = new Dictionary<string, List<NewsItem>>();
                }

                data["4576490"] = items;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(data, options));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"保存本地缓存文件失败: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════
    //  清除缓存（内存 + 本地文件）
    // ════════════════════════════════════════════════════════

    /// <summary>清除所有缓存（内存 + 本地文件），下次获取会重新从 Steam 拉取</summary>
    public static void ClearCache()
    {
        MemCache.Clear();

        try
        {
            if (File.Exists(CacheFilePath))
            {
                File.Delete(CacheFilePath);
                Logger.Info("本地缓存文件已删除");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"删除本地缓存文件失败: {ex.Message}");
        }

        Logger.Info("资讯缓存已清除（内存 + 本地文件）");
    }

    // ════════════════════════════════════════════════════════
    //  RSS 解析
    // ════════════════════════════════════════════════════════

    private static List<NewsItem> ParseSteamRss(string xml)
    {
        var doc = XDocument.Parse(xml);

        return doc.Descendants("item")
            .Select(item => new NewsItem
            {
                Title = item.Element("title")?.Value.Trim() ?? "无标题",
                Content = StripHtmlTags(
                    item.Element("description")?.Value.Trim() ?? "暂无内容"),
                Url = item.Element("link")?.Value.Trim() ?? string.Empty,
                PublishDate = TryParseDate(
                    item.Element("pubDate")?.Value)
            })
            .ToList();
    }

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // 先换行标签 → 换行符
        var text = MyRegex().Replace(html, "\n");
        // 去掉剩余 HTML 标签
        text = MyRegex1().Replace(text, " ");
        // 压缩空白（保留换行）
        text = MyRegex2().Replace(text, " ");
        // 合并连续空行
        text = MyRegex3().Replace(text, "\n\n");
        return text.Trim();
    }

    private static DateTime TryParseDate(string? dateStr)
    {
        return DateTime.TryParse(dateStr, out var result)
            ? result
            : DateTime.UtcNow;
    }

    [GeneratedRegex(@"</?(?:br|p|div|li|h[1-6])(?:\s[^>]*)?>")]
    private static partial Regex MyRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex MyRegex1();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex MyRegex2();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MyRegex3();

    private class CacheEntry
    {
        public List<NewsItem> Items { get; init; } = [];
        public DateTime CachedAt { get; } = DateTime.UtcNow;
        public bool IsValid => DateTime.UtcNow - CachedAt < MemCacheDuration;
    }
}