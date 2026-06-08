using System.IO;
using System.Text.Json;

namespace ChipLauncher.Services;

/// <summary>
/// 应用配置管理（JSON 文件存储）
/// </summary>
public class AppConfig
{
    private static readonly string ConfigPath =
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "config.json");

    public string? GamePath { get; set; }

    /// <summary>加载配置，不存在则返回默认值</summary>
    public static AppConfig Load()
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

    /// <summary>保存配置到 JSON 文件</summary>
    public void Save()
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
}