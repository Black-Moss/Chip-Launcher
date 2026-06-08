using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Xml.Linq;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
/// 从 Steam 新闻 RSS 获取游戏资讯（支持内存缓存）
/// </summary>
public partial class NewsService : INewsService
{
    private readonly HttpClient _httpClient;

    // ── 内存缓存 ──────────────────────────────────────────────
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private class CacheEntry
    {
        public List<NewsItem> Items { get; init; } = [];
        public DateTime CachedAt { get; init; } = DateTime.UtcNow;
        public bool IsValid => DateTime.UtcNow - CachedAt < CacheDuration;
    }

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
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            // 连接池优化
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 5,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            // 强制 HTTP/1.1
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/125.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept",
            "application/rss+xml,application/xml,text/xml,*/*");
        _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
    }

    /// <summary>检查缓存中是否有有效的资讯数据（不发起 HTTP 请求）</summary>
    public static List<NewsItem>? TryGetCached(string appId)
    {
        if (Cache.TryGetValue(appId, out var cached) && cached.IsValid)
            return cached.Items;
        return null;
    }

    public async Task<List<NewsItem>?> GetNewsAsync(string appId)
    {
        // 1. 检查缓存是否有效
        if (Cache.TryGetValue(appId, out var cached) && cached.IsValid)
        {
            Logger.Info($"使用缓存资讯: AppId={appId} ({cached.Items.Count} 条)");
            return cached.Items;
        }

        // 2. 缓存失效 → 发起一次 HTTP 请求（失败不自动重试）
        var url = $"https://store.steampowered.com/feeds/news/app/{appId}/";

        try
        {
            Logger.Info($"正在获取 Steam 资讯: AppId={appId}");
            var xml = await _httpClient.GetStringAsync(url);
            var items = ParseSteamRss(xml);

            // 更新缓存
            Cache[appId] = new CacheEntry { Items = items };
            Logger.Info($"获取资讯成功: {items.Count} 条 (已缓存 {CacheDuration.TotalMinutes} 分钟)");
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

    /// <summary>
    /// 清除所有缓存，下次调用 GetNewsAsync 会重新从 Steam 拉取
    /// </summary>
    public static void ClearCache()
    {
        Cache.Clear();
        Logger.Info("资讯缓存已清除");
    }

    private static List<NewsItem> ParseSteamRss(string xml)
    {
        var doc = XDocument.Parse(xml);

        return doc.Descendants("item")
            .Select(item => new NewsItem
            {
                Title = item.Element("title")?.Value?.Trim() ?? "无标题",
                Content = StripHtmlTags(
                    item.Element("description")?.Value?.Trim() ?? "暂无内容"),
                Url = item.Element("link")?.Value?.Trim() ?? string.Empty,
                PublishDate = TryParseDate(
                    item.Element("pubDate")?.Value),
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

    [System.Text.RegularExpressions.GeneratedRegex(@"</?(?:br|p|div|li|h[1-6])(?:\s[^>]*)?>")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();

    [System.Text.RegularExpressions.GeneratedRegex("<[^>]*>")]
    private static partial System.Text.RegularExpressions.Regex MyRegex1();

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\S\n]+")]
    private static partial System.Text.RegularExpressions.Regex MyRegex2();

    [System.Text.RegularExpressions.GeneratedRegex(@"\n{3,}")]
    private static partial System.Text.RegularExpressions.Regex MyRegex3();
}