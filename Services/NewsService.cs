using System.Net.Http;
using System.Xml.Linq;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
/// 从 Steam 新闻 RSS 获取游戏资讯
/// </summary>
public class NewsService : INewsService
{
    private readonly HttpClient _httpClient;

    public NewsService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ChipLauncher/1.0");
    }

    public async Task<List<NewsItem>> GetNewsAsync(string appId)
    {
        var url = $"https://store.steampowered.com/feeds/news/app/{appId}/";

        try
        {
            Logger.Info($"正在获取 Steam 资讯: AppId={appId}");
            var xml = await _httpClient.GetStringAsync(url);
            var items = ParseSteamRss(xml);
            Logger.Info($"获取资讯成功: {items.Count} 条");
            return items;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error($"网络请求失败: {url}", ex);
            return new List<NewsItem>();
        }
        catch (Exception ex)
        {
            Logger.Error($"解析资讯失败: {ex.Message}", ex);
            return new List<NewsItem>();
        }
    }

    /// <summary>
    /// 解析 Steam RSS XML 为 NewsItem 列表
    /// </summary>
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

    /// <summary>
    /// 去除 HTML 标签，保留纯文本
    /// </summary>
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

    /// <summary>
    /// 尝试解析日期字符串，失败则返回当前时间
    /// </summary>
    private static DateTime TryParseDate(string? dateStr)
    {
        if (DateTime.TryParse(dateStr, out var result))
            return result;
        return DateTime.UtcNow;
    }
}
