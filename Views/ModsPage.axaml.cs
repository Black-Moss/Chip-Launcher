using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ChipLauncher.Models;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
///     模组管理页面 — 扫描 BepInEx\plugins 子目录，支持启用/禁用切换、配置编辑和拖放安装
/// </summary>
public partial class ModsPage : UserControl
{
    // ── 值转换器 ──────────────────────────────────────────────

    /// <summary>启用/禁用 → 状态圆点颜色</summary>
    public static readonly IValueConverter ColorConverter = new FuncConverter<bool, IBrush>(enabled => enabled
        ? new SolidColorBrush(Color.Parse("#4CAF50"))
        : new SolidColorBrush(Color.Parse("#888888"))
    );

    /// <summary>启用/禁用 → 状态文本</summary>
    public static readonly IValueConverter StatusTextConverter =
        new FuncConverter<bool, string>(enabled => enabled ? "已启用" : "已禁用"
        );

    /// <summary>启用/禁用 → 按钮文本</summary>
    public static readonly IValueConverter ToggleTextConverter =
        new FuncConverter<bool, string>(enabled => enabled ? "禁用" : "启用"
        );

    /// <summary>SettingType → 是否为 Boolean（控制开关可见性）</summary>
    public static readonly IValueConverter IsBooleanConverter =
        new FuncConverter<string, bool>(settingType =>
            string.Equals(settingType, "Boolean", StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>SettingType → 是否非 Boolean（控制文本框可见性）</summary>
    public static readonly IValueConverter IsNotBooleanConverter =
        new FuncConverter<string, bool>(settingType =>
            !string.Equals(settingType, "Boolean", StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>Value (string) ↔ bool（ToggleSwitch 双向绑定）</summary>
    public static readonly IValueConverter StringToBoolConverter = new FuncConverter<string, bool>(
        value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase),
        b => b ? "true" : "false"
    );

    private const string DefaultEmptyHint =
        "未找到 BepInEx 模组\n请确保游戏已安装 BepInEx\n且模组位于 BepInEx\\plugins 目录";

    private const string SearchNoResultHint =
        "未找到匹配模组，按 Enter 在 NexusMods 网页中搜索";

    private List<ModInfo>? _pendingBatchDelete;

    private List<ModInfo>? _allMods;

    private BepInExConfig? _currentConfig;

    private ModInfo? _selectedMod;

    private ModInstaller? _installer;

    // ── 字段 ──────────────────────────────────────────────────

    private string? _gameDir;

    // ── 页面逻辑 ──────────────────────────────────────────────

    public ModsPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        ModSearchBox.TextChanged += OnModSearchChanged;
    }

    private void InitializeInstaller()
    {
        _gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(_gameDir)) return;

        var pluginsDir = Path.Combine(_gameDir, "BepInEx", "plugins");
        if (Directory.Exists(pluginsDir))
            _installer = new ModInstaller(pluginsDir);
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        LoadMods();
    }

    // ===== 本地模组管理 =====

    /// <summary>扫描 BepInEx\plugins 子目录加载模组列表</summary>
    private void LoadMods()
    {
        ErrorHint.IsVisible = false;

        _gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(_gameDir))
        {
            ShowError("未找到游戏目录。请在设置页面中配置游戏路径。");
            return;
        }

        var pluginsDir = Path.Combine(_gameDir, "BepInEx", "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            ShowError("未找到 BepInEx\\plugins 目录。\n请确保游戏已安装 BepInEx。");
            return;
        }

        _installer = new ModInstaller(pluginsDir);

        try
        {
            var mods = new List<ModInfo>();

            foreach (var dir in Directory.GetDirectories(pluginsDir))
            {
                var dllFiles = Directory.GetFiles(dir, "*.dll");
                if (dllFiles.Length > 0)
                {
                    var (guid, name, _) = ModInstaller.GetBepInPluginInfo(dllFiles[0]);
                    if (string.IsNullOrEmpty(guid)) continue; // 无 [BepInPlugin] 不是有效模组

                    mods.Add(new ModInfo
                    {
                        Name = name ?? Path.GetFileName(dir),
                        Guid = guid,
                        DirectoryPath = dir,
                        PluginFilePath = dllFiles[0]
                    });
                    continue;
                }

                var disabledFiles = Directory.GetFiles(dir, "*.disabled");
                if (disabledFiles.Length > 0)
                {
                    var dllPath = disabledFiles[0].Replace(".disabled", "");
                    if (!File.Exists(dllPath)) continue;

                    var (guid, name, _) = ModInstaller.GetBepInPluginInfo(dllPath);
                    if (string.IsNullOrEmpty(guid)) continue;

                    mods.Add(new ModInfo
                    {
                        Name = name ?? Path.GetFileName(dir),
                        Guid = guid,
                        DirectoryPath = dir,
                        PluginFilePath = disabledFiles[0]
                    });
                }
            }

            mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            _allMods = mods;

            // 如果有搜索关键词，应用过滤
            var keyword = ModSearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                ModListBox.ItemsSource = mods;
                ModListBox.IsVisible = mods.Count > 0;
                EmptyHint.Text = DefaultEmptyHint;
                EmptyHint.IsVisible = mods.Count == 0;
                EmptyHint.Foreground = new SolidColorBrush(Color.Parse("#888888"));
                NoSelectionHint.IsVisible = mods.Count > 0;
            }
            else
            {
                var filtered = mods
                    .Where(m => m.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                ModListBox.ItemsSource = filtered;
                ModListBox.IsVisible = filtered.Count > 0;
                EmptyHint.Text = SearchNoResultHint;
                EmptyHint.IsVisible = filtered.Count == 0;
                EmptyHint.Foreground = new SolidColorBrush(Color.Parse("#e67e22"));
                NoSelectionHint.IsVisible = false;
            }

            UpdateModStats();
        }
        catch (Exception ex)
        {
            ShowError($"读取模组目录失败：{ex.Message}");
        }
    }

    /// <summary>更新模组统计文本（总数/启用/禁用）</summary>
    private void UpdateModStats()
    {
        var source = ModListBox.ItemsSource as List<ModInfo>;
        if (source == null || source.Count == 0)
        {
            ModStatsText.Text = "";
            return;
        }

        var total = source.Count;
        var enabled = source.Count(m => m.IsEnabled);
        var disabled = total - enabled;

        // 如果在搜索模式下，额外显示总数
        if (_allMods != null && source.Count < _allMods.Count)
            ModStatsText.Text = $"共 {total} 模组 · {enabled} 已启用 · {disabled} 已禁用（共 {_allMods.Count} 个）";
        else
            ModStatsText.Text = $"共 {total} 模组 · {enabled} 已启用 · {disabled} 已禁用";
    }

    /// <summary>更新批量操作按钮的文字（显示选中数量）</summary>
    private void UpdateBatchButtonTexts()
    {
        var count = ModListBox.SelectedItems!.Count;
        BtnBatchEnable.Content = $"启用选中 ({count})";
        BtnBatchDisable.Content = $"禁用选中 ({count})";
        BtnBatchDelete.Content = $"🗑 删除选中 ({count})";
    }

    /// <summary>强制刷新列表显示（用于批量切换后）</summary>
    private void RefreshListDisplay()
    {
        var source = ModListBox.ItemsSource as List<ModInfo>;
        if (source != null)
        {
            ModListBox.ItemsSource = null;
            ModListBox.ItemsSource = source;
        }
        UpdateModStats();
    }

    /// <summary>搜索框文本变化 → 过滤模组列表，并控制提示文字</summary>
    private void OnModSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_allMods == null) return;

        var keyword = ModSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            ModListBox.ItemsSource = _allMods;
            ModListBox.IsVisible = _allMods.Count > 0;
            EmptyHint.Text = DefaultEmptyHint;
            EmptyHint.Foreground = new SolidColorBrush(Color.Parse("#888888"));
            EmptyHint.IsVisible = _allMods.Count == 0;
            UpdateModStats();
            return;
        }

        var filtered = _allMods
            .Where(m => m.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
        ModListBox.ItemsSource = filtered;
        ModListBox.IsVisible = filtered.Count > 0;
        EmptyHint.Text = SearchNoResultHint;
        EmptyHint.IsVisible = filtered.Count == 0;
        EmptyHint.Foreground = new SolidColorBrush(Color.Parse("#e67e22"));
        UpdateModStats();
    }

    /// <summary>搜索框按键 → Enter 时打开 NexusMods 网页搜索</summary>
    private void OnModSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        var keyword = ModSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword)) return;

        const string domain = "scavprototype";
        if (string.IsNullOrEmpty(domain)) return;

        var url = $"https://www.nexusmods.com/games/{domain}/search?keyword={Uri.EscapeDataString(keyword)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>供 MainWindow 拖放调用 — 批量安装文件并刷新列表</summary>
    public async Task InstallFilesAsync(IEnumerable<string> filePaths)
    {
        if (_installer == null)
        {
            AppNotification.Show("请先在设置页中配置游戏路径", NotificationType.Warning);
            return;
        }

        var installed = 0;
        var failed = 0;
        var lastMessage = "";

        foreach (var localPath in filePaths)
        {
            var (success, message) = await _installer.InstallAsync(localPath);
            if (success)
                installed++;
            else
                failed++;
            lastMessage = message;
        }

        // 刷新模组列表
        LoadMods();

        if (installed > 0 && failed == 0)
            AppNotification.Show($"已安装 {installed} 个模组", NotificationType.Success);
        else if (installed > 0 && failed > 0)
            AppNotification.Show($"安装 {installed} 个，{failed} 个失败", NotificationType.Warning);
        else
            AppNotification.Show(lastMessage, NotificationType.Error);
    }

    /// <summary>切换模组启用/禁用</summary>
    private async void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ModInfo mod) return;

        var pluginFile = mod.PluginFilePath;

        try
        {
            if (pluginFile.EndsWith(".disabled"))
            {
                // .disabled -> .dll（启用）
                var dllPath = pluginFile.Replace(".disabled", "");
                File.Move(pluginFile, dllPath);
                mod.PluginFilePath = dllPath;
                mod.IsEnabled = true;
            }
            else
            {
                // .dll -> .disabled（禁用）
                var disabledPath = pluginFile + ".disabled";
                File.Move(pluginFile, disabledPath);
                mod.PluginFilePath = disabledPath;
                mod.IsEnabled = false;
            }

            // 刷新列表显示
            var list = ModListBox.ItemsSource as List<ModInfo>;
            if (list != null)
            {
                var idx = list.IndexOf(mod);
                if (idx >= 0)
                {
                    list[idx] = mod; // 触发 UI 更新
                    ModListBox.ItemsSource = null;
                    ModListBox.ItemsSource = list;
                }
            }

            ClearConfigPanel();
            UpdateModStats();
        }
        catch (Exception ex)
        {
            ShowError($"切换失败：{ex.Message}");
        }
    }

    private void OnModSelected(object? sender, SelectionChangedEventArgs e)
    {
        _pendingBatchDelete = null;
        var selectedCount = ModListBox.SelectedItems!.Count;

        if (selectedCount == 0)
        {
            _selectedMod = null;
            ClearConfigPanel();
            BatchToolbar.IsVisible = false;
            return;
        }

        if (selectedCount == 1 && ModListBox.SelectedItems[0] is ModInfo mod)
        {
            // 单选 → 显示配置面板（原有行为）
            _selectedMod = mod;
            LoadConfigForMod(mod);
            BatchToolbar.IsVisible = false;
            return;
        }

        // 多选 → 显示批量操作工具栏，隐藏配置面板
        _selectedMod = null;
        ClearConfigPanel();
        BatchToolbar.IsVisible = true;
        UpdateBatchButtonTexts();
    }

    /// <summary>加载选中模组的 BepInEx 配置（根据 GUID 匹配 {BepInEx/config}/{GUID}.cfg）</summary>
    private void LoadConfigForMod(ModInfo mod)
    {
        _gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(_gameDir))
        {
            ShowConfigUnavailable("未找到游戏目录。");
            return;
        }

        var configDir = Path.Combine(_gameDir, "BepInEx", "config");

        // 优先按 GUID 查找配置文件
        if (!string.IsNullOrEmpty(mod.Guid))
        {
            var cfgPath = Path.Combine(configDir, mod.Guid + ".cfg");
            if (File.Exists(cfgPath))
            {
                var cfg = BepInExConfig.Load(cfgPath);
                if (cfg != null)
                {
                    _currentConfig = cfg;
                    ConfigFileName.Text = cfg.FileName;
                    ConfigDescription.Text = cfg.ModDescription;
                    ConfigItemsControl.ItemsSource = cfg.Entries;
                    ConfigPanel.IsVisible = true;
                    NoSelectionHint.IsVisible = false;
                    return;
                }
            }
        }

        // 降级：在模组目录下查找 config/ 子目录（旧版兼容）
        var legacyCfgDir = Path.Combine(mod.DirectoryPath, "config");
        if (Directory.Exists(legacyCfgDir))
        {
            var cfgFiles = Directory.GetFiles(legacyCfgDir, "*.cfg");
            if (cfgFiles.Length > 0)
            {
                var cfg = BepInExConfig.Load(cfgFiles[0]);
                if (cfg != null)
                {
                    _currentConfig = cfg;
                    ConfigFileName.Text = cfg.FileName;
                    ConfigDescription.Text = cfg.ModDescription;
                    ConfigItemsControl.ItemsSource = cfg.Entries;
                    ConfigPanel.IsVisible = true;
                    NoSelectionHint.IsVisible = false;
                    return;
                }
            }
        }

        ShowConfigUnavailable("此模组没有配置文件。");
    }

    private void ShowConfigPanel(BepInExConfig config)
    {
        _currentConfig = config;
        ConfigItemsControl.ItemsSource = config.Entries;
        ConfigPanel.IsVisible = true;
        NoSelectionHint.IsVisible = false;
    }

    private void ShowConfigUnavailable(string reason)
    {
        ClearConfigPanel();

        ConfigItemsControl.ItemsSource = null;
        ConfigFileName.Text = "";
        ConfigDescription.Text = reason;
        ConfigPanel.IsVisible = true;
        NoSelectionHint.IsVisible = false;
    }

    private void ClearConfigPanel()
    {
        ConfigPanel.IsVisible = false;
        NoSelectionHint.IsVisible = true;
        _currentConfig = null;
        DeleteConfirmPanel.IsVisible = false;
        BatchToolbar.IsVisible = false;
    }

    // ── 删除模组 ──────────────────────────────────────────────

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedMod == null) return;

        if (AppConfig.Instance.ConfirmModDeletion)
        {
            DeleteConfirmText.Text = $"确定要删除「{_selectedMod.Name}」吗？\n此操作将删除整个模组目录，不可恢复。";
            DeleteConfirmPanel.IsVisible = true;
        }
        else
        {
            DeleteModAndRefresh(_selectedMod);
        }
    }

    private async void OnConfirmDeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteConfirmPanel.IsVisible = false;

        // 优先处理批量删除
        if (_pendingBatchDelete != null && _pendingBatchDelete.Count > 0)
        {
            var mods = _pendingBatchDelete;
            _pendingBatchDelete = null;
            var count = mods.Count;

            await Task.Run(() =>
            {
                foreach (var mod in mods)
                    DeleteMod(mod);
            });

            ClearConfigPanel();
            LoadMods();
            AppNotification.Show($"已删除 {count} 个模组", NotificationType.Success);
            return;
        }

        if (_selectedMod == null) return;

        var modName = _selectedMod.Name;
        await Task.Run(() => DeleteMod(_selectedMod!));

        ClearConfigPanel();
        LoadMods();
        AppNotification.Show($"已删除「{modName}」", NotificationType.Success);
    }

    private void OnCancelDeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteConfirmPanel.IsVisible = false;
        _pendingBatchDelete = null;
    }

    /// <summary>删除模组目录（递归）并刷新列表</summary>
    private void DeleteModAndRefresh(ModInfo mod)
    {
        DeleteMod(mod);
        ClearConfigPanel();
        LoadMods();
        AppNotification.Show($"已删除「{mod.Name}」", NotificationType.Success);
    }

    /// <summary>删除模组目录（递归）</summary>
    private static void DeleteMod(ModInfo mod)
    {
        try
        {
            if (Directory.Exists(mod.DirectoryPath))
                Directory.Delete(mod.DirectoryPath, true);
        }
        catch (Exception ex)
        {
            Logger.Error($"删除模组失败: {mod.Name}", ex);
        }
    }

    private async void OnSaveConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_currentConfig == null) return;

        var ok = _currentConfig.Save();
        AppNotification.Show(ok ? "配置已保存" : "配置保存失败",
            ok ? NotificationType.Success : NotificationType.Error);
    }

    // ── 批量操作 ────────────────────────────────────────────────

    private async void OnBatchEnableClick(object? sender, RoutedEventArgs e)
    {
        var selected = ModListBox.SelectedItems!.Cast<ModInfo>().ToList();
        var toggled = 0;

        foreach (var mod in selected)
        {
            if (mod.IsEnabled) continue;

            var pluginFile = mod.PluginFilePath;
            if (!pluginFile.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var dllPath = pluginFile[..^".disabled".Length];
                File.Move(pluginFile, dllPath);
                mod.PluginFilePath = dllPath;
                toggled++;
            }
            catch (Exception ex)
            {
                Logger.Error($"启用模组失败: {mod.Name}", ex);
            }
        }

        RefreshListDisplay();
        AppNotification.Show($"已启用 {toggled} 个模组", NotificationType.Success);
    }

    private async void OnBatchDisableClick(object? sender, RoutedEventArgs e)
    {
        var selected = ModListBox.SelectedItems!.Cast<ModInfo>().ToList();
        var toggled = 0;

        foreach (var mod in selected)
        {
            if (!mod.IsEnabled) continue;

            var pluginFile = mod.PluginFilePath;
            if (pluginFile.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var disabledPath = pluginFile + ".disabled";
                File.Move(pluginFile, disabledPath);
                mod.PluginFilePath = disabledPath;
                toggled++;
            }
            catch (Exception ex)
            {
                Logger.Error($"禁用模组失败: {mod.Name}", ex);
            }
        }

        RefreshListDisplay();
        AppNotification.Show($"已禁用 {toggled} 个模组", NotificationType.Success);
    }

    private async void OnBatchDeleteClick(object? sender, RoutedEventArgs e)
    {
        var selected = ModListBox.SelectedItems!.Cast<ModInfo>().ToList();
        if (selected.Count == 0) return;

        if (AppConfig.Instance.ConfirmModDeletion)
        {
            _pendingBatchDelete = selected;
            DeleteConfirmText.Text = $"确定要删除选中的 {selected.Count} 个模组吗？\n此操作将删除整个模组目录，不可恢复。";
            DeleteConfirmPanel.IsVisible = true;
        }
        else
        {
            foreach (var mod in selected)
                DeleteMod(mod);

            ClearConfigPanel();
            LoadMods();
            AppNotification.Show($"已删除 {selected.Count} 个模组", NotificationType.Success);
        }
    }

    private void ShowError(string message)
    {
        ModListBox.IsVisible = false;
        EmptyHint.IsVisible = false;
        ErrorHint.Text = message;
        ErrorHint.IsVisible = true;
    }

    // ── 工具栏按钮 ──────────────────────────────────────────────

    /// <summary>弹出文件选择对话框，选择 .dll 或 .zip 模组文件进行安装</summary>
    private async void OnInstallLocalClick(object? sender, RoutedEventArgs e)
    {
        var parentWindow = this.VisualRoot as Window;
        if (parentWindow == null) return;

        var files = await parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择模组文件（.dll 或 .zip）",
            AllowMultiple = true,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("模组文件") { Patterns = new[] { "*.dll", "*.zip" } },
                new("所有文件") { Patterns = new[] { "*" } }
            }
        });

        if (files == null || files.Count == 0) return;

        var filePaths = files.Select(f => f.Path.LocalPath).ToArray();
        await InstallFilesAsync(filePaths);
    }

    /// <summary>在资源管理器中打开 BepInEx\plugins 模组文件夹</summary>
    private void OnOpenModFolderClick(object? sender, RoutedEventArgs e)
    {
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir))
        {
            AppNotification.Show("未找到游戏目录，请在设置页中配置", NotificationType.Warning);
            return;
        }

        var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            AppNotification.Show("BepInEx\\plugins 目录不存在", NotificationType.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(pluginsDir) { UseShellExecute = true });
    }

    /// <summary>在资源管理器中打开 BepInEx\config 配置文件夹</summary>
    private void OnOpenConfigFolderClick(object? sender, RoutedEventArgs e)
    {
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir))
        {
            AppNotification.Show("未找到游戏目录，请在设置页中配置", NotificationType.Warning);
            return;
        }

        var configDir = Path.Combine(gameDir, "BepInEx", "config");
        if (!Directory.Exists(configDir))
        {
            AppNotification.Show("BepInEx\\config 目录不存在", NotificationType.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(configDir) { UseShellExecute = true });
    }
}

/// <summary>双向值转换器辅助类</summary>
public class FuncConverter<TIn, TOut> : IValueConverter
{
    private readonly Func<TIn?, TOut?> _convert;
    private readonly Func<TOut?, TIn?>? _convertBack;

    public FuncConverter(Func<TIn?, TOut?> convert, Func<TOut?, TIn?>? convertBack = null)
    {
        _convert = convert;
        _convertBack = convertBack;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TIn tIn ? _convert(tIn) : _convert(default);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (_convertBack == null)
            throw new NotSupportedException();

        return value is TOut tOut ? _convertBack(tOut) : _convertBack(default);
    }
}
