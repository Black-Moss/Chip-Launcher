namespace ChipLauncher.Models;

/// <summary>
///     游戏资讯项数据模型
/// </summary>
public class NewsItem
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishDate { get; set; }
}