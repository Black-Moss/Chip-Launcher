namespace ChipLauncher.Models;

/// <summary>
///     游戏资讯项数据模型
/// </summary>
public class NewsItem
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public DateTime PublishDate { get; init; }
}