using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using SearchOption = System.IO.SearchOption;

namespace PSL.Core;

/// <summary>已安装的一个资源（模组 / 涂装包 / 零件包 / 星系包 / 蓝图）。</summary>
public sealed class ResourceItem
{
    public string Name { get; set; } = "";

    /// <summary>文件或文件夹的完整路径。</summary>
    public string Path { get; set; } = "";

    public bool IsDirectory { get; set; }

    /// <summary>是否启用（禁用 = 改名加 .disabled 后缀，游戏不再加载）。</summary>
    public bool Enabled { get; set; } = true;

    public long Size { get; set; }

    public DateTime Modified { get; set; }

    public SfsResourceKind Kind { get; set; }

    /// <summary>附加说明（如「含 3 个 dll」）。</summary>
    public string Extra { get; set; } = "";

    public string SizeText => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024d:F1} KB",
        < 1024L * 1024 * 1024 => $"{Size / 1024d / 1024d:F1} MB",
        _ => $"{Size / 1024d / 1024d / 1024d:F2} GB"
    };

    public string StatusText => Enabled ? "已启用" : "已禁用";
}

/// <summary>安装结果。</summary>
public sealed class InstallResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string TargetPath { get; init; } = "";
    public int FileCount { get; init; }

    public static InstallResult Fail(string message) => new() { Success = false, Message = message };

    public static InstallResult Ok(string message, string target, int count = 1)
        => new() { Success = true, Message = message, TargetPath = target, FileCount = count };
}

/// <summary>五类资源的本地扫描 / 安装 / 卸载 / 启停。</summary>
public static class ResourceManager
{
    private const string DisabledSuffix = ".disabled";
    private const long MaxExtractBytes = 4L * 1024 * 1024 * 1024; // 解压保护上限

    private static Encoding? _gbk;

    private static Encoding Gbk => _gbk ??= CreateGbk();

    private static Encoding CreateGbk()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    // ================= 扫描 =================

    public static List<ResourceItem> List(GameVersion version, SfsResourceKind kind) => kind switch
    {
        SfsResourceKind.Mod => ListModules(version),
        SfsResourceKind.TexturePack => ListTexturePacks(version),
        SfsResourceKind.PartsPack => ListPartsPacks(version),
        SfsResourceKind.SolarSystem => ListSolarSystems(version),
        SfsResourceKind.Blueprint => ListBlueprints(version),
        _ => new List<ResourceItem>()
    };

    /// <summary>DLL 模组：Mods 下的子文件夹（含 dll），以及直接摆在 Mods 根下的 dll。</summary>
    public static List<ResourceItem> ListModules(GameVersion version)
    {
        var list = new List<ResourceItem>();
        string root = version.ModsDir;
        if (!Directory.Exists(root)) return list;

        // 子文件夹形式的模组
        foreach (string dir in SafeDirs(root))
        {
            string name = Path.GetFileName(dir);
            if (name.Equals("Custom_Assets", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("Custom Assets", StringComparison.OrdinalIgnoreCase)) continue;

            int dllCount = CountFiles(dir, "*.dll");
            if (dllCount == 0) continue;

            var info = new DirectoryInfo(dir);
            list.Add(new ResourceItem
            {
                Name = name,
                Path = dir,
                IsDirectory = true,
                Enabled = true,
                Size = DirSize(dir),
                Modified = info.LastWriteTime,
                Kind = SfsResourceKind.Mod,
                Extra = dllCount > 1 ? $"含 {dllCount} 个 dll" : "dll 模组"
            });
        }

        // 直接堆在 Mods 根目录的 dll（游戏首次启动会自动收进子文件夹）
        foreach (string file in SafeFiles(root, "*.dll").Concat(SafeFiles(root, "*" + DisabledSuffix)))
        {
            if (!file.EndsWith(".dll" + DisabledSuffix, StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

            bool enabled = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            var info = new FileInfo(file);
            list.Add(new ResourceItem
            {
                Name = Path.GetFileName(file) is var n && n.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
                    ? n[..^DisabledSuffix.Length]
                    : n,
                Path = file,
                IsDirectory = false,
                Enabled = enabled,
                Size = info.Length,
                Modified = info.LastWriteTime,
                Kind = SfsResourceKind.Mod,
                Extra = "未归档（游戏启动后会自动归类）"
            });
        }

        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>涂装包：Custom_Assets\Texture Packs 下的文件夹或压缩文件。</summary>
    public static List<ResourceItem> ListTexturePacks(GameVersion version)
        => ListSimple(version.TexturePacksDir, SfsResourceKind.TexturePack, "涂装包");

    /// <summary>零件包：Custom_Assets\Parts 下的 .pack 文件。</summary>
    public static List<ResourceItem> ListPartsPacks(GameVersion version)
    {
        var list = new List<ResourceItem>();
        string root = version.PartsDir;
        if (!Directory.Exists(root)) return list;

        foreach (string file in Enumerate(root))
        {
            string name = Path.GetFileName(file);
            if (!name.EndsWith(".pack", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".pack" + DisabledSuffix, StringComparison.OrdinalIgnoreCase)) continue;

            bool enabled = name.EndsWith(".pack", StringComparison.OrdinalIgnoreCase);
            var info = new FileInfo(file);
            list.Add(new ResourceItem
            {
                Name = enabled ? name : name[..^DisabledSuffix.Length],
                Path = file,
                IsDirectory = Directory.Exists(file),
                Enabled = enabled,
                Size = Directory.Exists(file) ? DirSize(file) : info.Length,
                Modified = info.LastWriteTime,
                Kind = SfsResourceKind.PartsPack,
                Extra = "零件包 (.pack)"
            });
        }

        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>星系包：Spaceflight Simulator_Data\Custom Solar Systems 下的文件夹。</summary>
    public static List<ResourceItem> ListSolarSystems(GameVersion version)
        => ListSimple(version.SolarSystemsDir, SfsResourceKind.SolarSystem, "星系包");

    /// <summary>蓝图：Saving\Blueprints 下的子文件夹（含 Blueprint.txt）。</summary>
    public static List<ResourceItem> ListBlueprints(GameVersion version)
    {
        var list = new List<ResourceItem>();
        string root = version.BlueprintsDir;
        if (!Directory.Exists(root)) return list;

        foreach (string dir in SafeDirs(root))
        {
            string name = Path.GetFileName(dir);
            bool enabled = !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            string display = enabled ? name : name[..^DisabledSuffix.Length];

            string bpFile = Path.Combine(dir, "Blueprint.txt");
            var info = new DirectoryInfo(dir);
            list.Add(new ResourceItem
            {
                Name = display,
                Path = dir,
                IsDirectory = true,
                Enabled = enabled,
                Size = DirSize(dir),
                Modified = info.LastWriteTime,
                Kind = SfsResourceKind.Blueprint,
                Extra = File.Exists(bpFile) ? "含 Blueprint.txt" : "缺少 Blueprint.txt"
            });
        }

        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<ResourceItem> ListSimple(string root, SfsResourceKind kind, string tag)
    {
        var list = new List<ResourceItem>();
        if (!Directory.Exists(root)) return list;

        foreach (string dir in SafeDirs(root))
        {
            string name = Path.GetFileName(dir);
            bool enabled = !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            var info = new DirectoryInfo(dir);
            list.Add(new ResourceItem
            {
                Name = enabled ? name : name[..^DisabledSuffix.Length],
                Path = dir,
                IsDirectory = true,
                Enabled = enabled,
                Size = DirSize(dir),
                Modified = info.LastWriteTime,
                Kind = kind,
                Extra = tag
            });
        }

        foreach (string file in SafeFiles(root, "*"))
        {
            if (file.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase) == false &&
                Directory.Exists(file)) continue;

            string name = Path.GetFileName(file);
            bool enabled = !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            if (Path.GetExtension(enabled ? name : name[..^DisabledSuffix.Length]).Length == 0) continue;

            var info = new FileInfo(file);
            list.Add(new ResourceItem
            {
                Name = enabled ? name : name[..^DisabledSuffix.Length],
                Path = file,
                IsDirectory = false,
                Enabled = enabled,
                Size = info.Length,
                Modified = info.LastWriteTime,
                Kind = kind,
                Extra = tag
            });
        }

        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ================= 安装 =================

    /// <summary>安装一个压缩包（zip）。</summary>
    public static InstallResult InstallArchive(string archivePath, SfsResourceKind kind, GameVersion version, string? nameOverride = null)
    {
        if (!File.Exists(archivePath)) return InstallResult.Fail("安装包不存在。");

        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (ext != ".zip")
            return InstallResult.Fail($"暂不支持 {ext} 格式的安装包（目前支持 .zip）。");

        string name = string.IsNullOrWhiteSpace(nameOverride)
            ? Path.GetFileNameWithoutExtension(archivePath)
            : nameOverride!.Trim();

        string tempRoot = Path.Combine(Paths.CacheDir, "Extract", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            ExtractZip(archivePath, tempRoot);

            // 压缩包内如果只有一层文件夹，就把它当作内容根
            string contentRoot = ResolveContentRoot(tempRoot);

            return kind switch
            {
                SfsResourceKind.Mod => InstallModPayload(contentRoot, name, version),
                SfsResourceKind.TexturePack => InstallFolderPayload(contentRoot, version.TexturePacksDir, name, SfsResourceKind.TexturePack, "涂装包"),
                SfsResourceKind.PartsPack => InstallPartsPayload(contentRoot, name, version),
                SfsResourceKind.SolarSystem => InstallFolderPayload(contentRoot, version.SolarSystemsDir, name, SfsResourceKind.SolarSystem, "星系包"),
                SfsResourceKind.Blueprint => InstallBlueprintPayload(contentRoot, name, version),
                _ => InstallResult.Fail("未知资源类型。")
            };
        }
        catch (Exception ex)
        {
            Log.Error("安装失败", ex);
            return InstallResult.Fail($"安装失败：{ex.Message}");
        }
        finally
        {
            SafeDeleteDir(tempRoot);
        }
    }

    /// <summary>一键安装单个 dll（拖入 dll 用）。</summary>
    public static InstallResult InstallDll(string dllPath, GameVersion version)
    {
        try
        {
            if (!File.Exists(dllPath)) return InstallResult.Fail("文件不存在。");
            if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return InstallResult.Fail("只支持 .dll 文件。");

            string name = Path.GetFileNameWithoutExtension(dllPath);
            string targetDir = Path.Combine(version.ModsDir, name);
            Directory.CreateDirectory(targetDir);

            string target = Path.Combine(targetDir, Path.GetFileName(dllPath));
            File.Copy(dllPath, target, true);

            Log.Info($"已安装 DLL 模组：{target}");
            return InstallResult.Ok($"模组「{name}」已安装到 Mods\\{name}\\", targetDir);
        }
        catch (Exception ex)
        {
            Log.Error("安装 dll 失败", ex);
            return InstallResult.Fail($"安装失败：{ex.Message}");
        }
    }

    /// <summary>一键安装单个 .pack 零件包（拖入 .pack 用）。</summary>
    public static InstallResult InstallPack(string packPath, GameVersion version)
    {
        try
        {
            if (!File.Exists(packPath)) return InstallResult.Fail("文件不存在。");
            if (!packPath.EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
                return InstallResult.Fail("只支持 .pack 文件。");

            Directory.CreateDirectory(version.PartsDir);
            string target = Path.Combine(version.PartsDir, Path.GetFileName(packPath));
            File.Copy(packPath, target, true);

            Log.Info($"已安装零件包：{target}");
            return InstallResult.Ok($"零件包「{Path.GetFileNameWithoutExtension(packPath)}」已安装", target);
        }
        catch (Exception ex)
        {
            Log.Error("安装 pack 失败", ex);
            return InstallResult.Fail($"安装失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 只读取压缩包内的条目名来判断它属于哪一类资源（**不解压**，所以很快）。
    /// 判定顺序自上而下，先命中先返回：
    /// <list type="number">
    /// <item>含 <c>.dll</c> → 模组</item>
    /// <item>含 <c>.pack</c> → 零件包</item>
    /// <item>含 <c>Blueprint.txt</c> → 蓝图</item>
    /// <item>路径含 <c>Custom Solar Systems/</c> 或含 <c>SolarSystem.txt</c> → 星系包</item>
    /// <item>路径含 <c>Texture Packs/</c> 或含 <c>TexturePack.txt</c> → 涂装包</item>
    /// </list>
    /// 全都判不出来时返回 <c>null</c>，由调用方让用户手动指定，避免装错目录。
    /// </summary>
    public static SfsResourceKind? DetectArchiveKind(string archivePath)
    {
        if (!File.Exists(archivePath)) return null;

        List<string> names;
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(archivePath);
            names = zip.Entries.Select(en => en.FullName.Replace('\\', '/')).ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"读取压缩包失败：{archivePath} —— {ex.Message}");
            return null;
        }

        if (names.Count == 0) return null;

        bool EndsWith(string suffix)
            => names.Any(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        bool Contains(string part)
            => names.Any(n => n.Contains(part, StringComparison.OrdinalIgnoreCase));

        if (EndsWith(".dll")) return SfsResourceKind.Mod;
        if (EndsWith(".pack")) return SfsResourceKind.PartsPack;
        if (EndsWith("Blueprint.txt")) return SfsResourceKind.Blueprint;
        if (Contains("Custom Solar Systems/") || EndsWith("SolarSystem.txt")) return SfsResourceKind.SolarSystem;
        if (Contains("Texture Packs/") || EndsWith("TexturePack.txt")) return SfsResourceKind.TexturePack;
        return null;
    }

    /// <summary>资源类型的中文名。</summary>
    public static string KindName(SfsResourceKind kind) => kind switch
    {
        SfsResourceKind.Mod => "DLL 模组",
        SfsResourceKind.TexturePack => "涂装包",
        SfsResourceKind.PartsPack => "零件包",
        SfsResourceKind.SolarSystem => "星系包",
        SfsResourceKind.Blueprint => "蓝图",
        _ => "资源"
    };

    private static InstallResult InstallModPayload(string contentRoot, string name, GameVersion version)
    {
        List<string> dlls = EnumerateFilesRecursive(contentRoot, "*.dll").ToList();
        if (dlls.Count == 0)
            return InstallResult.Fail("压缩包内没有找到 .dll 文件，这不是一个 DLL 模组。");

        string targetDir = Path.Combine(version.ModsDir, name);
        Directory.CreateDirectory(targetDir);

        // 连同 dll 同级的配置文件一起复制
        foreach (string dll in dlls)
        {
            string dllDir = Path.GetDirectoryName(dll)!;
            foreach (string file in Directory.EnumerateFiles(dllDir))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
        }

        Log.Info($"已安装 DLL 模组：{targetDir}（{dlls.Count} 个 dll）");
        return InstallResult.Ok($"模组「{name}」已安装（{dlls.Count} 个 dll）", targetDir, dlls.Count);
    }

    private static InstallResult InstallPartsPayload(string contentRoot, string name, GameVersion version)
    {
        List<string> packs = EnumerateFilesRecursive(contentRoot, "*.pack").ToList();
        if (packs.Count > 0)
        {
            Directory.CreateDirectory(version.PartsDir);
            foreach (string pack in packs)
                File.Copy(pack, Path.Combine(version.PartsDir, Path.GetFileName(pack)), true);

            Log.Info($"已安装零件包：{name}（{packs.Count} 个 .pack）");
            return InstallResult.Ok($"零件包「{name}」已安装（{packs.Count} 个 .pack）", version.PartsDir, packs.Count);
        }

        // 没有 .pack 就退化为整个文件夹放进 Parts
        return InstallFolderPayload(contentRoot, version.PartsDir, name, SfsResourceKind.PartsPack, "零件包");
    }

    private static InstallResult InstallFolderPayload(
        string contentRoot, string targetRoot, string name, SfsResourceKind kind, string tag)
    {
        Directory.CreateDirectory(targetRoot);
        string target = MakeUniqueDir(Path.Combine(targetRoot, SanitizeFolderName(name)));

        // 内容根本身就是我们要的文件夹时，直接复制它
        CopyDirectory(contentRoot, target);

        Log.Info($"已安装{tag}：{target}");
        return InstallResult.Ok($"{tag}「{name}」已安装", target);
    }

    private static InstallResult InstallBlueprintPayload(string contentRoot, string name, GameVersion version)
    {
        Directory.CreateDirectory(version.BlueprintsDir);
        string target = MakeUniqueDir(Path.Combine(version.BlueprintsDir, SanitizeFolderName(name)));

        // 蓝图：包里可能直接是 Blueprint.txt，也可能是一层文件夹
        List<string> bps = EnumerateFilesRecursive(contentRoot, "Blueprint.txt").ToList();
        if (bps.Count > 0)
        {
            Directory.CreateDirectory(target);
            foreach (string bp in bps)
            {
                string dir = Path.GetDirectoryName(bp)!;
                string destDir = bps.Count == 1 ? target : Path.Combine(target, Path.GetFileName(dir));
                Directory.CreateDirectory(destDir);
                foreach (string file in Directory.EnumerateFiles(dir))
                    File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            }
        }
        else if (EnumerateFilesRecursive(contentRoot, "*.bp").Any())
        {
            Directory.CreateDirectory(target);
            foreach (string file in EnumerateFilesRecursive(contentRoot, "*"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        }
        else
        {
            return InstallResult.Fail("压缩包内没有找到蓝图文件（Blueprint.txt 或 .bp）。");
        }

        Log.Info($"已安装蓝图：{target}");
        return InstallResult.Ok($"蓝图「{name}」已导入到 Saving\\Blueprints", target);
    }

    // ================= 启停 / 卸载 =================

    /// <summary>启用 / 禁用某个资源（通过改名实现）。</summary>
    public static bool SetEnabled(ResourceItem item, bool enabled)
    {
        try
        {
            if (item.Enabled == enabled) return true;

            string src = item.Path;
            if (!File.Exists(src) && !Directory.Exists(src)) return false;

            string dst = enabled
                ? (src.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
                    ? src[..^DisabledSuffix.Length]
                    : src)
                : src + DisabledSuffix;

            if (Directory.Exists(src))
            {
                Directory.Move(src, dst);
            }
            else
            {
                File.Move(src, dst, true);
            }

            item.Path = dst;
            item.Enabled = enabled;
            Log.Info($"{(enabled ? "启用" : "禁用")}资源：{dst}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("切换启用状态失败", ex);
            return false;
        }
    }

    /// <summary>卸载资源（删除到回收站，可恢复）。</summary>
    public static bool Uninstall(ResourceItem item)
    {
        try
        {
            if (Directory.Exists(item.Path))
                FileSystem.DeleteDirectory(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else if (File.Exists(item.Path))
                FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                return false;

            Log.Info($"已卸载资源到回收站：{item.Path}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("卸载失败", ex);
            return false;
        }
    }

    // ================= 工具 =================

    /// <summary>把文件或文件夹移到回收站。</summary>
    public static void DeleteToRecycleBin(string path)
    {
        try
        {
            if (Directory.Exists(path))
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else if (File.Exists(path))
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            Log.Error($"删除失败：{path}", ex);
        }
    }

    /// <summary>解压 zip 到目标目录（自动处理 GBK 文件名，含 zip-slip 防护）。</summary>
    public static void ExtractZip(string zipPath, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        string fullTarget = Path.GetFullPath(targetDir);

        using ZipArchive archive = OpenZip(zipPath);
        long total = archive.Entries.Sum(e => e.Length);
        if (total > MaxExtractBytes)
            throw new IOException("压缩包解压后体积过大，已中止。");

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // 目录项

            string destPath = Path.GetFullPath(Path.Combine(fullTarget, entry.FullName));
            if (!destPath.StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"压缩包包含非法路径：{entry.FullName}");

            string? dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            entry.ExtractToFile(destPath, true);
        }
    }

    private static ZipArchive OpenZip(string path)
    {
        // 先按 UTF-8 试；中文乱码时改用 GBK
        try
        {
            var fs = File.OpenRead(path);
            try
            {
                var archive = new ZipArchive(fs, ZipArchiveMode.Read, false, Encoding.UTF8);
                bool garbled = archive.Entries.Any(e => e.FullName.Contains('\uFFFD'));
                if (!garbled) return archive;
                archive.Dispose();
            }
            catch
            {
                // 交给下面用 GBK 重试
            }
            finally
            {
                if (fs.CanRead) fs.Dispose();
            }
        }
        catch
        {
            // 忽略，走 GBK 分支
        }

        return new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read, false, Gbk);
    }

    /// <summary>压缩包内如果只有一层文件夹，返回该文件夹路径，否则返回原路径。</summary>
    private static string ResolveContentRoot(string tempRoot)
    {
        string[] dirs = Directory.GetDirectories(tempRoot);
        string[] files = Directory.GetFiles(tempRoot);
        if (dirs.Length == 1 && files.Length == 0) return dirs[0];
        return tempRoot;
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "未命名" : name.Trim();
    }

    private static string MakeUniqueDir(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return path;
        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }

        return path + " (" + Guid.NewGuid().ToString("N")[..6] + ")";
    }

    private static IEnumerable<string> SafeDirs(string root)
    {
        try { return Directory.EnumerateDirectories(root); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string root, string pattern)
    {
        try { return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> Enumerate(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            // 忽略
        }

        return Array.Empty<string>();
    }

    private static IEnumerable<string> EnumerateFilesRecursive(string root, string pattern)
    {
        try { return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories); }
        catch { return Array.Empty<string>(); }
    }

    private static int CountFiles(string root, string pattern)
    {
        try { return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly).Count(); }
        catch { return 0; }
    }

    private static long DirSize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; }
                });
        }
        catch
        {
            return 0;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(target, rel));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(source, file);
            string dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    private static void SafeDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch
        {
            // 忽略
        }
    }
}
