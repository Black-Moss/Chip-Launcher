using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ChipLauncher.Services;

/// <summary>
///     模组安装器 — 处理拖放安装，支持 .dll / .zip / .rar / .7z
/// </summary>
public class ModInstaller
{
    private readonly string _pluginsDir;

    public ModInstaller(string pluginsDir)
    {
        _pluginsDir = pluginsDir ?? throw new ArgumentNullException(nameof(pluginsDir));
    }

    /// <summary>安装模组文件（dll 或压缩包）</summary>
    public async Task<(bool Success, string Message)> InstallAsync(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".dll" => await InstallDllAsync(filePath),
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => await InstallArchiveAsync(filePath),
                _ => (false, $"不支持的文件格式：{ext}")
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"安装失败: {filePath}", ex);
            return (false, $"安装失败：{ex.Message}");
        }
    }

    // ── DLL 安装 ──────────────────────────────────────────────

    private async Task<(bool Success, string Message)> InstallDllAsync(string dllPath)
    {
        var folderName = await Task.Run(() => GetBepInPluginName(dllPath));
        if (string.IsNullOrEmpty(folderName))
            return (false, "无法识别模组：DLL 文件中未找到 [BepInPlugin] 特性");

        var targetDir = Path.Combine(_pluginsDir, folderName);
        var targetPath = Path.Combine(targetDir, Path.GetFileName(dllPath));

        if (File.Exists(targetPath))
            return (false, $"模组「{folderName}」已存在");

        Directory.CreateDirectory(targetDir);
        await Task.Run(() => File.Copy(dllPath, targetPath));
        return (true, $"✅ 模组「{folderName}」安装成功");
    }

    // ── 压缩包安装 ────────────────────────────────────────────

    private async Task<(bool Success, string Message)> InstallArchiveAsync(string archivePath)
    {
        // 先判断是否包含 BepInEx/plugins/ 目录结构
        var hasBepInEx = await Task.Run(() =>
        {
            using var stream = File.OpenRead(archivePath);
            using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions());
            return archive.Entries.Any(e =>
            {
                var key = e.Key?.Replace('\\', '/') ?? "";
                return key.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase)
                       && key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            });
        });

        return hasBepInEx
            ? await InstallBepInExArchiveAsync(archivePath)
            : await InstallRegularArchiveAsync(archivePath);
    }

    /// <summary>压缩包内含 BepInEx/plugins/ → 整体复制插件目录结构</summary>
    private async Task<(bool Success, string Message)> InstallBepInExArchiveAsync(string archivePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ChipLauncher_" + Guid.NewGuid().ToString("N"));
        try
        {
            await ExtractArchiveAsync(archivePath, tempDir);

            var srcPlugins = Path.Combine(tempDir, "BepInEx", "plugins");
            if (!Directory.Exists(srcPlugins))
                return (false, "压缩包内未找到 BepInEx/plugins 目录");

            var installed = 0;

            // 处理 BepInEx/plugins/ 下的子目录（每个子目录是一个模组）
            foreach (var dir in Directory.GetDirectories(srcPlugins))
            {
                var folderName = Path.GetFileName(dir);
                if (folderName == null) continue;

                var targetDir = Path.Combine(_pluginsDir, folderName);
                Directory.CreateDirectory(targetDir);
                CopyDirectoryContent(dir, targetDir);
                installed++;
            }

            // 处理 BepInEx/plugins/ 根目录下的 .dll（无子目录的独立插件）
            foreach (var dll in Directory.GetFiles(srcPlugins, "*.dll"))
            {
                var folderName = await Task.Run(() => GetBepInPluginName(dll));
                if (string.IsNullOrEmpty(folderName)) continue;

                var targetDir = Path.Combine(_pluginsDir, folderName);
                Directory.CreateDirectory(targetDir);

                var dllDir = Path.GetDirectoryName(dll)!;
                CopyDirectoryContent(dllDir, targetDir);
                installed++;
            }

            return installed > 0
                ? (true, $"✅ 已安装 {installed} 个模组")
                : (false, "压缩包内未找到可安装的模组");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>普通压缩包 → 扫描所有 DLL，提取其所在目录到同名文件夹</summary>
    private async Task<(bool Success, string Message)> InstallRegularArchiveAsync(string archivePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ChipLauncher_" + Guid.NewGuid().ToString("N"));
        try
        {
            await ExtractArchiveAsync(archivePath, tempDir);

            // ── 情况 A：根目录有 DLL → 整个压缩包视为一个模组 ──
            var rootDlls = Directory.GetFiles(tempDir, "*.dll", SearchOption.TopDirectoryOnly);
            if (rootDlls.Length > 0)
            {
                var rootDll = rootDlls[0];
                var folderName = await Task.Run(() => GetBepInPluginName(rootDll));
                if (string.IsNullOrEmpty(folderName))
                    return (false, "无法识别模组：压缩包根目录 DLL 未找到 [BepInPlugin] 特性");

                var targetDir = Path.Combine(_pluginsDir, folderName);

                Directory.CreateDirectory(targetDir);
                CopyDirectoryContent(tempDir, targetDir);

                return (true, $"✅ 模组「{folderName}」安装成功");
            }

            // ── 情况 B：根目录无 DLL → 扫描各子目录分别处理 ──
            var dllFiles = Directory.GetFiles(tempDir, "*.dll", SearchOption.AllDirectories);
            if (dllFiles.Length == 0)
                return (false, "压缩包中未找到 .dll 文件");

            var installed = 0;
            var processedDirs = new HashSet<string>();

            foreach (var dll in dllFiles)
            {
                var srcDir = Path.GetDirectoryName(dll)!;
                if (!processedDirs.Add(srcDir)) continue;

                var folderName = await Task.Run(() => GetBepInPluginName(dll));
                if (string.IsNullOrEmpty(folderName)) continue; // 不是有效模组，跳过

                var targetDir = Path.Combine(_pluginsDir, folderName);
                Directory.CreateDirectory(targetDir);

                // 复制 DLL 所在目录的全部文件
                foreach (var file in Directory.GetFiles(srcDir))
                    CopyFileIfNotExists(file, Path.Combine(targetDir, Path.GetFileName(file)));

                // 复制子目录
                foreach (var subDir in Directory.GetDirectories(srcDir))
                    CopyDirectoryIfNotExists(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));

                installed++;
            }

            return (true, $"✅ 已安装 {installed} 个模组");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ── 辅助方法 ──────────────────────────────────────────────

    /// <summary>用 SharpCompress 解压到目录</summary>
    private static Task ExtractArchiveAsync(string archivePath, string destDir)
    {
        return Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(destDir);

                using var stream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions());

                Logger.Info($"解压压缩包: {archivePath} → {destDir}，条目数: {archive.Entries.Count()}");

                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    entry.WriteToDirectory(destDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"解压失败: {archivePath}", ex);
                throw;
            }
        });
    }

    /// <summary>从 DLL 直接读取 [BepInPlugin(Guid, Name, Version)] 的三个参数（无需解析依赖）</summary>
    public static (string? Guid, string? Name, string? Version) GetBepInPluginInfo(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                foreach (var attrHandle in typeDef.GetCustomAttributes())
                {
                    var attr = metadataReader.GetCustomAttribute(attrHandle);

                    // 获取特性类型名称
                    var attrTypeName = GetAttributeTypeName(metadataReader, attr.Constructor);
                    if (attrTypeName == null) continue;
                    if (attrTypeName != "BepInPlugin" && attrTypeName != "BepInPluginAttribute"
                                                      && !attrTypeName.EndsWith(".BepInPlugin") &&
                                                      !attrTypeName.EndsWith(".BepInPluginAttribute"))
                        continue;

                    // 解析 BepInPlugin(Guid, Name, Version) 的三个字符串参数
                    return ParseBepInPluginAttribute(metadataReader, attr);
                }
            }

            return (null, null, null);
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取模组特性失败: {dllPath} - {ex.GetType().Name}: {ex.Message}");
            return (null, null, null);
        }
    }

    /// <summary>获取特性构造函数的类型全名</summary>
    private static string? GetAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        try
        {
            if (constructor.Kind == HandleKind.MemberReference)
            {
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)constructor);
                if (memberRef.Parent.Kind == HandleKind.TypeReference)
                {
                    var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                    var nameSpace = reader.GetString(typeRef.Namespace);
                    var name = reader.GetString(typeRef.Name);
                    return string.IsNullOrEmpty(nameSpace) ? name : $"{nameSpace}.{name}";
                }

                if (memberRef.Parent.Kind == HandleKind.TypeDefinition)
                {
                    var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)memberRef.Parent);
                    var name = reader.GetString(typeDef.Name);
                    return name;
                }
            }
            else if (constructor.Kind == HandleKind.MethodDefinition)
            {
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)constructor);
                var typeDef = reader.GetTypeDefinition(methodDef.GetDeclaringType());
                var name = reader.GetString(typeDef.Name);
                return name;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>解析 BepInPlugin(Guid, Name, Version) 特性的三个字符串参数</summary>
    private static (string? Guid, string? Name, string? Version) ParseBepInPluginAttribute(
        MetadataReader reader, CustomAttribute attr)
    {
        var valueReader = reader.GetBlobReader(attr.Value);

        // 跳过固定 prolog（2 字节，应为 0x0001）
        if (valueReader.Length < 2) return (null, null, null);
        valueReader.ReadUInt16();

        // 读取三个字符串参数
        var guid = ReadSerializedString(ref valueReader);
        var name = ReadSerializedString(ref valueReader);
        var version = ReadSerializedString(ref valueReader);

        return (guid, name, version);
    }

    /// <summary>从自定义特性值 Blob 中读取序列化字符串（ECMA-335 SerString / PackedLen）</summary>
    private static string? ReadSerializedString(ref BlobReader reader)
    {
        if (reader.RemainingBytes == 0) return null;

        var b = reader.ReadByte();
        if (b == 0xFF) return null; // null

        // 压缩无符号整数（ECMA-335 II.23.2）
        int length;
        if ((b & 0x80) == 0)
        {
            // 0x00-0x7F：单字节编码长度
            length = b;
        }
        else if ((b & 0xC0) == 0x80)
        {
            // 0x80-0xBF：双字节编码长度
            var b2 = reader.ReadByte();
            length = ((b & 0x3F) << 8) | b2;
        }
        else
        {
            // 0xC0-0xDF：四字节编码长度
            var b2 = reader.ReadByte();
            var b3 = reader.ReadByte();
            var b4 = reader.ReadByte();
            length = ((b & 0x1F) << 24) | (b2 << 16) | (b3 << 8) | b4;
        }

        if (length == 0) return string.Empty;
        var chars = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(chars);
    }

    /// <summary>获取程序集解析搜索目录列表（含 BepInEx/core）</summary>
    private static List<string> GetAssemblySearchDirs(string dllDir)
    {
        var dirs = new List<string> { dllDir };
        dirs.Add(RuntimeEnvironment.GetRuntimeDirectory());

        // 1. 通过 GameLocalization.GetGameDirectory() 定位 BepInEx/core/
        var gameDir = GameLocalization.GetGameDirectory();
        if (!string.IsNullOrEmpty(gameDir))
        {
            var bepInExCore = Path.Combine(gameDir, "BepInEx", "core");
            if (Directory.Exists(bepInExCore) && !dirs.Contains(bepInExCore))
                dirs.Add(bepInExCore);
        }

        // 2. 在当前程序所在目录下找 BepInEx/core/
        var appDir = Path.GetDirectoryName(AppContext.BaseDirectory);
        if (appDir != null)
        {
            var bepInExCore = Path.Combine(appDir, "BepInEx", "core");
            if (Directory.Exists(bepInExCore) && !dirs.Contains(bepInExCore))
                dirs.Add(bepInExCore);
        }

        return dirs;
    }

    /// <summary>读取 .NET 程序集的 [BepInPlugin] 特性获取模组名（第二个参数）</summary>
    private static string? GetBepInPluginName(string dllPath)
    {
        return GetBepInPluginInfo(dllPath).Name;
    }

    /// <summary>复制目录下所有文件（不递归子目录）</summary>
    private static void CopyDirectoryContent(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
            CopyFileIfNotExists(file, Path.Combine(targetDir, Path.GetFileName(file)));

        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectoryIfNotExists(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));
    }

    private static void CopyFileIfNotExists(string source, string dest)
    {
        if (!File.Exists(dest))
            File.Copy(source, dest);
    }

    private static void CopyDirectoryIfNotExists(string sourceDir, string targetDir)
    {
        if (Directory.Exists(targetDir)) return;
        Directory.CreateDirectory(targetDir);
        CopyDirectoryContent(sourceDir, targetDir);
    }
}