using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
/// 游戏资讯服务接口
/// </summary>
public interface INewsService
{
    /// <summary>获取资讯列表，失败返回 null</summary>
    Task<List<NewsItem>?> GetNewsAsync(string appId);
}
