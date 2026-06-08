using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
            }
        }
        catch (Exception ex)
        {
            ShowError($"读取模组目录失败：{ex.Message}");
        }
    }

    /// <summary>搜索框文本变化 → 过滤模组列表，并控制提示文字</summary>
    private void OnModSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_allMods == null) return;

        var keyword = ModSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            ModListBox.ItemsSource = _allMods;
            EmptyHint.Text = DefaultEmptyHint;
            EmptyHint.Foreground = new SolidColorBrush(Color.Parse("#888888"));
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
    }

    /// <summary>搜索框按键 → Enter 时打开 NexusMods 网页搜索</summary>
    private void OnModSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        var keyword = ModSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword)) return;

        var domain = AppConfig.Instance.NexusModsGameDomain;
        if (string.IsNullOrEmpty(domain)) return;

        var url = $"https://www.nexusmods.com/games/{domain}/search?keyword={Uri.EscapeDataString(keyword)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>供 MainWindow 拖放调用 — 安装单个文件并刷新列表</summary>
    public async void InstallFile(string filePath)
    {
        await InstallFilesAsync([filePath]);
    }

    /// <summary>通用安装逻辑（安装 → 刷新 → 显示结果）</summary>
    private async Task InstallFilesAsync(IEnumerable<string> filePaths)
    {
        if (_installer == null)
        {
            ShowInstallResult("请先在设置页中配置游戏路径");
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
            ShowInstallResult($"✅ 已成功安装 {installed} 个模组");
        else if (installed > 0 && failed > 0)
            ShowInstallResult($"✅ 安装 {installed} 个，{failed} 个失败。原因：{lastMessage}");
        else
            ShowInstallResult($"❌ {lastMessage}");
    }

    /// <summary>在 ErrorHint 显示安装结果，数秒后自动消失</summary>
    private async void ShowInstallResult(string message)
    {
        ErrorHint.Text = message;
        ErrorHint.IsVisible = true;

        await Task.Delay(3000);
        ErrorHint.IsVisible = false;
    }

    /// <summary>切换模组启用/禁用</summary>
    private async void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ModInfo mod) return;

        var pluginFile = mod.PluginFilePath;

        try
        {
            if (File.Exists(pluginFile))
            {
                // .dll -> .disabled
                var disabledPath = pluginFile + ".disabled";
                File.Move(pluginFile, disabledPath);
                mod.PluginFilePath = disabledPath;
                mod.IsEnabled = false;
            }
            else if (File.Exists(pluginFile + ".disabled"))
            {
                // .disabled -> .dll
                var dllPath = pluginFile.Replace(".disabled", "");
                File.Move(pluginFile, dllPath);
                mod.PluginFilePath = dllPath;
                mod.IsEnabled = true;
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
        }
        catch (Exception ex)
        {
            ShowError($"切换失败：{ex.Message}");
        }
    }

    private void OnModSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ModInfo mod)
        {
            _selectedMod = null;
            ClearConfigPanel();
            return;
        }

        _selectedMod = mod;
        LoadConfigForMod(mod);
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

        if (_selectedMod == null) return;

        var modName = _selectedMod.Name;
        await Task.Run(() => DeleteMod(_selectedMod!));

        ClearConfigPanel();
        LoadMods();
        ShowInstallResult($"✅ 已删除模组「{modName}」");
    }

    private void OnCancelDeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteConfirmPanel.IsVisible = false;
    }

    /// <summary>删除模组目录（递归）并刷新列表</summary>
    private void DeleteModAndRefresh(ModInfo mod)
    {
        DeleteMod(mod);
        ClearConfigPanel();
        LoadMods();
        ShowInstallResult($"✅ 已删除模组「{mod.Name}」");
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
        SaveStatus.Text = ok ? "✅ 已保存" : "❌ 保存失败";
        SaveStatus.IsVisible = true;

        await Task.Delay(2000);
        SaveStatus.IsVisible = false;
    }

    private void ShowError(string message)
    {
        ModListBox.IsVisible = false;
        EmptyHint.IsVisible = false;
        ErrorHint.Text = message;
        ErrorHint.IsVisible = true;
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
