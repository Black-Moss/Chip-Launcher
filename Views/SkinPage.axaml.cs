using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ChipLauncher.Models;
using ChipLauncher.Services;
using SukiUI.Dialogs;

namespace ChipLauncher.Views;

/// <summary>
///     皮肤管理页面 — 查看已下载的本地皮肤、启用/禁用、删除、查看详情
/// </summary>
public partial class SkinPage : UserControl
{
    // ── 值转换器（与模组管理页同步） ──────────────────────────

    /// <summary>皮肤 ID → 状态圆点颜色（是否匹配 EnabledSkinId）</summary>
    public static readonly IValueConverter EnabledColorConverter =
        new FuncConverter<int, IBrush>(id => AppConfig.Instance.EnabledSkinId == id
            ? new SolidColorBrush(Color.Parse("#e67e22"))
            : new SolidColorBrush(Color.Parse("#888888"))
        );

    /// <summary>皮肤 ID → 状态文本</summary>
    public static readonly IValueConverter EnabledStatusConverter =
        new FuncConverter<int, string>(id => AppConfig.Instance.EnabledSkinId == id
            ? "已启用" : "已禁用"
        );

    /// <summary>皮肤 ID → 按钮文本</summary>
    public static readonly IValueConverter ToggleTextConverter =
        new FuncConverter<int, string>(id => AppConfig.Instance.EnabledSkinId == id
            ? "停用" : "使用"
        );

    /// <summary>皮肤 ID → 切换按钮背景色</summary>
    public static readonly IValueConverter ToggleColorConverter =
        new FuncConverter<int, IBrush>(id => AppConfig.Instance.EnabledSkinId == id
            ? new SolidColorBrush(Color.Parse("#e67e22")) // 橙色：正在使用
            : new SolidColorBrush(Color.Parse("#2d6a2d")) // 绿色：可启用
        );

    // ── 字段 ──────────────────────────────────────────────────

    private static readonly HttpClient HttpClient = new();

    private readonly ObservableCollection<SkinDownloadItem> _skinItems = new();
    private List<SkinDownloadItem>? _allSkins;
    private SkinDownloadItem? _selectedSkin;

    private string _searchFilter = string.Empty;
    private bool _sortAscending = true;

    public SkinPage()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     每次控件被附加到可视化树时（包括 SukiUI 标签切换），重新扫描本地皮肤。
    ///     OnAttachedToVisualTree 比 Loaded 事件更可靠，因为 SukiUI 可能不会在每次
    ///     标签切换时都触发 Loaded。
    /// </summary>
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 检查 SkinSync 模组是否已安装
        if (!SkinSyncService.IsWarningShownThisSession())
            await CheckSkinSyncModAsync();

        await LoadSkinsAsync();
    }

    /// <summary>检查 SkinSync 模组，未安装时弹出提示对话框（每会话仅弹一次）。</summary>
    private static async Task CheckSkinSyncModAsync()
    {
        if (SkinSyncService.IsSkinSyncModInstalled()) return;

        SkinSyncService.MarkWarningShown();
        Logger.Warn("SkinSync 模组未安装 — 皮肤系统不可用");

        await MainWindow.DialogManager.CreateDialog()
            .WithTitle("需要 SkinSync 模组")
            .WithContent("皮肤系统依赖 SkinSync 模组才能正常工作。\n\n" +
                         "SkinSync 是 BepInEx 插件，负责在游戏中加载自定义皮肤。\n" +
                         "未安装时，皮肤管理功能不可用。\n\n" +
                         "点击「前往下载」从 GitHub 获取最新版本。")
            .WithActionButton("关闭", _ => { }, true)
            .WithActionButton("前往下载", _ =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/Bytechey/SkinSync/releases/latest",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("打开 SkinSync 下载链接失败", ex);
                }
            }, true, "Flat", "Accent")
            .TryShowAsync();
    }

    /// <summary>扫描本地皮肤并加载到列表（异步，含进度条）</summary>
    private async Task LoadSkinsAsync()
    {
        // 空保护：XAML 控件可能未完全初始化
        if (SkinListBox == null) return;

        var localSkins = LocalSkinReader.ScanLocalSkins();

        _allSkins = localSkins;
        _skinItems.Clear();
        foreach (var skin in localSkins)
        {
            // 同步加载缩略图（优先缓存中的云端缩略图）
            if (skin.ThumbnailCachePath != null && File.Exists(skin.ThumbnailCachePath))
            {
                try
                {
                    skin.ThumbnailImage = new Bitmap(skin.ThumbnailCachePath);
                }
                catch
                {
                    // ignore
                }
            }

            // 如果该皮肤有云端缩略图 URL 但尚未缓存，异步加载
            if (skin.ThumbnailImage == null && !string.IsNullOrEmpty(skin.ThumbnailUrl))
            {
                _ = LoadCloudThumbnailAsync(skin);
            }

            _skinItems.Add(skin);
        }

        // 将全部本地皮肤同步到游戏 CustomSprites 目录（带进度条）
        await SyncSkinsWithProgressAsync();

        ApplySearchFilter();
        UpdateSkinStats();
    }

    /// <summary>同步皮肤到游戏目录，显示进度遮罩</summary>
    private async Task SyncSkinsWithProgressAsync()
    {
        SyncOverlay.IsVisible = true;
        SyncProgressBar.Value = 0;
        SyncStatusText.Text = "正在扫描…";

        try
        {
            var progress = new Progress<(int Current, int Total, string SkinName)>(state =>
            {
                SyncStatusText.Text = $"({state.Current}/{state.Total}) {state.SkinName}";
                SyncProgressBar.Value = (double)state.Current / state.Total * 100;
            });

            await SkinSyncService.SyncAllToGameAsync(progress);
        }
        finally
        {
            SyncOverlay.IsVisible = false;
        }
    }

    /// <summary>应用搜索过滤（同 ModsPage 模式）</summary>
    private void ApplySearchFilter()
    {
        if (_allSkins == null) return;

        if (string.IsNullOrEmpty(_searchFilter))
        {
            SkinListBox.ItemsSource = _skinItems;
            SkinListBox.IsVisible = _skinItems.Count > 0;
            EmptyHint.Text = "未检测到本地皮肤&#x0a;请先在「皮肤下载」页面下载皮肤";
            EmptyHint.Foreground = new SolidColorBrush(Color.Parse("#888888"));
            EmptyHint.IsVisible = _skinItems.Count == 0;
            NoSelectionHint.IsVisible = _skinItems.Count > 0;
        }
        else
        {
            var filtered = _skinItems
                .Where(s => s.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            SkinListBox.ItemsSource = filtered;
            SkinListBox.IsVisible = filtered.Count > 0;
            EmptyHint.Text = "未找到匹配皮肤，按 Enter 在皮肤下载页中搜索";
            EmptyHint.Foreground = new SolidColorBrush(Color.Parse("#e67e22"));
            EmptyHint.IsVisible = filtered.Count == 0;
            NoSelectionHint.IsVisible = false;
        }

        UpdateSkinStats();
    }

    /// <summary>更新皮肤统计文本</summary>
    private void UpdateSkinStats()
    {
        var source = SkinListBox.ItemsSource as IList<SkinDownloadItem>;
        var count = source?.Count ?? 0;
        if (count == 0)
        {
            SkinStatsText.Text = "";
            return;
        }

        var enabledId = AppConfig.Instance.EnabledSkinId;
        var disabled = count - source!.Count(s => s.Id == enabledId);

        if (_allSkins != null && count < _allSkins.Count)
            SkinStatsText.Text = $"共 {count} 个皮肤 · {disabled} 已禁用（共 {_allSkins.Count} 个）";

        var checkedCount = source!.Count(s => s.IsChecked);
        if (checkedCount > 0)
            SkinStatsText.Text += $" · 勾选 {checkedCount} 个";
    }

    /// <summary>刷新列表显示</summary>
    private void RefreshListDisplay()
    {
        if (SkinListBox.ItemsSource is List<SkinDownloadItem> source)
        {
            SkinListBox.ItemsSource = null;
            SkinListBox.ItemsSource = source;
        }
        UpdateSkinStats();
    }

    /// <summary>搜索框文本变化时过滤列表（同 ModsPage 模式）</summary>
    private void OnSkinSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _searchFilter = SkinSearchBox.Text?.Trim() ?? string.Empty;
        ApplySearchFilter();
    }

    /// <summary>搜索框按键 → Enter 时跳转到皮肤下载页搜索</summary>
    private void OnSkinSearchKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter) return;

        // 无搜索结果时跳转
        if (VisualRoot is MainWindow mainWindow)
        {
            mainWindow.SelectSideMenuItem("皮肤下载");
        }
    }

    /// <summary>刷新按钮 — 重新扫描本地皮肤</summary>
    private void OnRefreshLocalClick(object? sender, RoutedEventArgs e)
    {
        _ = LoadSkinsAsync();
    }

    /// <summary>选中皮肤时更新详情面板</summary>
    private void OnSkinSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        _selectedSkin = e.AddedItems[0] as SkinDownloadItem;
        if (_selectedSkin == null) return;

        // 更新详情面板
        DetailName.Text = _selectedSkin.Name;
        DetailAuthor.Text = _selectedSkin.Author ?? "未知";
        DetailSize.Text = _selectedSkin.SizeDisplay;
        DetailDownloads.Text = _selectedSkin.DownloadsDisplay;
        DetailDescription.Text = $"存储位置：{LocalSkinReader.GetSkinDirectory(_selectedSkin.Name) ?? "未知"}";

        // 加载大图预览：优先使用画廊图片（高清），其次缩略图
        LoadDetailPreview(_selectedSkin);

        // 更新启用按钮状态
        UpdateEnableButtonState();

        DeleteConfirmPanel.IsVisible = false;
        DetailPanel.IsVisible = true;
        NoSelectionHint.IsVisible = false;
    }

    /// <summary>加载详情面板的大图预览（优先高清画廊图）</summary>
    private void LoadDetailPreview(SkinDownloadItem skin)
    {
        // 1. 优先使用已缓存的高清预览图（画廊第一张）
        if (SkinCache.HasPreviewCache(skin.Name))
        {
            var previewPath = SkinCache.GetPreviewLocalPath(skin.Name);
            if (previewPath != null && File.Exists(previewPath))
            {
                try
                {
                    DetailPreview.Source = new Bitmap(previewPath);
                    return;
                }
                catch
                {
                    // 文件损坏则忽略，继续走缩略图
                }
            }
        }

        // 2. 使用缩略图（列表项用的低清图）
        if (skin.ThumbnailImage is Bitmap bmp)
        {
            DetailPreview.Source = bmp;
        }
        else
        {
            DetailPreview.Source = null;
        }

        // 3. 如果有画廊 URL 且尚未缓存，异步加载高清预览图
        if (skin.GalleryUrls is { Count: > 0 } && !SkinCache.HasPreviewCache(skin.Name))
        {
            _ = LoadPreviewImageAsync(skin);
        }
    }

    /// <summary>异步下载并缓存皮肤画廊第一张高清大图，加载到详情预览</summary>
    private async Task LoadPreviewImageAsync(SkinDownloadItem skin)
    {
        try
        {
            var galleryUrl = skin.GalleryUrls![0];
            if (string.IsNullOrEmpty(galleryUrl)) return;

            // 拼接完整 URL（皮肤站使用相对路径）
            var fullUrl = galleryUrl.StartsWith("http")
                ? galleryUrl
                : $"{SkinWebsiteService.SiteBaseUrl}{galleryUrl}";

            var data = await HttpClient.GetByteArrayAsync(fullUrl);
            await SkinCache.CachePreviewAsync(skin.Name, data);

            // 加载到 UI
            var previewPath = SkinCache.GetPreviewLocalPath(skin.Name);
            if (previewPath != null && File.Exists(previewPath))
            {
                var bitmap = new Bitmap(previewPath);
                // 只更新当前选中皮肤的预览
                if (_selectedSkin?.Name == skin.Name)
                {
                    DetailPreview.Source = bitmap;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"加载高清预览图失败 [{skin.Name}]: {ex.Message}");
        }
    }

    /// <summary>更新"启用皮肤"按钮的状态文字和颜色</summary>
    private void UpdateEnableButtonState()
    {
        if (_selectedSkin == null) return;

        var isEnabled = AppConfig.Instance.EnabledSkinId == _selectedSkin.Id;
        if (isEnabled)
        {
            BtnEnableSkin.Content = "正在使用该皮肤";
            BtnEnableSkin.Background = new SolidColorBrush(Color.Parse("#e67e22"));
            EnableStatusText.Text = "此皮肤已在启动时启用";
            EnableStatusText.IsVisible = true;
        }
        else
        {
            BtnEnableSkin.Content = "使用该皮肤";
            BtnEnableSkin.Background = new SolidColorBrush(Color.Parse("#2d6a2d"));
            EnableStatusText.IsVisible = false;
        }

    }

    // ── 工具栏按钮 ──────────────────────────────────────────────

    /// <summary>安装本地皮肤（打开文件选择对话框选择 .zip 文件）</summary>
    private async void OnInstallLocalSkinClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is not Window parentWindow) return;

        var files = await parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择皮肤文件（.zip）",
            AllowMultiple = true,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("皮肤文件") { Patterns = ["*.zip"] },
                new("所有文件") { Patterns = ["*"] }
            }
        });

        if (files.Count == 0) return;

        InstallOverlay.IsVisible = true;

        var installed = 0;
        var failed = 0;

        foreach (var file in files)
        {
            try
            {
                var zipPath = file.Path.LocalPath;
                var tempDir = Path.Combine(Path.GetTempPath(), $"skin_install_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                // 解压到临时目录
                ZipFile.ExtractToDirectory(zipPath, tempDir);

                // 检测结构并复制到 skins/
                // 支持格式：{id}/Body/* 或 Body/* 或 直接图片文件
                var hasIdDir = false;
                foreach (var subDir in Directory.GetDirectories(tempDir))
                {
                    var subName = Path.GetFileName(subDir);
                    if (int.TryParse(subName, out _) || subName.Equals("Body", StringComparison.OrdinalIgnoreCase))
                    {
                        hasIdDir = true;
                        break;
                    }
                }

                if (hasIdDir)
                {
                    // 格式已匹配 skins/{id}/Body/ — 直接复制整个临时目录
                    foreach (var subDir in Directory.GetDirectories(tempDir))
                    {
                        var targetDir = Path.Combine(LocalSkinReader.SkinsDirectory, Path.GetFileName(subDir));
                        CopyDirectory(subDir, targetDir);
                    }
                }
                else
                {
                    // 无 ID 目录：直接作为新皮肤安装，使用 GUID 作为 ID
                    var newId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var target = Path.Combine(LocalSkinReader.SkinsDirectory, $"{newId}", "Body");
                    Directory.CreateDirectory(target);
                    CopyDirectory(tempDir, target);
                }

                // 清理临时目录
                try { Directory.Delete(tempDir, true); } catch { }

                installed++;
            }
            catch (Exception ex)
            {
                Logger.Error($"安装本地皮肤失败: {ex.Message}");
                failed++;
            }
        }

        InstallOverlay.IsVisible = false;
        _ = LoadSkinsAsync();

        if (installed > 0 && failed == 0)
            AppNotification.Show($"已安装 {installed} 个皮肤", NotificationType.Success);
        else if (installed > 0 && failed > 0)
            AppNotification.Show($"安装 {installed} 个，{failed} 个失败", NotificationType.Warning);
        else
            AppNotification.Show("安装失败", NotificationType.Error);
    }

    /// <summary>工具栏 — 打开皮肤文件夹</summary>
    private void OnOpenSkinFolderToolbarClick(object? sender, RoutedEventArgs e)
    {
        var skinsDir = LocalSkinReader.SkinsDirectory;
        if (!Directory.Exists(skinsDir))
        {
            AppNotification.Show("皮肤目录不存在", NotificationType.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(skinsDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开皮肤文件夹失败: {ex.Message}");
        }
    }

    /// <summary>详情面板 — 打开此皮肤文件夹</summary>
    private void OnOpenSkinFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedSkin == null) return;
        var dir = LocalSkinReader.GetSkinDirectory(_selectedSkin.Name);
        if (dir != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error($"打开皮肤文件夹失败: {ex.Message}");
            }
        }
        else
        {
            AppNotification.Show("皮肤目录不存在", NotificationType.Warning);
        }
    }

    /// <summary>前往下载页</summary>
    private void OnOpenSkinDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
        {
            mainWindow.SelectSideMenuItem("皮肤下载");
        }
    }

    /// <summary>切换 A-Z / Z-A 排序</summary>
    private void OnSortOrderClick(object? sender, RoutedEventArgs e)
    {
        _sortAscending = !_sortAscending;
        BtnSortOrder.Content = _sortAscending ? "A-Z ▾" : "Z-A ▴";

        // 重新排序
        var list = SkinListBox.ItemsSource as List<SkinDownloadItem>;
        if (list == null) return;

        if (_sortAscending)
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        else
            list.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));

        RefreshListDisplay();
    }

    // ── 启用/禁用皮肤 ──────────────────────────────────────────

    /// <summary>每行切换按钮点击（同 ModsPage.OnToggleClick 模式）</summary>
    /// <summary>列表卡片中的"使用"/"停用"按钮点击</summary>
    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SkinDownloadItem skin }) return;

        var currentEnabledId = AppConfig.Instance.EnabledSkinId;

        // 如果点击的就是当前使用的皮肤 → 取消选择
        if (currentEnabledId == skin.Id)
        {
            AppConfig.Instance.EnabledSkinId = -1;
            Logger.Info($"取消选择皮肤: {skin.Name} (#{skin.Id})");
            SkinSyncService.ClearCurrentSkin();
            RefreshAllStates();
            return;
        }

        // 使用此皮肤
        ApplyEnableSkin(skin);
    }

    /// <summary>详情面板中的"使用该皮肤"按钮点击</summary>
    private void OnEnableSkinClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedSkin == null) return;
        var skin = _selectedSkin;

        var currentEnabledId = AppConfig.Instance.EnabledSkinId;

        // 如果点击的就是当前使用的皮肤 → 取消选择
        if (currentEnabledId == skin.Id)
        {
            AppConfig.Instance.EnabledSkinId = -1;
            Logger.Info($"取消选择皮肤: {skin.Name} (#{skin.Id})");
            SkinSyncService.ClearCurrentSkin();
            RefreshAllStates();
            return;
        }

        // 使用此皮肤
        ApplyEnableSkin(skin);
    }

    /// <summary>执行皮肤使用操作（更新配置 + 通知 SkinSync）</summary>
    private void ApplyEnableSkin(SkinDownloadItem skin)
    {
        AppConfig.Instance.EnabledSkinId = skin.Id;
        Logger.Info($"使用皮肤: {skin.Name} (#{skin.Id})");
        // 通知 SkinSync Mod 当前使用的皮肤名称
        SkinSyncService.SetCurrentSkin(skin.Name);
        RefreshAllStates();
        UpdateSkinStats();
    }

    /// <summary>
    ///     刷新所有列表项的状态显示（启用/禁用按钮、圆点颜色等），保留搜索过滤。
    ///     先将 ItemsSource 置空再重新绑定，强制列表项容器重建，使转换器重新计算
    ///     EnabledSkinId（否则绑定到静态 Id 的转换器不会自动重新求值）。
    /// </summary>
    private void RefreshAllStates()
    {
        // 强制列表容器重建（Avalonia 不会对相同引用的 ItemsSource 重新生成容器，
        // 而绑定到 Id 的转换器无法感知 AppConfig.Instance.EnabledSkinId 的变化）
        SkinListBox.ItemsSource = null;

        // 重新应用搜索过滤
        ApplySearchFilter();

        // 如果详情面板已打开，更新按钮状态
        if (_selectedSkin != null)
        {
            UpdateEnableButtonState();
        }
    }

    // ── 复选框 + 批量操作（同 ModsPage 模式） ─────────────────

    /// <summary>复选框点击 → 处理勾选状态变化后的 UI 更新</summary>
    private void OnSkinCheckBoxClick(object? sender, RoutedEventArgs e)
    {
        // IsChecked 已通过双向绑定自动更新到 SkinDownloadItem.IsChecked
        // 这里只需要更新批量工具栏的显示状态
        UpdateBatchUI();
    }

    /// <summary>更新批量操作 UI</summary>
    private void UpdateBatchUI()
    {
        var source = SkinListBox.ItemsSource as IList<SkinDownloadItem>;
        var checkedCount = source?.Count(s => s.IsChecked) ?? 0;
        BatchToolbar.IsVisible = checkedCount > 0;
        var totalCount = source?.Count ?? 0;
        BtnBatchSelectAll.Content = checkedCount >= totalCount && totalCount > 0
            ? "全不选"
            : "全选";
        UpdateSkinStats();
    }

    /// <summary>全选 / 全不选</summary>
    private void OnBatchSelectAllClick(object? sender, RoutedEventArgs e)
    {
        if (SkinListBox.ItemsSource is not IList<SkinDownloadItem> source || source.Count == 0)
            return;

        var checkedCount = source.Count(s => s.IsChecked);

        // 已全选 → 全不选
        if (checkedCount == source.Count)
        {
            foreach (var skin in source)
                skin.IsChecked = false;
            BtnBatchSelectAll.Content = "全选";
            BatchToolbar.IsVisible = false;
            UpdateSkinStats();
            return;
        }

        // 全选
        foreach (var skin in source)
            skin.IsChecked = true;

        BtnBatchSelectAll.Content = "全不选";
        BatchToolbar.IsVisible = true;
        UpdateSkinStats();
    }

    /// <summary>反选</summary>
    private void OnBatchInvertClick(object? sender, RoutedEventArgs e)
    {
        if (SkinListBox.ItemsSource is not IList<SkinDownloadItem> source) return;

        foreach (var skin in source)
            skin.IsChecked = !skin.IsChecked;

        UpdateBatchUI();
    }

    /// <summary>批量删除选中的皮肤</summary>
    private async void OnBatchDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (SkinListBox.ItemsSource is not IList<SkinDownloadItem> source) return;
        var selected = source.Where(s => s.IsChecked).ToList();
        if (selected.Count == 0) return;

        if (AppConfig.Instance.ConfirmModDeletion)
        {
            // 使用与单删相同的确认面板
            _pendingBatchDelete = selected;
            DeleteConfirmText.Text = $"确定要删除选中的 {selected.Count} 个皮肤吗？\n此操作将删除整个皮肤目录，不可恢复。";
            DeleteConfirmPanel.IsVisible = true;
        }
        else
        {
            await DeleteSkinsAsync(selected);
        }
    }

    /// <summary>批量删除等待列表（用于确认面板）</summary>
    private List<SkinDownloadItem>? _pendingBatchDelete;

    // ── 删除皮肤（与模组管理页同步） ───────────────────────────

    /// <summary>点击"删除此皮肤"按钮</summary>
    private void OnDeleteSkinClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedSkin == null) return;

        if (AppConfig.Instance.ConfirmModDeletion)
        {
            DeleteConfirmText.Text = $"确定要删除「{_selectedSkin.Name}」吗？\n此操作将删除整个皮肤目录，不可恢复。";
            DeleteConfirmPanel.IsVisible = true;
        }
        else
        {
            DeleteSkinAndRefresh(_selectedSkin);
        }
    }

    /// <summary>确认删除（单删/批量）</summary>
    private async void OnConfirmDeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteConfirmPanel.IsVisible = false;

        // 优先处理批量删除
        if (_pendingBatchDelete is { Count: > 0 })
        {
            var skins = _pendingBatchDelete;
            _pendingBatchDelete = null;
            await DeleteSkinsAsync(skins);
            return;
        }

        if (_selectedSkin == null) return;

        // 保存目录路径，防止 _selectedSkin 在操作过程中丢失
        var skin = _selectedSkin;
        var skinName = skin.Name;

        await Task.Run(() =>
        {
            var dir = skin.DirectoryPath;
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            // 从元数据中移除
            SkinsMetadataService.Remove(skin.Id);
        });

        // 如果删除的是已启用的皮肤，清除启用状态
        if (AppConfig.Instance.EnabledSkinId == skin.Id)
            AppConfig.Instance.EnabledSkinId = -1;

        AppNotification.Show($"皮肤「{skinName}」已删除", NotificationType.Success);
        _selectedSkin = null;
        DetailPanel.IsVisible = false;
        NoSelectionHint.IsVisible = true;
        _ = LoadSkinsAsync();
    }

    /// <summary>取消删除</summary>
    private void OnCancelDeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteConfirmPanel.IsVisible = false;
        _pendingBatchDelete = null;
    }

    /// <summary>批量删除皮肤</summary>
    private async Task DeleteSkinsAsync(List<SkinDownloadItem> skins)
    {
        var count = skins.Count;
        await Task.Run(() =>
        {
            foreach (var skin in skins)
            {
                // 使用 DirectoryPath（ScanLocalSkins 中已设置）直接定位目录
                var dir = skin.DirectoryPath;
                if (dir != null && Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);

                // 从元数据中移除
                SkinsMetadataService.Remove(skin.Id);
            }
        });

        // 清除已删除皮肤的启用状态
        var enabledId = AppConfig.Instance.EnabledSkinId;
        if (skins.Any(s => s.Id == enabledId))
            AppConfig.Instance.EnabledSkinId = -1;

        _selectedSkin = null;
        DetailPanel.IsVisible = false;
        NoSelectionHint.IsVisible = true;
        _ = LoadSkinsAsync();
        AppNotification.Show($"已删除 {count} 个皮肤", NotificationType.Success);
    }

    /// <summary>直接删除皮肤（无二次确认）</summary>
    private void DeleteSkinAndRefresh(SkinDownloadItem skin)
    {
        var dir = skin.DirectoryPath;
        if (dir != null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        SkinsMetadataService.Remove(skin.Id);

        if (AppConfig.Instance.EnabledSkinId == skin.Id)
            AppConfig.Instance.EnabledSkinId = -1;

        AppNotification.Show($"皮肤「{skin.Name}」已删除", NotificationType.Success);
        _selectedSkin = null;
        DetailPanel.IsVisible = false;
        NoSelectionHint.IsVisible = true;
        _ = LoadSkinsAsync();
    }

    // ── 异步加载云端缩略图 ─────────────────────────────────────

    /// <summary>
    ///     异步加载云端缩略图（用于皮肤管理页的列表缩略图）。
    ///     下载后同时保存到皮肤目录 skins/{id}/thumb.png，这样下次
    ///     ScanLocalSkins 时可直接从本地加载（优先级最高）。
    /// </summary>
    private static async Task LoadCloudThumbnailAsync(SkinDownloadItem skin)
    {
        try
        {
            string? localPath = null;
            if (SkinCache.HasCache(skin.Name))
            {
                localPath = SkinCache.GetLocalPath(skin.Name);
            }
            else if (!string.IsNullOrEmpty(skin.ThumbnailUrl))
            {
                var temp = new SkinDownloadItem
                {
                    Id = skin.Id,
                    Name = skin.Name,
                    ThumbnailUrl = skin.ThumbnailUrl
                };
                localPath = await SkinWebsiteService.CacheThumbnailAsync(temp);
            }

            if (localPath != null && File.Exists(localPath))
            {
                var bitmap = new Bitmap(localPath);
                skin.ThumbnailImage = bitmap;

                // 额外：复制到皮肤目录（skins/{id}/thumb.png），
                // 确保下次扫描可本地加载，不依赖缓存
                if (skin.DirectoryPath != null && Directory.Exists(skin.DirectoryPath))
                {
                    try
                    {
                        var ext = Path.GetExtension(localPath)?.ToLowerInvariant();
                        if (string.IsNullOrEmpty(ext))
                            ext = ".png";
                        var dest = Path.Combine(skin.DirectoryPath, $"thumb{ext}");
                        if (!File.Exists(dest))
                            File.Copy(localPath, dest, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"保存云缩略图到皮肤目录失败 [{skin.Name}]: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"加载云端缩略图失败 [{skin.Name}]: {ex.Message}");
        }
    }

    // ── 工具方法 ────────────────────────────────────────────────

    /// <summary>递归复制目录</summary>
    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectory(dir, dest);
        }
    }
}
