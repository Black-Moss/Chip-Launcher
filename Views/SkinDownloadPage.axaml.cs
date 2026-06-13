using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ChipLauncher.Models;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
///     皮肤下载页面 — 从 <c>https://skin.cat-bot.de/</c> 浏览、搜索、下载皮肤
///     支持瀑布流/列表两种显示模式，搜索皮肤/作者，按多种方式排序，分类过滤。
/// </summary>
public partial class SkinDownloadPage : UserControl
{
    /// <summary>布尔值反转转换器（IsDownloading → 按钮隐藏）</summary>
    public static readonly IValueConverter BoolInvertConverter =
        new FuncConverter<bool, bool>(v => !v);
    private SkinSortMode _currentSort = SkinSortMode.Downloads;
    private SkinDisplayMode _displayMode = SkinDisplayMode.Waterfall;
    private int _currentCategoryId;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalSkins;
    private bool _loaded;
    private bool _isLoading;

    // ---- 缩略图预览状态 ----
    private SkinDownloadItem? _previewItem;
    private int _previewIndex;
    private readonly List<Bitmap?> _previewBitmaps = [];

    public SkinDownloadPage()
    {
        InitializeComponent();
        InitializeCategoryFilter();
        _ = LoadSkinsAsync();
        _loaded = true;
    }

    /// <summary>
    ///     每次控件被附加到可视化树时（包括 SukiUI 标签切换），刷新所有皮肤的本地下载状态。
    ///     这样用户在 SkinPage 删除皮肤后切回此页，下载按钮能正确显示为"可下载"。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshDownloadStatus();
    }

    /// <summary>遍历当前列表，重新检查每个皮肤的本地下载状态</summary>
    private void RefreshDownloadStatus()
    {
        foreach (var item in SkinItems)
        {
            item.IsDownloaded = LocalSkinReader.IsSkinDownloaded(item.Id);
        }
        RefreshDisplay();
    }

    public ObservableCollection<SkinDownloadItem> SkinItems { get; } = [];

    private List<SkinDownloadItem> FilteredItems => ApplySearchAndSort(SkinItems);

    private void InitializeCategoryFilter()
    {
        CategoryFilterComboBox.Items.Clear();
        foreach (var kv in SkinWebsiteService.Categories)
        {
            CategoryFilterComboBox.Items.Add(new KeyValuePair<int, string>(kv.Key, kv.Value));
        }
        CategoryFilterComboBox.SelectedIndex = 0;
    }

    /// <summary>从皮肤站异步加载数据</summary>
    private async Task LoadSkinsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            EmptyHint.Text = "正在加载皮肤数据...";
            EmptyHint.IsVisible = SkinItems.Count == 0;
            LoadingIndicator.IsVisible = true;

            var query = SearchTextBox.Text?.Trim();
            var items = await SkinWebsiteService.FetchSkinsAsync(query, _currentCategoryId, _currentPage);
            var (totalSkins, totalPages) = await SkinWebsiteService.GetPaginationInfoAsync(query, _currentCategoryId);

            _totalSkins = totalSkins > 0 ? totalSkins : items.Count;
            _totalPages = totalPages > 0 ? totalPages : 1;

            SkinItems.Clear();
            foreach (var item in items)
            {
                SkinItems.Add(item);
                // 异步缓存缩略图并加载到 UI
                _ = LoadThumbnailAsync(item);
            }

            RefreshDisplay();
            UpdateStats();
            UpdatePagination();

            // 通知
            AppNotification.Show($"已加载 {items.Count} 个皮肤");
        }
        catch (Exception ex)
        {
            Logger.Error($"加载皮肤数据失败: {ex.Message}", ex);
            EmptyHint.Text = "加载皮肤数据失败，请检查网络后重试";
            EmptyHint.IsVisible = SkinItems.Count == 0;
            AppNotification.Show("加载皮肤数据失败", NotificationType.Error);
        }
        finally
        {
            _isLoading = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    /// <summary>异步加载缩略图：先尝试缓存，然后创建 Bitmap 设置到 ThumbnailImage</summary>
    private static async Task LoadThumbnailAsync(SkinDownloadItem item)
    {
        try
        {
            string? localPath = null;

            // 1. 检查是否已有缓存
            if (SkinCache.HasCache(item.Name))
            {
                localPath = SkinCache.GetLocalPath(item.Name);
            }
            // 2. 有远程 URL 则下载并缓存
            else if (!string.IsNullOrEmpty(item.ThumbnailUrl))
            {
                localPath = await SkinWebsiteService.CacheThumbnailAsync(item);
            }

            // 3. 从本地路径创建 Bitmap
            if (localPath != null && File.Exists(localPath))
            {
                var bitmap = new Bitmap(localPath);
                item.ThumbnailImage = bitmap;
            }

            // 4. 异步加载画廊图片（如果有）
            if (item.GalleryUrls is { Count: > 0 })
            {
                _ = LoadGalleryImagesAsync(item);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"加载缩略图失败 [{item.Name}]: {ex.Message}");
        }
    }

    /// <summary>异步加载画廊图片（carousel 中的多张图片）</summary>
    private static async Task LoadGalleryImagesAsync(SkinDownloadItem item)
    {
        try
        {
            var images = new List<object?>();
            foreach (var url in item.GalleryUrls!)
            {
                try
                {
                    var imgUrl = url.StartsWith("http") ? url : $"https://skin.cat-bot.de{url}";
                    var data = await SkinWebsiteService.DownloadClient.GetByteArrayAsync(imgUrl);
                    using var ms = new MemoryStream(data);
                    images.Add(new Bitmap(ms));
                }
                catch
                {
                    // 单张图片加载失败不影响其他图片
                }
            }
            if (images.Count > 0)
                item.GalleryImages = images;
        }
        catch (Exception ex)
        {
            Logger.Warn($"加载画廊图片失败 [{item.Name}]: {ex.Message}");
        }
    }

    private void RefreshDisplay()
    {
        var filtered = FilteredItems;

        WaterfallItemsControl.ItemsSource = filtered;
        SkinListBox.ItemsSource = filtered;

        EmptyHint.IsVisible = filtered.Count == 0 && SkinItems.Count > 0;
        if (filtered.Count == 0 && SkinItems.Count > 0)
            EmptyHint.Text = "没有匹配的皮肤";

        UpdateStats();
    }

    private List<SkinDownloadItem> ApplySearchAndSort(IEnumerable<SkinDownloadItem> items)
    {
        var query = SearchTextBox.Text?.Trim() ?? string.Empty;
        IEnumerable<SkinDownloadItem> result = items;

        if (!string.IsNullOrEmpty(query))
        {
            var lower = query.ToLowerInvariant();
            result = result.Where(s =>
                s.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                (s.Author?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        result = _currentSort switch
        {
            SkinSortMode.NameAZ => result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
            SkinSortMode.NameZA => result.OrderByDescending(s => s.Name, StringComparer.OrdinalIgnoreCase),
            SkinSortMode.Id => result.OrderBy(s => s.Id),
            SkinSortMode.Downloads => result.OrderByDescending(s => s.Downloads),
            SkinSortMode.Size => result.OrderByDescending(s => s.Size),
            SkinSortMode.UploadTime => result.OrderByDescending(s => s.UploadTime),
            _ => result.OrderByDescending(s => s.Downloads)
        };

        return result.ToList();
    }

    private void UpdateStats()
    {
        if (_totalSkins > 0)
            StatTotalSkins.Text = $"共 {_totalSkins} 个皮肤";
        else
            StatTotalSkins.Text = $"共 {SkinItems.Count} 个皮肤";

        var localCount = LocalSkinReader.GetLocalSkinCount();
        StatLocalSkins.Text = $"本地：{localCount}";
    }

    private void UpdatePagination()
    {
        // 无底滚动模式：隐藏分页栏
        if (AppConfig.Instance.InfiniteScroll)
        {
            PaginationPanel.IsVisible = false;
            return;
        }

        if (_totalPages <= 1)
        {
            PaginationPanel.IsVisible = false;
            return;
        }

        PaginationPanel.IsVisible = true;
        PageInfoText.Text = $"{_currentPage} / {_totalPages}";
        BtnPrevPage.IsEnabled = _currentPage > 1;
        BtnNextPage.IsEnabled = _currentPage < _totalPages;
    }

    private void SwitchDisplayMode(SkinDisplayMode mode)
    {
        _displayMode = mode;

        WaterfallScrollViewer.IsVisible = mode == SkinDisplayMode.Waterfall;
        ListScrollViewer.IsVisible = mode == SkinDisplayMode.List;

        BtnWaterfallMode.Classes.Remove("Accent");
        BtnListMode.Classes.Remove("Accent");

        if (mode == SkinDisplayMode.Waterfall)
            BtnWaterfallMode.Classes.Add("Accent");
        else
            BtnListMode.Classes.Add("Accent");
    }

    /// <summary>远程搜索（从皮肤站重新拉取）</summary>
    private async void TriggerRemoteSearch()
    {
        if (!_loaded || _isLoading) return;
        _currentPage = 1;
        await LoadSkinsAsync();
    }

    // ============ 事件处理 ============

    /// <summary>搜索文本框按回车键 → 远程搜索</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TriggerRemoteSearch();
        }
    }

    /// <summary>搜索文本变化 → 不做任何事（仅 Enter / 搜索按钮触发远程搜索）</summary>
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        // 仅本地过滤不再执行，改用远程搜索
    }

    /// <summary>点击搜索按钮 → 远程搜索</summary>
    private void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        TriggerRemoteSearch();
    }

    /// <summary>排序选项变化</summary>
    private void OnSortSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _currentSort = SortComboBox.SelectedIndex switch
        {
            0 => SkinSortMode.NameAZ,
            1 => SkinSortMode.NameZA,
            2 => SkinSortMode.Id,
            3 => SkinSortMode.Downloads,
            4 => SkinSortMode.Size,
            5 => SkinSortMode.UploadTime,
            _ => SkinSortMode.Downloads
        };
        RefreshDisplay();
    }

    /// <summary>切换瀑布流/列表显示模式</summary>
    private void OnToggleDisplayMode(object? sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        if (sender == BtnWaterfallMode)
            SwitchDisplayMode(SkinDisplayMode.Waterfall);
        else if (sender == BtnListMode)
            SwitchDisplayMode(SkinDisplayMode.List);
    }

    /// <summary>分类过滤变化</summary>
    private void OnCategoryFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;

        if (CategoryFilterComboBox.SelectedItem is KeyValuePair<int, string> selected)
        {
            _currentCategoryId = selected.Key;
            _currentPage = 1;
            _ = LoadSkinsAsync();
        }
    }

    /// <summary>上一页</summary>
    private void OnPrevPageClick(object? sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            _ = LoadSkinsAsync();
        }
    }

    /// <summary>下一页</summary>
    private void OnNextPageClick(object? sender, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
            _ = LoadSkinsAsync();
        }
    }

    /// <summary>刷新数据</summary>
    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        _ = LoadSkinsAsync();
    }

    /// <summary>下载皮肤（点击按钮时触发）</summary>
    private async void OnDownloadSkinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SkinDownloadItem skin)
            return;

        if (skin.IsDownloaded)
        {
            AppNotification.Show($"皮肤「{skin.Name}」已下载", NotificationType.Info);
            return;
        }

        if (skin.IsDownloading)
        {
            AppNotification.Show($"皮肤「{skin.Name}」正在下载中…", NotificationType.Info);
            return;
        }

        skin.IsDownloading = true;
        try
        {
            await DownloadAndInstallSkinAsync(skin);
        }
        finally
        {
            skin.IsDownloading = false;
        }
    }

    /// <summary>已下载皮肤的"使用此皮肤"按钮 — 跳转到皮肤管理页</summary>
    private void OnUseSkinClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
        {
            mainWindow.SelectSideMenuItem("Skin");
            AppNotification.Show("已切换到皮肤管理页", NotificationType.Info);
        }
    }

    /// <summary>下载并安装皮肤</summary>
    private async Task DownloadAndInstallSkinAsync(SkinDownloadItem skin)
    {
        try
        {
            Logger.Info($"开始下载皮肤: {skin.Name} (#{skin.Id})");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var skinsDir = Path.Combine(baseDir, "skins");
            Directory.CreateDirectory(skinsDir);

            // 下载皮肤文件
            var data = await SkinWebsiteService.DownloadSkinAsync(skin.Id);
            if (data == null || data.Length == 0)
            {
                AppNotification.Show($"皮肤下载失败: {skin.Name}", NotificationType.Error);
                return;
            }

            // ---- 智能解压：自适应 ZIP 的多种目录结构 ----
            // 将 ZIP 解压到临时目录，然后整理为 skins/{Id}/Body/*.png 的规范格式
            var tempDir = Path.Combine(Path.GetTempPath(), $"ChipLauncher_Skin_{skin.Id}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(skinsDir, $"{skin.Id}.zip");
            try
            {
                await File.WriteAllBytesAsync(zipPath, data);
                ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);
            }
            finally
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }

            // 最终目标目录：skins/{Id}/Body/
            var skinDir = Path.Combine(skinsDir, skin.Id.ToString());
            var bodyDir = Path.Combine(skinDir, "Body");
            Directory.CreateDirectory(bodyDir);

            // 收集所有 .png 文件（皮肤贴图），无论它们在 ZIP 的什么位置
            var pngFiles = Directory.GetFiles(tempDir, "*.png", SearchOption.AllDirectories).ToList();
            if (pngFiles.Count == 0)
            {
                Logger.Warn($"ZIP 内未找到任何 .png 文件，可能不是有效的皮肤包 [{skin.Name}]");
            }
            else
            {
                foreach (var png in pngFiles)
                {
                    var fileName = Path.GetFileName(png);
                    // 跳过缩略图 / 预览图（它们不应混入 Body/）
                    if (fileName.StartsWith("thumb", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("preview", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("thumbnail", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dest = Path.Combine(bodyDir, fileName);
                    // 不覆盖已存在的文件（同名时保留已有版本）
                    if (!File.Exists(dest))
                        File.Copy(png, dest);
                }

                Logger.Info($"已提取 {Directory.GetFiles(bodyDir).Length} 个皮肤贴图到: {bodyDir}");
            }

            // 清理临时目录
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* 忽略清理失败 */ }

            // ---- 保存元数据和缩略图 ----

            // 保存云端元数据到统一文件 skins/skins_metadata.json
            SkinsMetadataService.SaveAndEnsureDir(skin);

            // 缓存云端缩略图
            if (!string.IsNullOrEmpty(skin.ThumbnailUrl))
            {
                await SkinWebsiteService.CacheThumbnailAsync(skin);

                // 额外：将缩略图保存到皮肤目录（skins/{id}/thumb.png），
                // 这样 SkinPage 可直接从本地加载，无需依赖缓存
                try
                {
                    var imgUrl = skin.ThumbnailUrl.StartsWith("http")
                        ? skin.ThumbnailUrl
                        : $"{SkinWebsiteService.SiteBaseUrl}{skin.ThumbnailUrl}";
                    using var dlClient = new HttpClient();
                    var thumbBytes = await dlClient.GetByteArrayAsync(imgUrl);
                    var ext = Path.GetExtension(skin.ThumbnailUrl)?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(ext) || !new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" }.Contains(ext))
                        ext = ".png";
                    var thumbPath = Path.Combine(skinDir, $"thumb{ext}");
                    await File.WriteAllBytesAsync(thumbPath, thumbBytes);
                    Logger.Info($"缩略图已保存到: {thumbPath}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"保存缩略图到皮肤目录失败 [{skin.Name}]: {ex.Message}");
                }
            }

            // 更新状态
            skin.IsDownloaded = true;
            UpdateStats();

            Logger.Info($"皮肤下载完成: {skin.Name}");
            Logger.Info($"保存路径: {skinDir}");
            AppNotification.Show($"皮肤「{skin.Name}」下载完成", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Logger.Error($"下载皮肤失败: {ex.Message}", ex);
            AppNotification.Show($"下载皮肤失败: {skin.Name}", NotificationType.Error);
        }
    }

    /// <summary>在浏览器中打开皮肤站</summary>
    private void OnSearchWebsiteClick(object? sender, RoutedEventArgs e)
    {
        var query = SearchTextBox.Text?.Trim();
        var url = SkinWebsiteService.GetSearchUrl(query, _currentCategoryId > 0 ? _currentCategoryId : null);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开浏览器失败: {ex.Message}");
        }
    }

    // ============ 无底滚动（Infinite Scroll）============

    /// <summary>ScrollViewer 滚动事件 — 滚动到底部时自动加载下一页</summary>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!AppConfig.Instance.InfiniteScroll) return;
        if (_isLoading || _currentPage >= _totalPages) return;

        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer == null) return;

        // 当 ScrollBarMaximum.Y > 0（内容高度超过可视区）且偏移量接近底部时触发
        if (scrollViewer.ScrollBarMaximum.Y > 0 &&
            scrollViewer.Offset.Y >= scrollViewer.ScrollBarMaximum.Y - 60)
        {
            _currentPage++;
            _ = LoadMoreSkinsAsync();
        }
    }

    /// <summary>加载下一页皮肤（追加到现有列表，不清除）</summary>
    private async Task LoadMoreSkinsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            LoadingIndicator.IsVisible = true;

            var query = SearchTextBox.Text?.Trim();
            var items = await SkinWebsiteService.FetchSkinsAsync(query, _currentCategoryId, _currentPage);

            foreach (var item in items)
            {
                SkinItems.Add(item);
                _ = LoadThumbnailAsync(item);
            }

            RefreshDisplay();
            UpdateStats();
            UpdatePagination();

            AppNotification.Show($"已加载更多 {items.Count} 个皮肤");
        }
        catch (Exception ex)
        {
            Logger.Error($"加载更多皮肤失败: {ex.Message}", ex);
            _currentPage--; // 回滚页码
            AppNotification.Show("加载更多皮肤失败", NotificationType.Error);
        }
        finally
        {
            _isLoading = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    // ============ 缩略图预览 Gallery ============

    /// <summary>点击缩略图 → 显示放大预览</summary>
    private void OnThumbnailClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Image image) return;
        var item = image.DataContext as SkinDownloadItem;
        if (item == null) return;

        ShowPreview(item);
    }

    /// <summary>显示皮肤图片预览</summary>
    private void ShowPreview(SkinDownloadItem item)
    {
        _previewItem = item;
        _previewIndex = 0;
        _previewBitmaps.Clear();

        PreviewTitle.Text = item.Name;

        // 收集所有可用图片：缩略图 + 画廊图片
        if (item.ThumbnailImage is Bitmap thumb)
            _previewBitmaps.Add(thumb);
        if (item.GalleryImages is { Count: > 0 })
        {
            foreach (var img in item.GalleryImages)
            {
                if (img is Bitmap bmp)
                    _previewBitmaps.Add(bmp);
            }
        }

        if (_previewBitmaps.Count == 0)
        {
            AppNotification.Show("该皮肤暂无可用预览图片", NotificationType.Info);
            return;
        }

        UpdatePreviewImage();
        PreviewOverlay.IsVisible = true;
        PreviewOverlay.Focus();
    }

    /// <summary>更新预览图片和导航按钮状态</summary>
    private void UpdatePreviewImage()
    {
        if (_previewIndex < 0 || _previewIndex >= _previewBitmaps.Count) return;

        PreviewImage.Source = _previewBitmaps[_previewIndex];
        PreviewPageText.Text = $"{_previewIndex + 1} / {_previewBitmaps.Count}";

        BtnPrevImage.IsVisible = _previewBitmaps.Count > 1;
        BtnNextImage.IsVisible = _previewBitmaps.Count > 1;
        BtnPrevImage.IsEnabled = _previewIndex > 0;
        BtnNextImage.IsEnabled = _previewIndex < _previewBitmaps.Count - 1;
    }

    /// <summary>关闭预览</summary>
    private void ClosePreview()
    {
        PreviewOverlay.IsVisible = false;
        PreviewImage.Source = null;
        _previewItem = null;
        _previewBitmaps.Clear();
    }

    /// <summary>关闭预览（按钮事件）</summary>
    private void OnClosePreviewClick(object? sender, RoutedEventArgs e)
    {
        ClosePreview();
    }

    /// <summary>点击遮罩层→关闭预览（点击外侧关闭）</summary>
    private void OnPreviewOverlayClick(object? sender, PointerPressedEventArgs e)
    {
        ClosePreview();
    }

    /// <summary>阻止内部卡片点击事件冒泡到遮罩层</summary>
    private void OnPreviewCardClick(object? sender, PointerPressedEventArgs e)
    {
        // 不做任何操作，仅阻止事件继续冒泡
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && PreviewOverlay.IsVisible)
        {
            ClosePreview();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>上一张</summary>
    private void OnPrevImageClick(object? sender, RoutedEventArgs e)
    {
        if (_previewIndex > 0)
        {
            _previewIndex--;
            UpdatePreviewImage();
        }
    }

    /// <summary>下一张</summary>
    private void OnNextImageClick(object? sender, RoutedEventArgs e)
    {
        if (_previewIndex < _previewBitmaps.Count - 1)
        {
            _previewIndex++;
            UpdatePreviewImage();
        }
    }
}
