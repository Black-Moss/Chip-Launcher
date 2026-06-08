using System.Text.Json;
using Microsoft.Win32;

namespace ChipLauncher.Services;

/// <summary>
///     读取游戏本地化文件（zh-CN.json）并提取指定键的文本
///     支持：
///     - 顶层键（string 值）
///     - "main" 子对象中的键（string 值）
///     - "character" 数组中的对话场景（从数组随机取一行）
/// </summary>
public static class GameLocalization
{
    /// <summary>要提取的键列表（只显示这些键对应的文本）</summary>
    /// <remarks>
    ///     键的来源：
    ///     顶层键：如 "startlorenote"、"name"
    ///     对话场景键（character[] 中每个对象的字段名）：
    ///     如 "seeGravel"、"hungry"、"thirsty"、"tired"、"confused"、"eatGood"、"wakeup" 等
    ///     可在 CasualtiesUnknown_Data\Lang\zh-CN.json 中查看实际键名
    ///     提示：对话场景键会从数组中随机选取一行显示
    /// </remarks>
    private static readonly string[] DisplayKeys =
    [
        // ── 对话场景（随机取一行显示）─────────────────
        "seeGravel",
        "eatGood",
        "eatMediocre",
        "eatBad",
        "refuseEat",
        "tired",
        "verytired",
        "confused",
        "wakeup",
        "sick",
        "verysick",
        "vomit",
        "vomitblood",
        "full",
        "hungry",
        "starving",
        "thirsty",
        "dehydrated",
        "limbmuscle",
        "limbinfected",
        "limbskin",
        "sad",
        "gloomy",
        "depressed",
        "miserable",
        "selfharm",
        "suicide",
        "refuse",
        "bleeding",
        "bleedingheavy",
        "steponglass",
        "seecorpse",
        "seecorpsedesensitized",
        "seecorpsesuicidal",
        "breakcorpse",
        "cold",
        "warm",
        "hot",
        "exerted",
        "exhausted",
        "freezing",
        "emaciated",
        "obese",
        "opiated",
        "opiatedsad",
        "opiatewithdrawal",
        "cantBreathe",
        "pain",
        "hitgroundhard",
        "bigpain",
        "fallscream",
        "hitbycreature",
        "wet",
        "dirty",
        "encumbered"
    ];

    /// <summary>缓存的根 JSON 文档</summary>
    private static JsonDocument? _cachedDoc;

    private static DateTime _lastRead = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>随机数生成器（用于选取对话行）</summary>
    private static readonly Random Rng = new();

    /// <summary>获取游戏目录路径</summary>
    public static string? GetGameDirectory()
    {
        // 1. 优先用 AppConfig 中设置的路径
        var gamePath = AppConfig.Instance.GamePath;
        if (!string.IsNullOrEmpty(gamePath) && File.Exists(gamePath))
            return Path.GetDirectoryName(gamePath);

        // 2. 通过 Steam 查找安装目录
        return FindSteamGamePath();
    }

    /// <summary>检查游戏目录是否已安装 BepInEx（检测 BepInEx/core/ 是否存在）</summary>
    public static bool IsBepInExInstalled()
    {
        var gameDir = GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir)) return false;

        var bepInExCore = Path.Combine(gameDir, "BepInEx", "core");
        return Directory.Exists(bepInExCore) && Directory.GetFiles(bepInExCore, "*.dll").Length > 0;
    }

    /// <summary>从 Steam 注册表/库查找游戏安装路径</summary>
    private static string? FindSteamGamePath()
    {
        // Steam 注册表操作仅在 Windows 上支持
        if (!OperatingSystem.IsWindows())
        {
            Logger.Warn("Steam 路径查找仅在 Windows 上支持");
            return null;
        }

        try
        {
            // 从注册表获取 Steam 安装目录
            using var steamKey = Registry.CurrentUser
                .OpenSubKey(@"Software\Valve\Steam");
            var steamPath = steamKey?.GetValue("SteamPath")?.ToString();
            if (string.IsNullOrEmpty(steamPath)) return null;

            // 读取 libraryfolders.vdf，获取所有 Steam 库路径
            var libraryPaths = new List<string> { Path.GetFullPath(steamPath) }; // Steam 安装目录本身也是一个库
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

            if (File.Exists(vdfPath))
            {
                var vdfLines = File.ReadAllLines(vdfPath);
                foreach (var line in vdfLines)
                {
                    var trimmed = line.Trim();
                    // 匹配形如: "path"  "X:\\Some\\Path"
                    if (trimmed.StartsWith("\"path\"") && trimmed.IndexOf('"', 7) >= 0)
                    {
                        var start = trimmed.IndexOf('"', 7) + 1;
                        var end = trimmed.LastIndexOf('"');
                        if (start > 0 && end > start)
                        {
                            var libPath = trimmed.Substring(start, end - start)
                                .Replace(@"\\", "\\"); // VDF 中使用双反斜杠
                            if (!libraryPaths.Contains(libPath, StringComparer.OrdinalIgnoreCase))
                                libraryPaths.Add(libPath);
                        }
                    }
                }
            }

            // 在每个库的 steamapps/common 中查找游戏目录
            foreach (var dir
                     in from lib
                         in libraryPaths
                     select Path.Combine(lib, "steamapps", "common")
                     into commonPath
                     where Directory.Exists(commonPath)
                     from dir
                         in Directory.GetDirectories(commonPath)
                     let dataDir = Path.Combine(dir, "CasualtiesUnknown_Data")
                     where Directory.Exists(dataDir)
                     select dir)
                return dir;

            Logger.Warn("在所有 Steam 库中未找到 CasualtiesUnknown_Data 目录");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Steam 路径查找失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>获取本地化文件路径</summary>
    private static string? GetJsonPath()
    {
        var gameDir = GetGameDirectory();
        if (gameDir == null) return null;

        var jsonPath = Path.Combine(gameDir, "CasualtiesUnknown_Data", "Lang", "zh-CN.json");
        return File.Exists(jsonPath) ? jsonPath : null;
    }

    /// <summary>重新加载 JSON（清空缓存）</summary>
    public static void Reload()
    {
        _cachedDoc?.Dispose();
        _cachedDoc = null;
        _lastRead = DateTime.MinValue;
    }

    /// <summary>读取 JSON 并返回指定键的显示文本列表</summary>
    public static List<string> GetDisplayTexts()
    {
        var result = new List<string>();

        try
        {
            // 检查文件缓存
            if (_cachedDoc != null && DateTime.UtcNow - _lastRead < CacheDuration)
                return ExtractDisplayTexts(_cachedDoc, result);

            var jsonPath = GetJsonPath();
            if (jsonPath == null) return result;

            var json = File.ReadAllText(jsonPath);

            // 释放旧文档
            _cachedDoc?.Dispose();

            _cachedDoc = JsonDocument.Parse(json);
            _lastRead = DateTime.UtcNow;
            return ExtractDisplayTexts(_cachedDoc, result);
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取本地化文件失败: {ex.Message}");
            return result;
        }
    }

    /// <summary>从 JsonDocument 中提取 DisplayKeys 对应的文本</summary>
    private static List<string> ExtractDisplayTexts(JsonDocument doc, List<string> result)
    {
        var root = doc.RootElement;

        foreach (var key in DisplayKeys)
        {
            var text = FindText(root, key);
            if (text != null)
                result.Add(text);
        }

        return result;
    }

    /// <summary>
    ///     在 JSON 中递归查找指定键对应的文本：
    ///     1. 顶层直接匹配 → string → 直接返回
    ///     2. "main" 子对象中匹配 → string → 直接返回
    ///     3. "character" 数组中匹配 → 数组 → 随机取一行
    ///     4. 如果未找到且在 character 数组中 → 跳过（可能是不含此键的角色对象）
    /// </summary>
    private static string? FindText(JsonElement root, string key)
    {
        // 1. 顶层直接匹配
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty(key, out var topValue))
        {
            var text = ExtractStringValue(topValue);
            if (text != null) return text;
        }

        // 2. "main" 子对象中匹配
        if (root.TryGetProperty("main", out var mainObj) && mainObj.ValueKind == JsonValueKind.Object)
            if (mainObj.TryGetProperty(key, out var mainValue))
            {
                var text = ExtractStringValue(mainValue);
                if (text != null) return text;
            }

        // 3. "character" 数组中匹配（每个角色对象的对话场景）
        if (!root.TryGetProperty("character", out var charArray) ||
            charArray.ValueKind != JsonValueKind.Array) return null;
        foreach (var charObj in charArray.EnumerateArray()
                     .Where(charObj => charObj.ValueKind == JsonValueKind.Object))
            if (charObj.TryGetProperty(key, out var dialogueArray) &&
                dialogueArray.ValueKind == JsonValueKind.Array)
                return PickRandomLine(dialogueArray);

        return null;
    }

    /// <summary>从 JsonElement 中提取字符串（如果是数组则随机取一行）</summary>
    private static string? ExtractStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => PickRandomLine(element),
            _ => null
        };
    }

    /// <summary>从 JSON 字符串数组中随机取一行</summary>
    private static string? PickRandomLine(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array) return null;

        var count = array.GetArrayLength();
        if (count == 0) return null;

        var index = Rng.Next(count);
        var item = array[index];

        return item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    }
}