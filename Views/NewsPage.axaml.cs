using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChipLauncher.Models;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
///     游戏资讯页面 - 从 Steam RSS 获取新闻（支持加载状态 + 失败重试）
/// </summary>
public partial class NewsPage : UserControl
{
    private readonly INewsService _newsService;
    private List<NewsItem>? _allNews;
    private string _currentUrl = string.Empty;
    private bool _selectionHooked;

    public NewsPage()
    {
        InitializeComponent();
        _newsService = new NewsService();

        Loaded += async (_, _) => await OnPageLoadedAsync();
        OpenUrlButton.Click += OnOpenUrlClick;
        // 重试按钮
        RetryButton.Click += OnRetryClick;
        RefreshButton.Click += OnRefreshClick;
        NewsSearchBox.TextChanged += OnNewsSearchChanged;
    }

    /// <summary>搜索框文本变化 → 过滤新闻列表</summary>
    private void OnNewsSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_allNews == null) return;

        var keyword = NewsSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            NewsListBox.ItemsSource = _allNews;
            return;
        }

        var filtered = _allNews
            .Where(n => n.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || n.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
        NewsListBox.ItemsSource = filtered;
    }

    /// <summary>页面加载时：优先使用缓存，无缓存则发起请求</summary>
    private async Task OnPageLoadedAsync()
    {
        var cached = NewsService.TryGetCached("4576490");
        if (cached != null)
        {
            BindNews(cached);
            return;
        }

        await FetchNewsAsync();
    }

    /// <summary>发起 HTTP 请求获取资讯</summary>
    private async Task FetchNewsAsync()
    {
        ShowOverlay(LoadingOverlay, true);
        ShowOverlay(ErrorOverlay, false);

        var result = await _newsService.GetNewsAsync("4576490");

        ShowOverlay(LoadingOverlay, false);

        if (result == null)
        {
            ShowOverlay(ErrorOverlay, true);
            return;
        }

        BindNews(result);
    }

    /// <summary>绑定新闻数据到界面</summary>
    private void BindNews(List<NewsItem> items)
    {
        _allNews = items;
        // 如果有搜索关键词，应用过滤
        var keyword = NewsSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            NewsListBox.ItemsSource = items;
        }
        else
        {
            var filtered = items
                .Where(n => n.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                            || n.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
            NewsListBox.ItemsSource = filtered;
        }

        // 先注册事件，再设置选中项，确保第一次选中时也能触发详情显示
        if (!_selectionHooked)
        {
            _selectionHooked = true;
            NewsListBox.SelectionChanged += (_, _) => OnSelectionChanged();
        }

        if (items.Count > 0)
            NewsListBox.SelectedIndex = 0;
        else
            ContentText.Text = "暂无资讯。";
    }

    /// <summary>列表选中项改变 → 更新详情 + 原文按钮</summary>
    private void OnSelectionChanged()
    {
        if (NewsListBox.SelectedItem is not NewsItem item) return;

        TitleText.Text = item.Title;
        DateText.Text = item.PublishDate.ToString("yyyy-MM-dd HH:mm");
        ContentText.Text = item.Content;

        _currentUrl = item.Url;
        OpenUrlButton.IsVisible = !string.IsNullOrEmpty(_currentUrl);
    }

    /// <summary>显示/隐藏遮罩（同时隐藏/显示内容面板）</summary>
    private void ShowOverlay(Control overlay, bool show)
    {
        overlay.IsVisible = show;
        ContentPanel.IsVisible = !show;
    }

    /// <summary>重试按钮点击</summary>
    private async void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            ShowOverlay(ErrorOverlay, false);
            NewsService.ClearCache();
            await FetchNewsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"重试按钮出错: {ex.Message}");
        }
    }

    /// <summary>刷新按钮点击 — 清除缓存后重新拉取</summary>
    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            NewsService.ClearCache();
            await FetchNewsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"刷新资讯出错: {ex.Message}");
        }
    }

    /// <summary>在浏览器中打开原文</summary>
    private void OnOpenUrlClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentUrl)) return;

        try
        {
            Logger.Info($"打开原文链接: {_currentUrl}");
            Process.Start(new ProcessStartInfo
            {
                FileName = _currentUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("打开原文失败", ex);
        }
    }
}