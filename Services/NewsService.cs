using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
/// 从 Steam 新闻 RSS 获取游戏资讯（支持内存缓存 + 自动重试）
/// </summary>
public class NewsService : INewsService
{
    private readonly HttpClient _httpClient;
    private const int MaxRetries = 3;

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
        ServicePointManager.SecurityProtocol =
            SecurityProtocolType.Tls | SecurityProtocolType.Tls11 |
            SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
        ServicePointManager.ServerCertificateValidationCallback =
            (_, _, _, _) => true;

        var handler = new HttpClientHandler
        {
            // 允许所有证书（解决部分 Windows 环境 SSL 问题）
            ServerCertificateCustomValidationCallback =
                (_, _, _, _) => true,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/125.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept",
            "application/rss+xml,application/xml,text/xml,*/*");
        _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
    }

    public async Task<List<NewsItem>> GetNewsAsync(string appId)
    {
        // 1. 检查缓存是否有效
        if (Cache.TryGetValue(appId, out var cached) && cached.IsValid)
        {
            Logger.Info($"使用缓存资讯: AppId={appId} ({cached.Items.Count} 条)");
            return cached.Items;
        }

        // 2. 缓存失效 → 发起 HTTP 请求
        var url = $"https://store.steampowered.com/feeds/news/app/{appId}/";

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                Logger.Info($"正在获取 Steam 资讯: AppId={appId} (第 {attempt} 次尝试)");
                var xml = await _httpClient.GetStringAsync(url);
                var items = ParseSteamRss(xml);

                // 更新缓存
                Cache[appId] = new CacheEntry { Items = items };
                Logger.Info($"获取资讯成功: {items.Count} 条 (已缓存 {CacheDuration.TotalMinutes} 分钟)");
                return items;
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                Logger.Warn($"第 {attempt} 次请求失败，即将重试: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
            catch (HttpRequestException ex)
            {
                Logger.Error($"网络请求失败（已重试 {MaxRetries} 次）: {url}", ex);
                return new List<NewsItem>();
            }
            catch (TaskCanceledException)
            {
                Logger.Warn("请求超时");
                if (attempt >= MaxRetries) return new List<NewsItem>();
            }
            catch (Exception ex)
            {
                Logger.Error($"解析资讯失败: {ex.Message}", ex);
                return new List<NewsItem>();
            }
        }

        return new List<NewsItem>();
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

        var text = System.Text.RegularExpressions.Regex
            .Replace(html, "<[^>]*>", " ");
        text = System.Text.RegularExpressions.Regex
            .Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private static DateTime TryParseDate(string? dateStr)
    {
        if (DateTime.TryParse(dateStr, out var result))
            return result;
        return DateTime.UtcNow;
    }
}
