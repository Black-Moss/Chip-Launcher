using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
/// 游戏资讯服务接口
/// </summary>
public interface INewsService
{
    Task<List<NewsItem>> GetNewsAsync(string appId);
}
