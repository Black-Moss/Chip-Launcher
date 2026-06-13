using System.Text;

namespace ChipLauncher.Services;

/// <summary>
///     表示 BepInEx .cfg 配置文件中的一个配置项
/// </summary>
public class ConfigEntry
{
    /// <summary>所属节（如 [General]）</summary>
    public string Section { get; init; } = string.Empty;

    /// <summary>键名</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>当前值</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>描述（## 注释）</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>设置类型（如 Boolean, String, Int32）</summary>
    public string SettingType { get; init; } = string.Empty;

    /// <summary>默认值</summary>
    public string DefaultValue { get; init; } = string.Empty;
}

/// <summary>
///     BepInEx .cfg 配置文件解析器（读取和写入）
/// </summary>
public class BepInExConfig
{
    private readonly string _filePath;

    private BepInExConfig(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>所有配置项</summary>
    public List<ConfigEntry> Entries { get; } = [];

    /// <summary>文件头部（文件开头的注释等）</summary>
    private string Header { get; set; } = string.Empty;

    /// <summary>获取配置文件名（不含路径）</summary>
    public string FileName => Path.GetFileName(_filePath);

    /// <summary>获取该配置对应的模组描述</summary>
    public string ModDescription
    {
        get
        {
            // 从 Header 中提取插件名和版本
            foreach (var entry in Entries
                         .Where(entry => !string.IsNullOrEmpty(entry.Description)))
                return entry.Description;
            return FileName;
        }
    }

    /// <summary>从文件加载配置</summary>
    public static BepInExConfig? Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var config = new BepInExConfig(filePath);
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);

            var currentSection = string.Empty;
            var currentDescription = string.Empty;
            var currentType = string.Empty;
            var currentDefault = string.Empty;
            var headerLines = new List<string>();

            var inHeader = true;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // 节头
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    currentSection = trimmed.Trim('[', ']');
                    inHeader = false;
                    continue;
                }

                // 描述注释（BepInEx 使用 ##）
                if (trimmed.StartsWith("##"))
                {
                    if (inHeader)
                        headerLines.Add(line);
                    else
                        currentDescription = trimmed.TrimStart('#').Trim();
                    continue;
                }

                // 类型注释
                if (trimmed.StartsWith("# Setting type:"))
                {
                    currentType = trimmed.Substring("# Setting type:".Length).Trim();
                    continue;
                }

                // 默认值注释
                if (trimmed.StartsWith("# Default value:"))
                {
                    currentDefault = trimmed.Substring("# Default value:".Length).Trim();
                    continue;
                }

                // 普通注释（跳过）
                if (trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;

                // 键值对
                if (trimmed.Contains('=') && !string.IsNullOrEmpty(currentSection))
                {
                    var eqIndex = trimmed.IndexOf('=');
                    var key = trimmed[..eqIndex].Trim();
                    var value = trimmed[(eqIndex + 1)..].Trim();

                    config.Entries.Add(new ConfigEntry
                    {
                        Section = currentSection,
                        Key = key,
                        Value = value,
                        Description = currentDescription,
                        SettingType = currentType,
                        DefaultValue = currentDefault
                    });

                    // 重置当前状态
                    currentDescription = string.Empty;
                    currentType = string.Empty;
                    currentDefault = string.Empty;
                }
            }

            config.Header = string.Join("\r\n", headerLines);
            return config;
        }
        catch (Exception ex)
        {
            Logger.Error($"读取配置文件失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>保存修改后的配置回文件</summary>
    public bool Save()
    {
        try
        {
            using var writer = new StreamWriter(_filePath, false, Encoding.UTF8);

            // 写入头部
            if (!string.IsNullOrEmpty(Header))
            {
                writer.WriteLine(Header);
                writer.WriteLine();
            }

            // 按 Section 分组写入
            var grouped = Entries
                .GroupBy(e => e.Section)
                .ToList();

            foreach (var group in grouped)
            {
                writer.WriteLine($"[{group.Key}]");
                writer.WriteLine();

                foreach (var entry in group)
                {
                    if (!string.IsNullOrEmpty(entry.Description)) writer.WriteLine($"## {entry.Description}");
                    if (!string.IsNullOrEmpty(entry.SettingType))
                        writer.WriteLine($"# Setting type: {entry.SettingType}");
                    if (!string.IsNullOrEmpty(entry.DefaultValue))
                        writer.WriteLine($"# Default value: {entry.DefaultValue}");
                    writer.WriteLine($"{entry.Key} = {entry.Value}");
                    writer.WriteLine();
                }
            }

            Logger.Info($"配置文件已保存: {_filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"保存配置文件失败: {ex.Message}");
            return false;
        }
    }
}