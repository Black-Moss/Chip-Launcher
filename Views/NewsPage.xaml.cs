using System.Windows.Controls;
using ChipLauncher.Models;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
/// 游戏资讯页面 - 自动从 Steam RSS 获取新闻
/// </summary>
public partial class NewsPage : UserControl
{
    private readonly INewsService _newsService;

    public NewsPage()
    {
        InitializeComponent();
        _newsService = new NewsService();
        Loaded += async (_, _) => await LoadNewsAsync();
    }

    private async Task LoadNewsAsync()
    {
        var items = await _newsService.GetNewsAsync("4576490");
        NewsListBox.ItemsSource = items;

        if (items.Count > 0)
        {
            NewsListBox.SelectedIndex = 0;
        }
        else
        {
            ContentText.Text = "暂无资讯，请检查网络连接。";
        }

        NewsListBox.SelectionChanged += (_, _) =>
        {
            if (NewsListBox.SelectedItem is NewsItem item)
            {
                TitleText.Text = item.Title;
                DateText.Text = item.PublishDate.ToString("yyyy-MM-dd HH:mm");
                ContentText.Text = item.Content;
            }
        };
    }
}
