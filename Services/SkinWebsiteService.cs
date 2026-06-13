using System.Net.Http;
using System.Text.RegularExpressions;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
///     皮肤网站数据服务 — 从 <c>https://skin.cat-bot.de/</c> 获取并解析真实皮肤数据
/// </summary>
public static partial class SkinWebsiteService
{
    private static readonly HttpClient _httpClient;
    private static readonly HttpClient _downloadClient;
    private const string BaseUrl = "https://skin.cat-bot.de";

    /// <summary>皮肤站基础 URL，供外部组件拼接完整图片地址</summary>
    public static string SiteBaseUrl => BaseUrl;

    /// <summary>公开的下载用 HttpClient（供外部直接调用，如图库图片加载）</summary>
    public static HttpClient DownloadClient => _downloadClient;

    /// <summary>类别 ID 与显示名称的映射</summary>
    public static readonly Dictionary<int, string> Categories = new()
    {
        [0] = "全部",
        [1] = "Funny",
        [2] = "Gunsaw",
        [3] = "Species",
        [4] = "OC",
        [5] = "Recolor",
        [12] = "Crossover",
        [13] = "Other",
        [14] = "Human"
    };

    static SkinWebsiteService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ChipLauncher/1.0 (+https://github.com/ChipLauncher)");

        // 专门用于大文件下载的客户端，超时更宽松
        _downloadClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(180)
        };
        _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ChipLauncher/1.0 (+https://github.com/ChipLauncher)");
    }

    /// <summary>
    ///     从皮肤站获取皮肤列表。
    /// </summary>
    /// <param name="query">搜索关键字（可选）</param>
    /// <param name="categoryId">分类 ID（0=全部，可选）</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="showSuggestive">是否显示暗示性内容</param>
    /// <returns>皮肤列表，失败返回空列表</returns>
    public static async Task<List<SkinDownloadItem>> FetchSkinsAsync(
        string? query = null,
        int categoryId = 0,
        int page = 1,
        bool showSuggestive = false)
    {
        try
        {
            var url = BuildUrl(query, categoryId, page, showSuggestive);
            Logger.Info($"获取皮肤列表: {url}");
            var html = await _httpClient.GetStringAsync(url);
            var items = ParseSkinItems(html);
            Logger.Info($"解析到 {items.Count} 个皮肤");
            return items;
        }
        catch (Exception ex)
        {
            Logger.Error($"获取皮肤列表失败: {ex.Message}", ex);
            return [];
        }
    }

    /// <summary>
    ///     获取总皮肤数和总页数（从页面顶部的统计文字解析）
    /// </summary>
    public static async Task<(int totalSkins, int totalPages)> GetPaginationInfoAsync(
        string? query = null,
        int categoryId = 0,
        bool showSuggestive = false)
    {
        try
        {
            var url = BuildUrl(query, categoryId, 1, showSuggestive);
            var html = await _httpClient.GetStringAsync(url);
            return ParsePagination(html);
        }
        catch
        {
            return (0, 1);
        }
    }

    /// <summary>
    ///     下载指定 ID 的皮肤文件（返回字节数组）。
    ///     使用独立的下载 HttpClient（180 秒超时）。
    /// </summary>
    public static async Task<byte[]?> DownloadSkinAsync(int skinId)
    {
        try
        {
            var url = $"{BaseUrl}/d/{skinId}";
            Logger.Info($"下载皮肤 #{skinId} 文件: {url}");
            return await _downloadClient.GetByteArrayAsync(url);
        }
        catch (Exception ex)
        {
            Logger.Error($"下载皮肤 #{skinId} 失败: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    ///     下载并缓存指定皮肤的缩略图。
    ///     返回本地缓存路径，失败返回 null。
    /// </summary>
    public static async Task<string?> CacheThumbnailAsync(SkinDownloadItem skin)
    {
        if (string.IsNullOrEmpty(skin.ThumbnailUrl)) return null;

        // 已缓存则直接返回
        if (SkinCache.HasCache(skin.Name))
            return SkinCache.GetLocalPath(skin.Name);

        try
        {
            // 缩略图 URL 是相对路径
            var imgUrl = skin.ThumbnailUrl.StartsWith("http")
                ? skin.ThumbnailUrl
                : $"{BaseUrl}{skin.ThumbnailUrl}";

            var data = await _downloadClient.GetByteArrayAsync(imgUrl);
            await SkinCache.CacheThumbnailAsync(skin.Name, data);
            return SkinCache.GetLocalPath(skin.Name);
        }
        catch (Exception ex)
        {
            Logger.Warn($"缓存缩略图失败 [{skin.Name}]: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     构建皮肤站的搜索 URL
    /// </summary>
    public static string GetSearchUrl(string? query = null, int? categoryId = null, int page = 1)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(query))
            parts.Add($"q={Uri.EscapeDataString(query)}");
        if (categoryId is > 0)
            parts.Add($"category={categoryId}");
        if (page > 1)
            parts.Add($"page={page}");

        return parts.Count > 0
            ? $"{BaseUrl}/?{string.Join("&", parts)}"
            : BaseUrl;
    }

    // ──────────────── 内部方法 ────────────────

    /// <summary>构建请求 URL</summary>
    private static string BuildUrl(string? query, int categoryId, int page, bool showSuggestive)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(query))
            parts.Add($"q={Uri.EscapeDataString(query)}");
        if (categoryId > 0)
            parts.Add($"category={categoryId}");
        if (page > 1)
            parts.Add($"page={page}");
        if (showSuggestive)
            parts.Add("show_suggestive=1");

        return parts.Count > 0
            ? $"{BaseUrl}/?{string.Join("&", parts)}"
            : BaseUrl;
    }

    /// <summary>解析 HTML 中的皮肤列表项</summary>
    internal static List<SkinDownloadItem> ParseSkinItems(string html)
    {
        var items = new List<SkinDownloadItem>();

        // 匹配每个 <li id="skin-{id}" ... data-skin-id=... data-skin-name=... data-owner-name=...>
        var skinItemRegex = SkinItemRegex();
        var matches = skinItemRegex.Matches(html);

        Logger.Info($"正则匹配到 {matches.Count} 个皮肤项");

        foreach (Match match in matches)
        {
            try
            {
                var id = int.Parse(match.Groups["id"].Value);
                var name = match.Groups["name"].Value.Trim();
                var author = match.Groups["author"].Value.Trim();

                // 提取缩略图 URL
                var previewMatch = PreviewRegex().Match(match.Value);
                var thumbnailUrl = previewMatch.Success
                    ? previewMatch.Groups["src"].Value
                    : null;

                // 提取文件大小和下载量
                var sizeLine = SizeLineRegex().Match(match.Value);
                var (size, downloads) = ParseSizeAndDownloads(sizeLine.Success
                    ? sizeLine.Groups["text"].Value
                    : "");

                // 提取分类
                var categoryMatch = CategoryRegex().Match(match.Value);
                var category = categoryMatch.Success
                    ? categoryMatch.Groups["cat"].Value.Trim()
                    : null;

                // 提取上传者（从 "Uploaded by {name}" 文本）
                var uploaderMatch = UploaderRegex().Match(match.Value);
                if (uploaderMatch.Success && string.IsNullOrEmpty(author))
                    author = uploaderMatch.Groups["name"].Value.Trim();

                // 提取画廊图片（carousel 中的多张图片）
                var galleryUrls = new List<string>();
                var galleryMatch = GalleryRegex().Match(match.Value);
                if (galleryMatch.Success)
                {
                    var carouselHtml = galleryMatch.Groups["gallery"].Value;
                    foreach (Match imgMatch in ImgSrcRegex().Matches(carouselHtml))
                    {
                        var src = imgMatch.Groups["src"].Value;
                        if (!string.IsNullOrEmpty(src) && !src.Equals(thumbnailUrl, StringComparison.OrdinalIgnoreCase))
                            galleryUrls.Add(src);
                    }
                }

                // 检查本地是否已下载（按 ID 匹配目录名 Skin_{Id}）
                var isDownloaded = LocalSkinReader.IsSkinDownloaded(id);

                var item = new SkinDownloadItem
                {
                    Id = id,
                    Name = name,
                    Author = string.IsNullOrEmpty(author) ? null : author,
                    Downloads = downloads,
                    Size = size,
                    UploadTime = DateTime.MinValue,
                    ThumbnailUrl = thumbnailUrl,
                    Category = category,
                    IsDownloaded = isDownloaded,
                    GalleryUrls = galleryUrls.Count > 0 ? galleryUrls : null
                };

                items.Add(item);
            }
            catch (Exception ex)
            {
                Logger.Warn($"解析皮肤项失败: {ex.Message}");
            }
        }

        return items;
    }

    /// <summary>解析统计文字中的总数和页数</summary>
    internal static (int totalSkins, int totalPages) ParsePagination(string html)
    {
        var totalSkins = 0;
        var totalPages = 1;

        // 匹配 "289 skins loaded." 或 "1 result for "TEST"." 等
        var statsMatch = StatsRegex().Match(html);
        if (statsMatch.Success)
        {
            var numStr = statsMatch.Groups["num"].Value;
            int.TryParse(numStr, out totalSkins);
        }

        // 计算页数（每页约 20 个）
        if (totalSkins > 0)
            totalPages = (int)Math.Ceiling(totalSkins / 20.0);

        return (totalSkins, totalPages);
    }

    /// <summary>
    ///     解析文件大小和下载量文字。
    ///     格式示例："18.7 KB • 8 downloads"
    /// </summary>
    private static (long size, long downloads) ParseSizeAndDownloads(string text)
    {
        var size = 0L;
        var downloads = 0L;

        if (string.IsNullOrEmpty(text)) return (0, 0);

        // 1. 解析文件大小 — 用 SizeNumberRegex 从文本中提取
        var sizeMatch = SizeNumberRegex().Match(text);
        if (sizeMatch.Success)
        {
            if (double.TryParse(sizeMatch.Groups["num"].Value, out var value))
            {
                var unit = sizeMatch.Groups["unit"].Value.ToUpperInvariant();
                size = unit switch
                {
                    "B" => (long)value,
                    "KB" => (long)(value * 1024),
                    "MB" => (long)(value * 1024 * 1024),
                    "GB" => (long)(value * 1024 * 1024 * 1024),
                    _ => (long)value
                };
            }
        }

        // 2. 解析下载次数 — 查找 "N downloads" 模式
        var downloadMatch = DownloadCountRegex().Match(text);
        if (downloadMatch.Success && long.TryParse(downloadMatch.Groups["num"].Value, out var d))
            downloads = d;

        return (size, downloads);
    }

    // ──────────────── 正则表达式 ────────────────

    [GeneratedRegex(
        @"<li[^>]*\bid=""skin-(?<id>\d+)""[^>]*(?:data-skin-id=""(?:\d+)"")[^>]*data-skin-name=""(?<name>[^""]*)""[^>]*data-owner-name=""(?<author>[^""]*)""[^>]*>(?<content>.*?)</li>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SkinItemRegex();

    [GeneratedRegex(@"<img[^>]*src=\""(?<src>/media/images/skins/[^""]+)\""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PreviewRegex();

    [GeneratedRegex(@"<div\s+class=""skin-size""[^>]*>(?<text>[^<]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SizeLineRegex();

    [GeneratedRegex(@"Uploaded\s+by\s+(?<name>[^<]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UploaderRegex();

    [GeneratedRegex(@"(?<num>\d+)\s+(?:skins?\s+loaded|results?\s+for|skin)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StatsRegex();

    [GeneratedRegex(@"(?<num>\d+)\s+download",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DownloadCountRegex();

    [GeneratedRegex(@"(?<num>[\d.]+)\s*(?<unit>[KMG]?B)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SizeNumberRegex();

    [GeneratedRegex(@"<div\s+class=""skin-category""[^>]*>\s*<span[^>]*>(?<cat>[^<]+)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CategoryRegex();

    [GeneratedRegex(@"<div\s+id=""thumbnails-(?<id>\d+)""\s+class=""carousel""[^>]*>(?<gallery>.*?)</div>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GalleryRegex();

    [GeneratedRegex(@"<img[^>]*src=\""(?<src>[^""]+)\""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ImgSrcRegex();
}
