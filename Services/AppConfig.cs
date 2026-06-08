using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ChipLauncher.Services;

/// <summary>
/// 应用配置管理（JSON 文件存储）— 单例 + 自动保存
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
    /// <summary>资讯获取失败时的最大重试次数（默认 5）</summary>
    public int MaxRetries
    {
        get => _maxRetries;
        set
        {
            if (_maxRetries == value) return;
            _maxRetries = Math.Clamp(value, 1, 20);
            OnPropertyChanged();
            Save();
        }
    }

    private int _textRotationInterval = 3;
    /// <summary>标题栏游戏文本轮播间隔（秒，默认 3，范围 1~60）</summary>
    public int TextRotationInterval
    {
        get => _textRotationInterval;
        set
        {
            if (_textRotationInterval == value) return;
            _textRotationInterval = Math.Clamp(value, 1, 60);
            OnPropertyChanged();
            Save();
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

    /// <summary>保存到 JSON 文件</summary>
    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            Logger.Info($"配置已保存: GamePath={GamePath ?? "(未设置)"}");
        }
        catch (Exception ex)
        {
            Logger.Error("保存配置文件失败", ex);
        }
    }

    // ── INotifyPropertyChanged ────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}