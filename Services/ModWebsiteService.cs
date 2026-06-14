using System.Net.Http;
using System.Text.Json;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
///     模组托管服务器数据服务 — 从国内托管服务器获取模组列表和下载数据
/// </summary>
public static partial class ModWebsiteService
{
    private static readonly HttpClient _httpClient;
    private static readonly HttpClient _downloadClient;

    /// <summary>
    ///     托管服务器基础 URL（可在运行时切换）
    /// </summary>
    public static string BaseUrl { get; set; } = "https://mods.example.com";

    static ModWebsiteService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ChipLauncher/1.0 (+https://github.com/ChipLauncher)");

        _downloadClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(180)
        };
        _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ChipLauncher/1.0 (+https://github.com/ChipLauncher)");
    }

    /// <summary>
    ///     从托管服务器获取模组列表
    /// </summary>
    /// <param name="query">搜索关键字（可选）</param>
    /// <param name="category">分类筛选（可选）</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <returns>模组列表及分页信息，失败返回空列表</returns>
    public static async Task<ModListResponse?> FetchModsAsync(
        string? query = null,
        string? category = null,
        int page = 1)
    {
        try
        {
            var url = BuildUrl(query, category, page);
            Logger.Info($"获取模组列表: {url}");
            var json = await _httpClient.GetStringAsync(url);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var result = JsonSerializer.Deserialize<ModListResponse>(json, options);
            Logger.Info($"解析到 {result?.Items.Count ?? 0} 个模组");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"获取模组列表失败: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    ///     下载模组文件
    /// </summary>
    /// <param name="downloadUrl">模组下载链接</param>
    /// <param name="progress">下载进度回调（0-100）</param>
    /// <returns>文件字节数组，失败返回 null</returns>
    public static async Task<byte[]?> DownloadModAsync(
        string downloadUrl,
        IProgress<double>? progress = null)
    {
        try
        {
            Logger.Info($"开始下载模组: {downloadUrl}");

            using var response = await _downloadClient.GetAsync(
                downloadUrl, HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            if (totalBytes <= 0)
            {
                // 无法获取总大小，直接下载全部
                return await response.Content.ReadAsByteArrayAsync();
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var memoryStream = new MemoryStream();
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await memoryStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                progress?.Report((double)totalRead / totalBytes * 100);
            }

            Logger.Info($"模组下载完成: {totalRead} 字节");
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Error($"下载模组失败: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    ///     构建 API 请求 URL
    /// </summary>
    private static string BuildUrl(string? query, string? category, int page)
    {
        var baseUrl = BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/mods?page={page}";

        if (!string.IsNullOrWhiteSpace(query))
            url += $"&keyword={Uri.EscapeDataString(query)}";

        if (!string.IsNullOrWhiteSpace(category) &&
            !string.Equals(category, "全部", StringComparison.OrdinalIgnoreCase))
            url += $"&category={Uri.EscapeDataString(category)}";

        return url;
    }
}
