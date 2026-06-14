using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChipLauncher.Models;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
///     模组下载页面 — 从国内托管服务器浏览、搜索、下载模组
/// </summary>
public partial class ModDownloadPage : UserControl
{
    private readonly ObservableCollection<ModDownloadItem> _mods = [];
    private readonly List<string> _categories = ["全部"];
    private string? _currentSearch;
    private string _currentCategory = "全部";
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalMods;

    /// <summary>取反转换器（true→false, false→true）</summary>
    public static readonly IValueConverter InverseBoolConverter = new FuncConverter<bool, bool>(
        value => value is false);

    public ModDownloadPage()
    {
        InitializeComponent();
        ModItemsControl.ItemsSource = _mods;
        LoadCategories();
    }

    /// <summary>页面可见时加载数据</summary>
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_mods.Count == 0)
            await FetchModsAsync();
    }

    // ── 加载分类 ──────────────────────────────────────────────

    private void LoadCategories()
    {
        CategoryFilterComboBox.ItemsSource = _categories;
        CategoryFilterComboBox.SelectedIndex = 0;
    }

    // ── 从服务器获取模组列表 ─────────────────────────────────

    private async Task FetchModsAsync()
    {
        LoadingIndicator.IsVisible = true;
        EmptyHint.IsVisible = false;
        PaginationPanel.IsVisible = false;

        try
        {
            var result = await ModWebsiteService.FetchModsAsync(
                _currentSearch, _currentCategory, _currentPage);

            _mods.Clear();

            if (result == null || result.Items.Count == 0)
            {
                EmptyHint.IsVisible = true;
                EmptyHint.Text = "未获取到模组数据，请检查托管服务器地址或网络连接";
                return;
            }

            foreach (var item in result.Items)
            {
                // 检查本地是否已安装：扫描 plugins 目录下是否有同名目录
                item.IsDownloaded = IsModInstalledLocally(item.Name);
                _mods.Add(item);
            }

            _totalPages = result.TotalPages;
            _totalMods = result.Total;
            UpdateStats();
            UpdatePagination();
        }
        catch (Exception ex)
        {
            Logger.Error($"获取模组列表失败: {ex.Message}", ex);
            EmptyHint.IsVisible = true;
            EmptyHint.Text = $"加载失败：{ex.Message}";
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
        }
    }

    /// <summary>检查模组是否已安装到本地 plugins 目录</summary>
    private static bool IsModInstalledLocally(string modName)
    {
        try
        {
            var gameDir = GameLocalization.GetGameDirectory();
            if (string.IsNullOrEmpty(gameDir)) return false;

            var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
            if (!Directory.Exists(pluginsDir)) return false;

            // 检查是否存在同名目录（ModInstaller 以 [BepInPlugin] Name 创建目录）
            var modDir = Path.Combine(pluginsDir, modName);
            if (Directory.Exists(modDir)) return true;

            // 也检查 .disabled 后缀（但目录名本身不变）
            var disabledDir = modDir + ".disabled";
            if (Directory.Exists(disabledDir)) return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    // ── 统计信息 ──────────────────────────────────────────────

    private void UpdateStats()
    {
        StatTotalMods.Text = $"共 {_totalMods} 个模组";
    }

    private void UpdatePagination()
    {
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

    // ── 下载模组 ──────────────────────────────────────────────

    private async void OnDownloadModClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ModDownloadItem mod)
            return;

        if (mod.IsDownloaded)
        {
            AppNotification.Show($"模组「{mod.DisplayName}」已安装", NotificationType.Info);
            return;
        }

        if (mod.IsDownloading)
        {
            AppNotification.Show($"模组「{mod.DisplayName}」正在下载中…", NotificationType.Info);
            return;
        }

        mod.IsDownloading = true;
        mod.DownloadProgress = 0;

        try
        {
            await DownloadAndInstallModAsync(mod);
        }
        finally
        {
            mod.IsDownloading = false;
            mod.DownloadProgress = 0;
        }
    }

    /// <summary>下载并安装模组</summary>
    private async Task DownloadAndInstallModAsync(ModDownloadItem mod)
    {
        try
        {
            Logger.Info($"开始下载模组: {mod.DisplayName}");

            var progress = new Progress<double>(p => mod.DownloadProgress = p);
            var data = await ModWebsiteService.DownloadModAsync(mod.DownloadUrl, progress);

            if (data == null || data.Length == 0)
            {
                AppNotification.Show($"模组下载失败: {mod.DisplayName}", NotificationType.Error);
                return;
            }

            // 保存到临时文件
            var tempDir = Path.Combine(Path.GetTempPath(), $"ChipLauncher_Mod_{mod.Id}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var ext = GetExtensionFromUrl(mod.DownloadUrl);
            var tempFile = Path.Combine(tempDir, $"mod{ext}");

            try
            {
                await File.WriteAllBytesAsync(tempFile, data);

                // 使用现有的 ModInstaller 安装
                var gameDir = GameLocalization.GetGameDirectory();
                if (string.IsNullOrEmpty(gameDir))
                {
                    AppNotification.Show("无法获取游戏目录，安装失败", NotificationType.Error);
                    return;
                }

                var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
                var installer = new ModInstaller(pluginsDir);
                var (success, message) = await installer.InstallAsync(tempFile);

                if (success)
                {
                    mod.IsDownloaded = true;
                    Logger.Info($"模组安装完成: {mod.DisplayName}");
                    AppNotification.Show($"模组「{mod.DisplayName}」下载并安装成功", NotificationType.Success);
                }
                else
                {
                    AppNotification.Show($"安装失败: {message}", NotificationType.Error);
                }
            }
            finally
            {
                // 清理临时文件
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { /* 忽略清理失败 */ }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"下载模组失败: {ex.Message}", ex);
            AppNotification.Show($"下载模组失败: {mod.DisplayName}", NotificationType.Error);
        }
    }

    /// <summary>从下载链接推断文件扩展名</summary>
    private static string GetExtensionFromUrl(string url)
    {
        var path = url.Split('?')[0]; // 去掉查询参数
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext switch
        {
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => ext,
            ".dll" => ext,
            _ => ".zip" // 默认当作压缩包
        };
    }

    // ── 搜索 ──────────────────────────────────────────────────

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _currentSearch = SearchTextBox.Text?.Trim();
            _currentPage = 1;
            await FetchModsAsync();
        }
    }

    // ── 分类筛选 ──────────────────────────────────────────────

    private async void OnCategoryFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CategoryFilterComboBox.SelectedItem is string category)
        {
            _currentCategory = category;
            _currentPage = 1;
            await FetchModsAsync();
        }
    }

    // ── 翻页 ──────────────────────────────────────────────────

    private async void OnPrevPageClick(object? sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            await FetchModsAsync();
        }
    }

    private async void OnNextPageClick(object? sender, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
            await FetchModsAsync();
        }
    }

    // ── 刷新 ──────────────────────────────────────────────────

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        await FetchModsAsync();
    }
}
