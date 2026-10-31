using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using SearchOption = System.IO.SearchOption;

namespace PSL.Core;

/// <summary>把版本文件夹加入游戏目录时的方式。</summary>
public enum AddVersionMode
{
    /// <summary>移动到 .sfs 下（同盘秒完成）。</summary>
    Move,

    /// <summary>复制到 .sfs 下。</summary>
    Copy,

    /// <summary>在 .sfs 下创建目录联接（junction，不占额外空间）。</summary>
    Link
}

/// <summary>版本扫描 / 添加 / 删除 / 启动。</summary>
public static class VersionManager
{
    /// <summary>SFS 的 Steam AppID。</summary>
    public const string SteamAppId = "1718870";

    /// <summary>SFS 的 Steam 商店页。</summary>
    public const string SteamStoreUrl = "https://store.steampowered.com/app/" + SteamAppId;

    /// <summary>扫描 .sfs 下的直接子文件夹，返回所有合法版本（按名称排序）。</summary>
    public static List<GameVersion> Scan(string? sfsDir = null)
    {
        sfsDir ??= PslConfig.Current.SfsDirectory;
        var list = new List<GameVersion>();

        if (!Directory.Exists(sfsDir))
        {
            Log.Warn($"游戏目录目录不存在：{sfsDir}");
            return list;
        }

        IEnumerable<string> subDirs;
        try
        {
            subDirs = Directory.EnumerateDirectories(sfsDir);
        }
        catch (Exception ex)
        {
            Log.Error($"枚举游戏目录目录失败：{sfsDir}", ex);
            return list;
        }

        foreach (string dir in subDirs)
        {
            try
            {
                // 直接以「根目录是否存在 SFS 启动文件」作为唯一判定标准
                if (!GameVersion.IsGameFolder(dir)) continue;

                var version = new GameVersion(dir)
                {
                    DisplayName = PslConfig.Current.GetVersionName(dir)
                };
                list.Add(version);
            }
            catch (Exception ex)
            {
                Log.Error($"读取版本失败：{dir}", ex);
            }
        }

        return list
            .OrderBy(v => v.ParsedVersion is null ? 1 : 0)
            .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 扫描若干个游戏文件夹，把它们的版本合并成一张列表（按文件夹顺序、组内按名称排序）。
    /// 对应 PCL 中「文件夹列表 → 每个文件夹一份 versions\ 目录」的模型。
    /// </summary>
    public static List<GameVersion> ScanAll(IEnumerable<string> folders)
    {
        var list = new List<GameVersion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in folders)
        {
            foreach (GameVersion version in Scan(folder))
            {
                if (seen.Add(GameFolder.Normalize(version.FolderPath))) list.Add(version);
            }
        }

        return list;
    }

    /// <summary>校验一个文件夹是否可作为版本加入游戏目录，返回失败原因。</summary>
    public static bool Validate(string folder, out string error)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            error = "文件夹不存在。";
            return false;
        }

        if (!GameVersion.IsGameFolder(folder))
        {
            error = $"该文件夹根目录没有找到 {GameVersion.GameExeName}，不是一个 SFS 版本文件夹。";
            return false;
        }

        if (!Directory.Exists(Path.Combine(folder, GameVersion.DataDirName)))
        {
            error = $"警告：未找到 {GameVersion.DataDirName}，可能不是完整的游戏本体。";
            return false;
        }

        error = "";
        return true;
    }

    /// <summary>把一个版本文件夹加入游戏目录。</summary>
    public static GameVersion Add(string sourceFolder, AddVersionMode mode, string? sfsDir = null)
    {
        sfsDir ??= PslConfig.Current.SfsDirectory;
        sourceFolder = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar);
        Directory.CreateDirectory(sfsDir);

        string folderName = Path.GetFileName(sourceFolder);
        string target = Path.Combine(sfsDir, folderName);

        // 已经在游戏目录里了
        if (string.Equals(Path.GetFullPath(target), sourceFolder, StringComparison.OrdinalIgnoreCase))
            return new GameVersion(sourceFolder);

        target = MakeUniquePath(target);

        switch (mode)
        {
            case AddVersionMode.Move:
                try
                {
                    Directory.Move(sourceFolder, target);
                }
                catch (IOException)
                {
                    // 跨盘移动会抛异常 → 退化为复制
                    Log.Warn("跨盘移动失败，退化为复制");
                    CopyDirectory(sourceFolder, target);
                }
                break;

            case AddVersionMode.Copy:
                CopyDirectory(sourceFolder, target);
                break;

            case AddVersionMode.Link:
                CreateJunction(target, sourceFolder);
                break;
        }

        Log.Info($"已添加版本：{target}（{mode}）");
        return new GameVersion(target);
    }

    /// <summary>
    /// 扫描一个「大版本文件夹」的**直接子文件夹**，返回其中所有合法的 SFS 版本目录。
    /// 判定标准与 <see cref="Scan"/> 完全一致：子文件夹根目录存在 SFS 启动文件即为一个版本
    /// （不递归更深层，也不看 Pck 之类的内部目录）。
    /// 用于「添加大版本文件夹」——一个父文件夹里同时放着好几个版本的情况。
    /// </summary>
    public static List<string> ScanSubVersionFolders(string parentFolder)
    {
        var list = new List<string>();
        if (!Directory.Exists(parentFolder)) return list;

        try
        {
            foreach (string dir in Directory.EnumerateDirectories(parentFolder))
            {
                try
                {
                    if (GameVersion.IsGameFolder(dir)) list.Add(dir);
                }
                catch (Exception ex)
                {
                    Log.Warn($"探测子文件夹失败：{dir} —— {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"枚举大版本文件夹失败：{parentFolder}", ex);
        }

        return list.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>从游戏目录移除一个版本（删除到回收站，可恢复）。</summary>
    public static void RemoveToRecycleBin(GameVersion version)
    {
        if (!Directory.Exists(version.FolderPath)) return;

        // junction：只删链接本体，不动真实目录
        var info = new DirectoryInfo(version.FolderPath);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            info.Delete();
            Log.Info($"已移除目录联接：{version.FolderPath}");
            return;
        }

        FileSystem.DeleteDirectory(
            version.FolderPath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
        Log.Info($"已删除版本到回收站：{version.FolderPath}");
    }

    // ---------- 启动 ----------

    /// <summary>直接启动游戏本体 exe。</summary>
    public static Process? LaunchDirect(GameVersion version, bool asAdmin = false)
    {
        if (version.ExePath is null) throw new FileNotFoundException("找不到游戏启动文件。");

        var psi = new ProcessStartInfo
        {
            FileName = version.ExePath,
            WorkingDirectory = version.FolderPath,
            UseShellExecute = true
        };
        if (asAdmin) psi.Verb = "runas";
        return Process.Start(psi);
    }

    /// <summary>通过 Steam 启动游戏。</summary>
    public static Process? LaunchViaSteam()
    {
        var psi = new ProcessStartInfo
        {
            FileName = $"steam://rungameid/{SteamAppId}",
            UseShellExecute = true
        };
        return Process.Start(psi);
    }

    /// <summary>在资源管理器中打开目录。</summary>
    public static void OpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    // ---------- 内部工具 ----------

    private static string MakeUniquePath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return path;
        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }

        throw new IOException("目标已存在，且无法生成唯一名称。");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, target));

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, target), true);
    }

    private static void CreateJunction(string linkPath, string targetPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using Process? p = Process.Start(psi);
        p?.WaitForExit();
        if (p is null || p.ExitCode != 0 || !Directory.Exists(linkPath))
            throw new IOException("创建目录联接失败（可能需要管理员权限，或目标已存在）。");
    }
}
