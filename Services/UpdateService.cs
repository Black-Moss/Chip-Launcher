using System.Diagnostics;
using System.Text.Json;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
///     更新检查服务 — 从 GitHub Releases API 获取最新版本号
/// </summary>
public static class UpdateService
{
    private const string ApiUrl =
        "https://api.github.com/repos/Black-Moss/Chip-Launcher/releases/latest";

    /// <summary>
    ///     检查 GitHub 最新 Release 版本，返回 (最新版本号, 下载页 URL)；
    ///     若网络异常或版本相同则返回 null。
    /// </summary>
    public static async Task<(string Version, string Url)?> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // GitHub API 要求 User-Agent
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ChipLauncher/1.0");

            var json = await client.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 取 tag_name，格式如 "v1.1.0"
            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName)) return null;

            // 去掉前缀 v
            var latestVersion = tagName.TrimStart('v', 'V');

            // 获取当前版本
            var currentVersion = new LauncherInfo().Version;

            // 比较版本
            if (!IsNewerVersion(latestVersion, currentVersion))
                return null;

            // 取 html_url (Release 页)
            var url = root.GetProperty("html_url").GetString() ??
                      "https://github.com/Black-Moss/Chip-Launcher/releases/latest";

            return (latestVersion, url);
        }
        catch (Exception ex)
        {
            Logger.Warn($"检查更新失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>比较版本号，latest > current 返回 true</summary>
    private static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest, out var latestV) &&
            Version.TryParse(current, out var currentV))
        {
            return latestV > currentV;
        }

        // 解析失败时按字符串比较
        return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
