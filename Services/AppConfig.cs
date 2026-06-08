using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ChipLauncher.Services;

/// <summary>
/// 应用配置管理（JSON 文件存储）— 单例 + 自动保存（防抖 500ms）
/// </summary>
public class AppConfig : INotifyPropertyChanged
{
    private static readonly string ConfigPath =
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "config.json");

    /// <summary>全局唯一实例，首次访问时自动从文件加载</summary>
    public static AppConfig Instance { get; } = Load();

    // ── 属性 ──────────────────────────────────────────────────

    private string? _gamePath;
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

    private int _maxRetries = 5;
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
    /// 给 TextBox 绑定的字符串包装属性（避免 int → string 转换异常）。
    /// 空值 / 非法输入不更新。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string MaxRetriesText
    {
        get => _maxRetries.ToString();
        set
        {
            if (int.TryParse(value, out var parsed))
                MaxRetries = parsed;
        }
    }

    private int _textRotationInterval = 3;
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
    /// 给 TextBox 绑定的字符串包装属性（避免 int → string 转换异常）
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string TextRotationIntervalText
    {
        get => _textRotationInterval.ToString();
        set
        {
            if (int.TryParse(value, out var parsed))
                TextRotationInterval = parsed;
        }
    }

    // ── 私有构造（防止外部 new，只能通过 Load 创建） ──────────
    [System.Text.Json.Serialization.JsonConstructor]
    private AppConfig() { }

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

    // ── 防抖保存 ──────────────────────────────────────────────

    private CancellationTokenSource? _saveCts;
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 防抖保存：500ms 内没有被再次调用才会真正写入文件。
    /// 连续修改（如打字时）只会触发一次最终写入。
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
                Logger.Info($"配置已保存");
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

    // ── INotifyPropertyChanged ────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}