using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using ChipLauncher.Models;
using ChipLauncher.Services;

namespace ChipLauncher.Views;

/// <summary>
///     模组管理页面 — 扫描 BepInEx\plugins 子目录，支持启用/禁用切换和配置编辑
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

    private BepInExConfig? _currentConfig;

    // ── 字段 ──────────────────────────────────────────────────

    private string? _gameDir;

    // ── 页面逻辑 ──────────────────────────────────────────────

    public ModsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadMods();
    }

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

        try
        {
            var mods = new List<ModInfo>();

            foreach (var dir in Directory.GetDirectories(pluginsDir))
            {
                var dllFiles = Directory.GetFiles(dir, "*.dll");
                if (dllFiles.Length > 0)
                {
                    mods.Add(new ModInfo
                    {
                        Name = Path.GetFileName(dir),
                        DirectoryPath = dir,
                        PluginFilePath = dllFiles[0]
                    });
                    continue;
                }

                var disabledFiles = Directory.GetFiles(dir, "*.disabled");
                if (disabledFiles.Length > 0)
                    mods.Add(new ModInfo
                    {
                        Name = Path.GetFileName(dir),
                        DirectoryPath = dir,
                        PluginFilePath = disabledFiles[0]
                    });
            }

            mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            ModListBox.ItemsSource = mods;
            ModListBox.IsVisible = mods.Count > 0;
            EmptyHint.IsVisible = mods.Count == 0;
        }
        catch (Exception ex)
        {
            ShowError($"读取模组目录失败：{ex.Message}");
        }
    }

    /// <summary>切换模组启用/禁用</summary>
    private async void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ModInfo mod) return;

        try
        {
            var oldFile = mod.PluginFilePath;
            string newFile;

            if (mod.IsEnabled)
                newFile = Path.ChangeExtension(oldFile, ".disabled");
            else
                newFile = Path.ChangeExtension(oldFile, ".dll");

            File.Move(oldFile, newFile, true);
            Logger.Info($"模组 {(mod.IsEnabled ? "禁用" : "启用")}: {mod.Name}");

            // 清除右侧配置（避免选中项漂移）
            ClearConfigPanel();
            LoadMods();
        }
        catch (Exception ex)
        {
            Logger.Error($"切换模组状态失败: {ex.Message}");
            ShowError($"切换失败：{ex.Message}");
        }
    }

    /// <summary>选中模组 → 加载对应的配置文件</summary>
    private void OnModSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not ModInfo mod) return;

        LoadConfigForMod(mod);
    }

    /// <summary>查找并加载选定模组的配置文件</summary>
    private void LoadConfigForMod(ModInfo mod)
    {
        if (string.IsNullOrEmpty(_gameDir)) return;

        var configDir = Path.Combine(_gameDir, "BepInEx", "config");
        if (!Directory.Exists(configDir))
        {
            ShowConfigUnavailable("未找到 BepInEx\\config 目录");
            return;
        }

        // 扫描 config 目录下所有 .cfg 文件
        var cfgFiles = Directory.GetFiles(configDir, "*.cfg");
        if (cfgFiles.Length == 0)
        {
            ShowConfigUnavailable("未找到任何配置文件");
            return;
        }

        // 尝试匹配：找文件名包含模组名的 cfg 文件
        var modNameNormalized = mod.Name.Replace(" ", "").Replace("-", "").Replace("_", "")
            .ToLowerInvariant();

        var matchedFile = cfgFiles.FirstOrDefault(cfg =>
        {
            var cfgName = Path.GetFileNameWithoutExtension(cfg)
                .Replace(" ", "").Replace("-", "").Replace("_", "")
                .ToLowerInvariant();
            return cfgName.Contains(modNameNormalized) || modNameNormalized.Contains(cfgName);
        });

        if (matchedFile == null)
        {
            ShowConfigUnavailable("此模组没有配置文件");
            return;
        }

        var config = BepInExConfig.Load(matchedFile);
        if (config == null)
        {
            ShowConfigUnavailable($"无法读取配置文件\n{Path.GetFileName(matchedFile)}");
            return;
        }

        _currentConfig = config;
        ShowConfigPanel(config);
    }

    /// <summary>显示配置编辑器</summary>
    private void ShowConfigPanel(BepInExConfig config)
    {
        ConfigFileName.Text = config.FileName;
        ConfigDescription.Text = config.Entries.Count > 0
            ? $"共 {config.Entries.Count} 个设置项"
            : "此配置文件没有可编辑的设置项";

        ConfigItemsControl.ItemsSource = config.Entries;
        BtnSaveConfig.IsVisible = config.Entries.Count > 0;
        SaveStatus.IsVisible = false;
        ConfigPanel.IsVisible = true;
        NoSelectionHint.IsVisible = false;
    }

    /// <summary>显示"配置不可用"提示（隐藏保存按钮和编辑区）</summary>
    private void ShowConfigUnavailable(string reason)
    {
        _currentConfig = null;
        ConfigFileName.Text = reason;
        ConfigDescription.Text = string.Empty;
        ConfigItemsControl.ItemsSource = null;
        BtnSaveConfig.IsVisible = false;
        SaveStatus.IsVisible = false;
        ConfigPanel.IsVisible = true;
        NoSelectionHint.IsVisible = false;
    }

    /// <summary>清空配置面板</summary>
    private void ClearConfigPanel()
    {
        _currentConfig = null;
        ConfigPanel.IsVisible = false;
        NoSelectionHint.IsVisible = true;
    }

    /// <summary>保存当前配置</summary>
    private async void OnSaveConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_currentConfig == null) return;

        if (_currentConfig.Save())
        {
            SaveStatus.Text = "✅ 配置已保存";
            SaveStatus.IsVisible = true;

            // 3 秒后隐藏提示
            await Task.Delay(3000);
            SaveStatus.IsVisible = false;
        }
        else
        {
            SaveStatus.Text = "❌ 保存失败";
            SaveStatus.Foreground = new SolidColorBrush(Color.Parse("#e74c3c"));
            SaveStatus.IsVisible = true;
        }
    }

    private void ShowError(string message)
    {
        ModListBox.IsVisible = false;
        EmptyHint.IsVisible = false;
        ErrorHint.Text = message;
        ErrorHint.IsVisible = true;
    }
}

/// <summary>
///     简单的 FuncConverter，将 Lambda 转换为 IValueConverter
/// </summary>
public class FuncConverter<TIn, TOut> : IValueConverter
{
    private readonly Func<TIn, TOut> _convert;
    private readonly Func<TOut, TIn>? _convertBack;

    public FuncConverter(Func<TIn, TOut> convert, Func<TOut, TIn>? convertBack = null)
    {
        _convert = convert;
        _convertBack = convertBack;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TIn input)
            return _convert(input);
        return default(TOut);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (_convertBack != null && value is TOut input)
            return _convertBack(input);
        throw new NotSupportedException();
    }
}