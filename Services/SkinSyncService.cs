using System.Text;
using ChipLauncher.Models;

namespace ChipLauncher.Services;

/// <summary>
///     将所有本地皮肤同步到游戏目录下的 CustomSprites（供 SkinSync Mod 使用），
///     并独立更新 skinsync.cfg 的 CurrentSkin 配置。
///     所有皮肤统一用名称做目录名，而非数字 ID。
/// </summary>
public static partial class SkinSyncService
{
    private const string SkinSyncFileName = "com.Bytechey.skinsync.cfg";
    private static bool _warningShownThisSession;

    /// <summary>检测 SkinSync 模组是否已安装（检查 BepInEx/plugins 下是否存在 SkinSync.dll）。</summary>
    public static bool IsSkinSyncModInstalled()
    {
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir)) return false;

        var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
        if (!Directory.Exists(pluginsDir)) return false;

        // 1. 直接放在 plugins 根目录
        if (File.Exists(Path.Combine(pluginsDir, "SkinSync.dll"))) return true;

        // 2. 放在 "Skin Sync Mod" 子目录（最常见方式）
        if (File.Exists(Path.Combine(pluginsDir, "Skin Sync Mod", "SkinSync.dll"))) return true;

        // 3. 其他一级子目录
        try
        {
            if (Directory
                .GetDirectories(pluginsDir)
                .Any(subDir => File.Exists(Path.Combine(subDir, "SkinSync.dll"))))
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    /// <summary>检查本轮会话是否已显示过警告。</summary>
    public static bool IsWarningShownThisSession() => _warningShownThisSession;

    /// <summary>标记会话警告已显示。</summary>
    public static void MarkWarningShown() => _warningShownThisSession = true;

    /// <summary>将全部本地皮肤同步到游戏 CustomSprites 目录（名称目录）。</summary>
    public static void SyncAllToGame()
    {
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir))
        {
            Logger.Warn("[SkinSync] 未找到游戏目录，跳过同步");
            return;
        }

        var customSpritesDir = Path.Combine(gameDir, "BepInEx", "plugins", "CustomSprites");
        if (!Directory.Exists(customSpritesDir))
        {
            try { Directory.CreateDirectory(customSpritesDir); }
            catch (Exception ex)
            {
                Logger.Error("[SkinSync] 无法创建 CustomSprites 目录", ex);
                return;
            }
        }

        // 扫描所有本地皮肤
        var localSkins = LocalSkinReader.ScanLocalSkins();
        if (localSkins.Count == 0)
        {
            Logger.Info("[SkinSync] 无本地皮肤可同步");
            return;
        }

        var syncedCount = localSkins.Count(skin => SyncOneSkin(skin, customSpritesDir));

        Logger.Info($"[SkinSync] 同步完成：{syncedCount}/{localSkins.Count} 个皮肤 → CustomSprites/");
    }

    /// <summary>将全部本地皮肤同步到游戏（异步版，支持进度回调）。</summary>
    public static async Task SyncAllToGameAsync(IProgress<(int Current, int Total, string SkinName)>? progress = null)
    {
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir))
        {
            Logger.Warn("[SkinSync] 未找到游戏目录，跳过同步");
            return;
        }

        var customSpritesDir = Path.Combine(gameDir, "BepInEx", "plugins", "CustomSprites");
        if (!Directory.Exists(customSpritesDir))
        {
            try { Directory.CreateDirectory(customSpritesDir); }
            catch (Exception ex)
            {
                Logger.Error("[SkinSync] 无法创建 CustomSprites 目录", ex);
                return;
            }
        }

        // 扫描所有本地皮肤
        var localSkins = LocalSkinReader.ScanLocalSkins();
        if (localSkins.Count == 0)
        {
            Logger.Info("[SkinSync] 无本地皮肤可同步");
            return;
        }

        var syncedCount = 0;
        var total = localSkins.Count;

        await Task.Run(() =>
        {
            for (var i = 0; i < total; i++)
            {
                var skin = localSkins[i];
                if (SyncOneSkin(skin, customSpritesDir))
                    syncedCount++;

                progress?.Report((i + 1, total, skin.Name));
            }
        });

        Logger.Info($"[SkinSync] 同步完成：{syncedCount}/{total} 个皮肤 → CustomSprites/");
    }

    /// <summary>将 CurrentSkin 写入 skinsync.cfg（不涉及文件复制）。</summary>
    public static void SetCurrentSkin(string skinName)
    {
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir))
        {
            Logger.Warn("[SkinSync] 未找到游戏目录，跳过设置 CurrentSkin");
            return;
        }

        UpdateSkinSyncConfig(gameDir, SanitizeFolderName(skinName));
    }

    /// <summary>将 CurrentSkin 置空（不使用任何皮肤时调用）。</summary>
    public static void ClearCurrentSkin()
    {
        var gameDir = GameLocalization.GetGameDirectory();
        if (string.IsNullOrEmpty(gameDir)) return;

        UpdateSkinSyncConfig(gameDir, "");
    }

    /// <summary>同步单个皮肤到 CustomSprites。</summary>
    private static bool SyncOneSkin(SkinDownloadItem skin, string customSpritesDir)
    {
        // 源：本地 skins/{Id}/Body/
        var sourceBodyDir = LocalSkinReader.GetBodyDirectory(skin.Id);
        if (sourceBodyDir == null)
        {
            Logger.Warn($"[SkinSync] 皮肤 #{skin.Id} ({skin.Name}) 无本地 Body 目录，跳过");
            return false;
        }

        var skinFolderName = SanitizeFolderName(skin.Name);
        if (string.IsNullOrWhiteSpace(skinFolderName))
        {
            Logger.Warn($"[SkinSync] 皮肤名称无效: {skin.Name}");
            return false;
        }

        var targetSkinDir = Path.Combine(customSpritesDir, skinFolderName);
        var targetBodyDir = Path.Combine(targetSkinDir, "Body");

        try
        {
            // 先清除旧的目录（防残留）
            if (Directory.Exists(targetSkinDir))
                Directory.Delete(targetSkinDir, true);

            // 复制 Body 目录
            CopyDirectory(sourceBodyDir, targetBodyDir);
            Logger.Info($"[SkinSync] ✓ {skin.Name} → CustomSprites/{skinFolderName}/Body/");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[SkinSync] 同步「{skin.Name}」失败", ex);
            return false;
        }
    }

    /// <summary>更新 skinsync.cfg 中的 CurrentSkin 配置项</summary>
    private static void UpdateSkinSyncConfig(string gameDir, string skinName)
    {
        var configPath = Path.Combine(gameDir, "BepInEx", "config", SkinSyncFileName);
        if (!File.Exists(configPath))
        {
            Logger.Warn($"[SkinSync] 未找到 {SkinSyncFileName}，跳过配置更新");
            return;
        }

        try
        {
            var lines = File.ReadAllLines(configPath, Encoding.UTF8);
            var changed = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!trimmed.StartsWith("CurrentSkin", StringComparison.OrdinalIgnoreCase) ||
                    !trimmed.Contains('=')) continue;
                lines[i] = $"CurrentSkin = {skinName}";
                changed = true;
                break;
            }

            if (changed)
            {
                File.WriteAllLines(configPath, lines, Encoding.UTF8);
                Logger.Info($"[SkinSync] 已更新 CurrentSkin = {(string.IsNullOrEmpty(skinName) ? "(空)" : skinName)}");
            }
            else
            {
                Logger.Warn($"[SkinSync] 未在 {SkinSyncFileName} 中找到 CurrentSkin 配置项");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[SkinSync] 更新 {SkinSyncFileName} 失败", ex);
        }
    }

    /// <summary>清理文件夹名称，移除非法路径字符</summary>
    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Create(name.Length, (name, invalid), (span, state) =>
        {
            var i = 0;
            foreach (var c in state.name)
                span[i++] = state.invalid.Contains(c) ? '_' : c;
        });
        sanitized = sanitized.Trim();
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized.All(c => c == '.'))
            return "_unknown";
        return sanitized;
    }

    /// <summary>递归复制目录</summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }
}
