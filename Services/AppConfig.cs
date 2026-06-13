using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChipLauncher.Services;

/// <summary>
///     应用配置管理（JSON 文件存储）— 单例 + 自动保存（防抖 500ms）
/// </summary>
public class AppConfig : INotifyPropertyChanged
{
    private static readonly string ConfigPath =
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "config.json");

    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);
    private bool _autoCheckUpdates = true;
    private bool _confirmModDeletion = true;
    private bool _infiniteScroll;
    private int _enabledSkinId = -1;

    private string _defaultPage = "News";

    // ── 属性 ──────────────────────────────────────────────────

    private string? _gamePath;

    private int _maxRetries = 5;

    // ── 防抖保存 ──────────────────────────────────────────────

    private CancellationTokenSource? _saveCts;

    private int _textRotationInterval = 3;

    // ── 私有构造（防止外部 new，只能通过 Load 创建） ──────────
    [JsonConstructor]
    private AppConfig()
    {
    }

    /// <summary>全局唯一实例，首次访问时自动从文件加载</summary>
    public static AppConfig Instance { get; } = Load();

    public string? GamePath
    {
        get => _gamePath;
        set
        {
            if (_gamePath == value) return;
            _gamePath = value;
            OnPropertyChanged();
            Save(); // 变更后自动保存
        }
    }

    /// <summary>启动时默认显示的页面（News / Mods / Settings，默认 News）</summary>
    public string DefaultPage
    {
        get => _defaultPage;
        set
        {
            var valid = new[] { "News", "Mods", "Settings" };
            var clamped = Array.Exists(valid, v => v == value) ? value : "News";
            if (_defaultPage == clamped) return;
            _defaultPage = clamped;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>资讯获取失败时的最大重试次数（默认 5，JSON 序列化用此属性）</summary>
    public int MaxRetries
    {
        get => _maxRetries;
        set
        {
            var clamped = Math.Clamp(value, 1, 20);
            if (_maxRetries == clamped) return;
            _maxRetries = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaxRetriesText));
            Save();
        }
    }

    /// <summary>
    ///     给 TextBox 绑定的字符串包装属性（避免 int → string 转换异常）。
    ///     空值 / 非法输入不更新。
    /// </summary>
    [JsonIgnore]
    public string MaxRetriesText
    {
        get => _maxRetries.ToString();
        set
        {
            if (int.TryParse(value, out var parsed))
                MaxRetries = parsed;
        }
    }

    /// <summary>ComboBox 绑定的索引（0=News, 1=Mods, 2=ModDownload, 3=Settings），不序列化</summary>
    [JsonIgnore]
    public int DefaultPageIndex
    {
        get
        {
            var pages = new[] { "News", "Mods", "ModDownload", "Settings" };
            return Math.Max(0, Array.IndexOf(pages, _defaultPage));
        }
        set
        {
            var pages = new[] { "News", "Mods", "ModDownload", "Settings" };
            if (value >= 0 && value < pages.Length)
                DefaultPage = pages[value];
        }
    }

    /// <summary>删除模组前是否显示二次确认</summary>
    public bool ConfirmModDeletion
    {
        get => _confirmModDeletion;
        set
        {
            if (_confirmModDeletion == value) return;
            _confirmModDeletion = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>皮肤下载页是否启用无底滚动（自动加载下一页）</summary>
    public bool InfiniteScroll
    {
        get => _infiniteScroll;
        set
        {
            if (_infiniteScroll == value) return;
            _infiniteScroll = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>启用的皮肤 ID（-1 表示未启用任何皮肤），单选</summary>
    public int EnabledSkinId
    {
        get => _enabledSkinId;
        set
        {
            if (_enabledSkinId == value) return;
            _enabledSkinId = value;
            OnPropertyChanged();
            Save();
        }
    }


    /// <summary>启动时是否自动检查更新</summary>
    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set
        {
            if (_autoCheckUpdates == value) return;
            _autoCheckUpdates = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>标题栏游戏文本轮播间隔（秒，默认 3，JSON 序列化用此属性）</summary>
    public int TextRotationInterval
    {
        get => _textRotationInterval;
        set
        {
            var clamped = Math.Clamp(value, 1, 60);
            if (_textRotationInterval == clamped) return;
            _textRotationInterval = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TextRotationIntervalText));
            Save();
        }
    }

    /// <summary>
    ///     给 TextBox 绑定的字符串包装属性（避免 int → string 转换异常）
    /// </summary>
    [JsonIgnore]
    public string TextRotationIntervalText
    {
        get => _textRotationInterval.ToString();
        set
        {
            if (int.TryParse(value, out var parsed))
                TextRotationInterval = parsed;
        }
    }

    // ── INotifyPropertyChanged ────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>从 JSON 文件加载，不存在则返回默认值</summary>
    private static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                Logger.Info($"加载配置文件: {ConfigPath}");
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }

            Logger.Info("配置文件不存在，使用默认配置");
        }
        catch (Exception ex)
        {
            Logger.Error("加载配置文件失败", ex);
        }

        return new AppConfig();
    }

    /// <summary>
    ///     防抖保存：500ms 内没有被再次调用才会真正写入文件。
    ///     连续修改（如打字时）只会触发一次最终写入。
    /// </summary>
    private void Save()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDebounce, token);

                var dir = Path.GetDirectoryName(ConfigPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                Logger.Info("配置已保存");
            }
            catch (TaskCanceledException)
            {
                // 被后续修改取消，不需要处理
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置文件失败", ex);
            }
        }, token);
    }

    /// <summary>重置所有配置为默认值并保存</summary>
    public void Reset()
    {
        _gamePath = null;
        _defaultPage = "News";
        _maxRetries = 5;
        _confirmModDeletion = true;
        _autoCheckUpdates = true;
        _textRotationInterval = 3;

        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(DefaultPage));
        OnPropertyChanged(nameof(DefaultPageIndex));
        OnPropertyChanged(nameof(MaxRetries));
        OnPropertyChanged(nameof(MaxRetriesText));
        OnPropertyChanged(nameof(ConfirmModDeletion));
        OnPropertyChanged(nameof(AutoCheckUpdates));
        OnPropertyChanged(nameof(TextRotationInterval));
        OnPropertyChanged(nameof(TextRotationIntervalText));

        Save();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}