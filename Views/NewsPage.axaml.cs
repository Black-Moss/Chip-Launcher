using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChipLauncher.Models;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
/// 游戏资讯页面 - 从 Steam RSS 获取新闻（支持加载状态 + 失败重试）
/// </summary>
public partial class NewsPage : UserControl
{
    private readonly INewsService _newsService;
    private bool _selectionHooked;
    private string _currentUrl = string.Empty;

    public NewsPage()
    {
        InitializeComponent();
        _newsService = new NewsService();

        Loaded += async (_, _) => await OnPageLoadedAsync();
        OpenUrlButton.Click += OnOpenUrlClick;
        // 重试按钮：通过 FindControl 查找（Avalonia 中 x:Name 在模板内可能不可直接访问）
        RetryButton.Click += OnRetryClick;
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
        NewsListBox.ItemsSource = items;

        if (items.Count > 0)
        {
            NewsListBox.SelectedIndex = 0;
        }
        else
        {
            ContentText.Text = "暂无资讯。";
        }

        if (_selectionHooked) return;
        _selectionHooked = true;
        NewsListBox.SelectionChanged += (_, _) => OnSelectionChanged();
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
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Error("打开原文失败", ex);
        }
    }
}
