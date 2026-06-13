using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ChipLauncher.Models;
using ChipLauncher.Services;
using SukiUI.Dialogs;

namespace ChipLauncher.Views;

/// <summary>
///     模组管理页面 — 扫描 BepInEx\plugins 子目录，支持启用/禁用切换、配置编辑和拖放安装
/// </summary>
public partial class ModsPage : UserControl
{
    private const string DefaultEmptyHint =
        "未找到 BepInEx 模组\n请确保游戏已安装 BepInEx\n且模组位于 BepInEx\\plugins 目录";

    private const string SearchNoResultHint =
        "未找到匹配模组，按 Enter 在 NexusMods 网页中搜索";
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

    /// <summary>启用/禁用 → 切换按钮背景色</summary>
    public static readonly IValueConverter ToggleColorConverter =
        new FuncConverter<bool, IBrush>(enabled => enabled
            ? new SolidColorBrush(Color.Parse("#2d6a2d"))
            : new SolidColorBrush(Color.Parse("#6a2d2d"))
        );

    /// <summary>通过 CheckBox 勾选进行批量操作的模组集合</summary>
    private readonly HashSet<ModInfo> _batchSelectedMods = new();

    private List<ModInfo>? _allMods;

    private BepInExConfig? _currentConfig;

    // ── 字段 ──────────────────────────────────────────────────

    private string? _gameDir;

    private ModInstaller? _installer;

    /// <summary>上一次点击的模组（用于 Shift 连选锚点）</summary>
    private ModInfo? _lastClickedMod;

    private List<ModInfo>? _pendingBatchDelete;

    private ModInfo? _selectedMod;

    private bool _sortAscending = true;

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
            ShowError("未找到 BepInEx\\plugins 目录。\n请确保游戏已安装 BepInEx 或存在 plugins 目录。");
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
                if (disabledFiles.Length <= 0) continue;

                {
                    // .disabled 文件本质就是重命名的 DLL，可直接读取元数据
                    var disabledPath = disabledFiles[0];
                    var (dGuid, dName, _) = ModInstaller.GetBepInPluginInfo(disabledPath);
                    if (string.IsNullOrEmpty(dGuid)) continue;

                    mods.Add(new ModInfo
                    {
                        Name = dName ?? Path.GetFileName(dir),
                        Guid = dGuid,
                        DirectoryPath = dir,
                        PluginFilePath = disabledPath
                    });
                }
            }

            ApplySort(mods);

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
        if (ModListBox.ItemsSource is not List<ModInfo> source || source.Count == 0)
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

        if (_batchSelectedMods.Count > 0)
            ModStatsText.Text += $" · 勾选 {_batchSelectedMods.Count} 个";
    }

    /// <summary>更新批量操作按钮的文字</summary>
    private void UpdateBatchButtonTexts()
    {
        BtnBatchEnable.Content = "启用选中";
        BtnBatchDisable.Content = "禁用选中";
        BtnBatchDelete.Content = "删除选中";
    }

    /// <summary>清除所有勾选，隐藏批量工具栏</summary>
    private void ClearBatchSelection()
    {
        foreach (var mod in _batchSelectedMods)
            mod.IsChecked = false;
        _batchSelectedMods.Clear();
        BatchToolbar.IsVisible = false;
        UpdateModStats();
    }

    /// <summary>强制刷新列表显示</summary>
    private void RefreshListDisplay()
    {
        if (ModListBox.ItemsSource is List<ModInfo> source)
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

    /// <summary>显示安装加载遮罩</summary>
    private void ShowInstallOverlay()
    {
        InstallOverlay.IsVisible = true;
        InstallOverlay.Opacity = 1;
    }

    /// <summary>隐藏安装加载遮罩</summary>
    private void HideInstallOverlay()
    {
        InstallOverlay.IsVisible = false;
        InstallOverlay.Opacity = 1;
    }

    /// <summary>供 MainWindow 拖放调用 — 批量安装文件并刷新列表</summary>
    public async Task InstallFilesAsync(IEnumerable<string> filePaths)
    {
        if (_installer == null)
        {
            AppNotification.Show("请先在设置页中配置游戏路径", NotificationType.Warning);
            return;
        }

        ShowInstallOverlay();

        try
        {
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

            switch (installed)
            {
                case > 0 when failed == 0:
                    AppNotification.Show($"已安装 {installed} 个模组", NotificationType.Success);
                    break;
                case > 0 when failed > 0:
                    AppNotification.Show($"安装 {installed} 个，{failed} 个失败", NotificationType.Warning);
                    break;
                default:
                    AppNotification.Show(lastMessage, NotificationType.Error);
                    break;
            }
        }
        finally
        {
            HideInstallOverlay();
        }
    }

    /// <summary>切换模组启用/禁用</summary>
    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModInfo mod }) return;

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
            if (ModListBox.ItemsSource is List<ModInfo> list)
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

    private void ApplySort(List<ModInfo> mods)
    {
        if (_sortAscending)
            mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        else
            mods.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     ListBox 指针按下事件
    ///     • 普通单击 → 选中查看配置 + 切换批量勾选
    ///     • Shift+单击 → 从上次点击到本次范围内，
    ///     全部已勾选则全部取消，否则全部勾选
    /// </summary>
    private void OnModListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var mod = FindClickedMod(e);
        if (mod == null) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _lastClickedMod != null)
        {
            // Shift+单击：范围批量选择
            if (ModListBox.ItemsSource is IList<ModInfo> source)
                ToggleBatchRange(mod, source);
        }
        else
        {
            // 普通单击：选中查看配置 + 切换批量选择（单选行为：替换）
            ModListBox.SelectedItem = mod;
            ToggleSingleSelect(mod);
        }

        UpdateBatchUI();
    }

    /// <summary>从 PointerPressed 事件源向上查找被点击的模组</summary>
    private static ModInfo? FindClickedMod(PointerPressedEventArgs e)
    {
        var src = e.Source as StyledElement;
        while (src != null)
        {
            if (src is ListBoxItem { DataContext: ModInfo mod })
                return mod;
            src = src.Parent;
        }

        return null;
    }

    /// <summary>
    ///     范围切换：从 _lastClickedMod 到 targetMod 之间
    ///     全部已勾选 → 全部取消；否则全部勾选
    /// </summary>
    private void ToggleBatchRange(ModInfo targetMod, IList<ModInfo> source)
    {
        var currentIdx = source.IndexOf(targetMod);
        if (_lastClickedMod == null) return;
        var lastIdx = source.IndexOf(_lastClickedMod);
        if (currentIdx < 0 || lastIdx < 0) return;

        var start = Math.Min(currentIdx, lastIdx);
        var end = Math.Max(currentIdx, lastIdx);

        // 判断范围内是否全部已勾选
        var allChecked = true;
        for (var i = start; i <= end; i++)
            if (!source[i].IsChecked)
            {
                allChecked = false;
                break;
            }

        // 全部已勾选 → 全部取消；否则全部勾选
        for (var i = start; i <= end; i++)
        {
            var rangeMod = source[i];
            var newState = !allChecked;
            rangeMod.IsChecked = newState;
            if (newState)
                _batchSelectedMods.Add(rangeMod);
            else
                _batchSelectedMods.Remove(rangeMod);
        }
    }

    /// <summary>
    ///     模组项点击 → 切换批量勾选（已有一个时替换选中）
    /// </summary>
    private void ToggleSingleSelect(ModInfo mod)
    {
        var newChecked = !mod.IsChecked;

        // 已选中 1 个且点击不同模组 → 替换
        if (_batchSelectedMods.Count == 1 && !_batchSelectedMods.Contains(mod))
        {
            var current = _batchSelectedMods.First();
            current.IsChecked = false;
            _batchSelectedMods.Clear();
        }

        mod.IsChecked = newChecked;
        if (newChecked)
            _batchSelectedMods.Add(mod);
        else
            _batchSelectedMods.Remove(mod);

        _lastClickedMod = mod;
    }

    /// <summary>
    ///     复选框点击 → 添加/移除（始终多选，不替换已有选中）
    /// </summary>
    private void ToggleMultiSelect(ModInfo mod, bool newCheckedState)
    {
        mod.IsChecked = newCheckedState;
        if (newCheckedState)
            _batchSelectedMods.Add(mod);
        else
            _batchSelectedMods.Remove(mod);

        _lastClickedMod = mod;
    }

    /// <summary>更新批量操作 UI（工具栏显隐、按钮文字、全选文本、统计）</summary>
    private void UpdateBatchUI()
    {
        BatchToolbar.IsVisible = _batchSelectedMods.Count > 0;
        UpdateBatchButtonTexts();
        var totalCount = (ModListBox.ItemsSource as IList<ModInfo>)?.Count ?? 0;
        BtnBatchSelectAll.Content = _batchSelectedMods.Count >= totalCount && totalCount > 0
            ? "全不选"
            : "全选";
        UpdateModStats();
    }

    /// <summary>
    ///     复选框点击 → 添加/移除勾选（始终多选）
    /// </summary>
    private void OnModCheckBoxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: ModInfo mod }) return;

        // 读取 CheckBox 视觉状态（点击后已更新），而非 mod.IsChecked（绑定可能尚未写入）
        var isChecked = sender is CheckBox cb && cb.IsChecked == true;
        ToggleMultiSelect(mod, isChecked);
        UpdateBatchUI();
    }

    /// <summary>反选：所有已勾选的取消，未勾选的勾选</summary>
    private void OnBatchInvertClick(object? sender, RoutedEventArgs e)
    {
        if (ModListBox.ItemsSource is not IList<ModInfo> source) return;

        foreach (var mod in source)
        {
            var newState = !mod.IsChecked;
            mod.IsChecked = newState;
            if (newState)
                _batchSelectedMods.Add(mod);
            else
                _batchSelectedMods.Remove(mod);
        }

        UpdateBatchUI();
    }

    private void OnModSelected(object? sender, SelectionChangedEventArgs e)
    {
        _pendingBatchDelete = null;
        var selectedCount = ModListBox.SelectedItems?.Count ?? 0;

        switch (selectedCount)
        {
            case 0:
                _selectedMod = null;
                ClearConfigPanel();
                return;
            case 1 when ModListBox.SelectedItems![0] is ModInfo mod:
                // 单选 → 显示配置面板
                _selectedMod = mod;
                LoadConfigForMod(mod);
                return;
        }
    }

    /// <summary>加载选中模组的 BepInEx 配置（根据 GUID 匹配 {BepInEx/config}/{GUID}.cfg）</summary>
    private void LoadConfigForMod(ModInfo mod)
    {
        // 始终显示模组名
        ConfigModName.Text = mod.Name;

        _gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(_gameDir))
        {
            ShowConfigUnavailable(mod, "未找到游戏目录。");
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
                    ConfigFileName.IsVisible = true;
                    ConfigDescription.IsVisible = false;
                    ConfigItemsControl.ItemsSource = cfg.Entries;
                    BtnSaveConfig.IsVisible = true;
                    BtnOpenConfig.IsVisible = true;
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
                    ConfigFileName.IsVisible = true;
                    ConfigDescription.IsVisible = false;
                    ConfigItemsControl.ItemsSource = cfg.Entries;
                    BtnSaveConfig.IsVisible = true;
                    BtnOpenConfig.IsVisible = true;
                    ConfigPanel.IsVisible = true;
                    NoSelectionHint.IsVisible = false;
                    return;
                }
            }
        }

        ShowConfigUnavailable(mod, "此模组没有配置文件。");
    }

    private void ShowConfigPanel(BepInExConfig config)
    {
        _currentConfig = config;
        ConfigItemsControl.ItemsSource = config.Entries;
        BtnSaveConfig.IsVisible = true;
        BtnOpenConfig.IsVisible = true;
        ConfigPanel.IsVisible = true;
        NoSelectionHint.IsVisible = false;
    }

    private void ShowConfigUnavailable(ModInfo mod, string reason)
    {
        ClearConfigPanel();

        // ClearConfigPanel 清空了 ConfigModName，此处恢复模组名
        ConfigModName.Text = mod.Name;

        ConfigItemsControl.ItemsSource = null;
        ConfigFileName.IsVisible = false;
        ConfigDescription.Text = reason;
        ConfigDescription.IsVisible = true;
        BtnSaveConfig.IsVisible = false;
        BtnOpenConfig.IsVisible = false;
        ConfigPanel.IsVisible = true;
        NoSelectionHint.IsVisible = false;
    }

    private void ClearConfigPanel()
    {
        ConfigPanel.IsVisible = false;
        NoSelectionHint.IsVisible = true;
        BtnSaveConfig.IsVisible = true;
        BtnOpenConfig.IsVisible = true;
        _currentConfig = null;
        DeleteConfirmPanel.IsVisible = false;
        ConfigModName.Text = "";
        ConfigFileName.IsVisible = false;
        ConfigDescription.IsVisible = false;
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
        if (_pendingBatchDelete is { Count: > 0 })
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

    private void OnSaveConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_currentConfig == null) return;

        var ok = _currentConfig.Save();
        AppNotification.Show(ok ? "配置已保存" : "配置保存失败",
            ok ? NotificationType.Success : NotificationType.Error);
    }

    /// <summary>重置配置到默认值</summary>
    private void OnResetConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_currentConfig == null) return;

        foreach (var entry in _currentConfig.Entries)
            if (!string.IsNullOrEmpty(entry.DefaultValue))
                entry.Value = entry.DefaultValue;

        var ok = _currentConfig.Save();
        if (ok)
        {
            // 刷新显示
            ConfigItemsControl.ItemsSource = null;
            ConfigItemsControl.ItemsSource = _currentConfig.Entries;
            AppNotification.Show("配置已重置为默认值", NotificationType.Success);
        }
        else
        {
            AppNotification.Show("配置重置失败", NotificationType.Error);
        }
    }

    /// <summary>用默认程序打开当前选中模组的配置文件</summary>
    private void OnOpenConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedMod == null) return;

        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir))
        {
            AppNotification.Show("未找到游戏目录", NotificationType.Warning);
            return;
        }

        var configDir = Path.Combine(gameDir, "BepInEx", "config");
        string? cfgPath = null;

        // 优先按 GUID 查找
        if (!string.IsNullOrEmpty(_selectedMod.Guid))
        {
            var path = Path.Combine(configDir, _selectedMod.Guid + ".cfg");
            if (File.Exists(path))
                cfgPath = path;
        }

        // 降级：在模组目录下查找 config/
        if (cfgPath == null)
        {
            var legacyCfgDir = Path.Combine(_selectedMod.DirectoryPath, "config");
            if (Directory.Exists(legacyCfgDir))
            {
                var files = Directory.GetFiles(legacyCfgDir, "*.cfg");
                if (files.Length > 0)
                    cfgPath = files[0];
            }
        }

        if (cfgPath == null)
        {
            AppNotification.Show("未找到配置文件", NotificationType.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(cfgPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("打开配置文件失败", ex);
            AppNotification.Show("打开配置文件失败", NotificationType.Error);
        }
    }

    // ── 批量操作 ────────────────────────────────────────────────

    private void OnBatchEnableClick(object? sender, RoutedEventArgs e)
    {
        var selected = _batchSelectedMods.ToList();
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

        ClearBatchSelection();
        RefreshListDisplay();
        AppNotification.Show($"已启用 {toggled} 个模组", NotificationType.Success);
    }

    private void OnBatchDisableClick(object? sender, RoutedEventArgs e)
    {
        var selected = _batchSelectedMods.ToList();
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

        ClearBatchSelection();
        RefreshListDisplay();
        AppNotification.Show($"已禁用 {toggled} 个模组", NotificationType.Success);
    }

    private async void OnBatchDeleteClick(object? sender, RoutedEventArgs e)
    {
        var selected = _batchSelectedMods.ToList();
        if (selected.Count == 0) return;

        // 弹窗确认
        var result = await MainWindow.DialogManager.CreateDialog()
            .WithTitle("删除选中模组")
            .WithContent($"确定要删除选中的 {selected.Count} 个模组吗？\n此操作将删除整个模组目录，不可恢复。")
            .WithActionButton("取消", _ => { }, true)
            .WithActionButton("确认删除", _ =>
            {
                foreach (var mod in selected)
                    DeleteMod(mod);

                ClearBatchSelection();
                ClearConfigPanel();
                LoadMods();
                AppNotification.Show($"已删除 {selected.Count} 个模组", NotificationType.Success);
            }, true, "Flat", "Accent")
            .TryShowAsync();
    }

    /// <summary>切换 A-Z / Z-A 排序</summary>
    private void OnSortOrderClick(object? sender, RoutedEventArgs e)
    {
        _sortAscending = !_sortAscending;
        BtnSortOrder.Content = _sortAscending ? "A-Z ▾" : "Z-A ▴";

        // 重新应用排序
        if (_allMods == null) return;
        ApplySort(_allMods);
        RefreshListDisplay();
    }

    /// <summary>全选 / 全不选 — 通过数据绑定同步 CheckBox 状态</summary>
    private void OnBatchSelectAllClick(object? sender, RoutedEventArgs e)
    {
        if (ModListBox.ItemsSource
                is not IList<ModInfo> source
            || source.Count == 0) return;

        // 如果当前已全选 → 全不选
        if (_batchSelectedMods.Count == source.Count)
        {
            _batchSelectedMods.Clear();
            BtnBatchSelectAll.Content = "全选";
            BatchToolbar.IsVisible = false;
            UpdateModStats();
            // 数据绑定自动更新所有 CheckBox 的 IsChecked
            foreach (var mod in source)
                mod.IsChecked = false;
            return;
        }

        // 否则全选
        _batchSelectedMods.Clear();
        foreach (var mod in source)
        {
            mod.IsChecked = true; // 数据绑定自动更新 CheckBox 视觉状态
            _batchSelectedMods.Add(mod);
        }

        BtnBatchSelectAll.Content = "全不选";
        BatchToolbar.IsVisible = true;
        UpdateBatchButtonTexts();
        UpdateModStats();
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
        if (VisualRoot is not Window parentWindow) return;

        var files = await parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择模组文件（.dll 或 .zip）",
            AllowMultiple = true,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("模组文件") { Patterns = ["*.dll", "*.zip"] },
                new("所有文件") { Patterns = ["*"] }
            }
        });

        if (files.Count == 0) return;

        var filePaths = files.Select(f => f.Path.LocalPath).ToArray();
        await InstallFilesAsync(filePaths);
    }

    /// <summary>在资源管理器中打开 BepInEx\plugins 模组文件夹</summary>
    private void OnOpenModFolderClick(object? sender, RoutedEventArgs e)
    {
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir))
        {
            AppNotification.Show("未找到游戏目录", NotificationType.Warning);
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
            AppNotification.Show("未找到游戏目录", NotificationType.Warning);
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
public class FuncConverter<TIn, TOut>(Func<TIn?, TOut?> convert, Func<TOut?, TIn?>? convertBack = null)
    : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TIn tIn ? convert(tIn) : convert(default);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (convertBack == null)
            throw new NotSupportedException();

        return value is TOut tOut ? convertBack(tOut) : convertBack(default);
    }
}