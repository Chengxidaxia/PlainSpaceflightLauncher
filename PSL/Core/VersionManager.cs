using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace PSL.Core;

/// <summary>版本扫描 / 添加 / 删除 / 启动。</summary>
public static class VersionManager
{
    /// <summary>SFS 的 Steam AppID。</summary>
    public const string SteamAppId = "1718870";

    /// <summary>SFS 的 Steam 商店页。</summary>
    public const string SteamStoreUrl = "https://store.steampowered.com/app/" + SteamAppId;

    /// <summary>
    /// 扫描一个游戏文件夹下的所有合法版本（按名称排序）。
    /// <para>
    /// 默认只认**直接子文件夹**；<paramref name="includeSelf"/> 为 true 时，
    /// 若该文件夹根目录本身就含 SFS 启动文件（即它自己就是一个版本），也把它算进去。
    /// 这条规则是「添加单个版本 → 抽象成一个只含该版本的游戏文件夹」能成立的前提：
    /// SFS 没有 <c>versions\</c> 这一层，玩家常见做法是直接把某个版本放在
    /// <c>K:\SFS1.6\</c> 这种目录里，此时该目录既当文件夹也当版本。
    /// </para>
    /// </summary>
    public static List<GameVersion> Scan(string? sfsDir = null, bool includeSelf = false)
    {
        sfsDir ??= PslConfig.Current.SfsDirectory;
        var list = new List<GameVersion>();

        if (!Directory.Exists(sfsDir))
        {
            Log.Warn($"游戏目录不存在：{sfsDir}");
            return list;
        }

        // 文件夹自身就是一个版本（PCL 里不可能出现，SFS 里很常见）
        if (includeSelf && GameVersion.IsGameFolder(sfsDir))
        {
            try
            {
                list.Add(new GameVersion(GameFolder.Normalize(sfsDir))
                {
                    DisplayName = PslConfig.Current.GetVersionName(sfsDir)
                });
            }
            catch (Exception ex)
            {
                Log.Error($"读取版本失败：{sfsDir}", ex);
            }
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
            foreach (GameVersion version in Scan(folder, includeSelf: true))
            {
                if (seen.Add(GameFolder.Normalize(version.FolderPath))) list.Add(version);
            }
        }

        return list;
    }

    /// <summary>
    /// 把用户在文件夹对话框里选中的目录，整理成版本选择页左栏的一个「游戏文件夹」。
    /// <para>
    /// 对应 PCL <c>PageSelectLeft.xaml.vb:182-190</c> 的「检查实际的 Minecraft 文件夹位置」：
    /// PCL 选到不含 <c>versions\</c> 的目录时，会往子目录里找一个真正含 <c>versions\</c> 的。
    /// SFS 没有 <c>versions\</c> 这一层，判定改成「根目录含 <c>Spaceflight Simulator.exe</c>」，
    /// 于是只剩两种情况，都不需要动任何文件、只登记路径：
    /// </para>
    /// <list type="number">
    /// <item>选中的目录<b>下面</b>有版本（装着多个版本的父目录）→ 它自己就是一个游戏文件夹；</item>
    /// <item>选中的目录<b>本身</b>就是一个版本（如 <c>K://SFS1.6</c>）→ 抽象成一个只含该版本的游戏文件夹。</item>
    /// </list>
    /// 两者都不是时返回 <see langword="null"/>，由 <paramref name="message"/> 说明原因。
    /// </summary>
    public static string? ResolveGameFolder(string picked, out string message)
    {
        string dir = GameFolder.Normalize(picked);

        if (!Directory.Exists(dir))
        {
            message = $"文件夹不存在：{dir}";
            return null;
        }

        // 1) 它下面装着版本 → 直接当游戏文件夹
        List<string> subs = ScanSubVersionFolders(dir);
        if (subs.Count > 0)
        {
            message = subs.Count == 1
                ? $"「{GameFolder.DefaultName(dir)}」下有 1 个版本。"
                : $"「{GameFolder.DefaultName(dir)}」下有 {subs.Count} 个版本。";
            return dir;
        }

        // 2) 它本身就是一个版本 → 抽象成只含它一个的游戏文件夹
        if (GameVersion.IsGameFolder(dir))
        {
            message = $"「{GameFolder.DefaultName(dir)}」本身就是一个版本，已作为只含该版本的文件夹加入。";
            return dir;
        }

        message = $"在「{dir}」里没有找到任何 SFS 版本。\n\n" +
                  $"判定标准：该文件夹、或它的直接子文件夹的根目录下存在 {GameVersion.GameExeName}。\n" +
                  "只识别一层，不会递归更深层。";
        return null;
    }

    /// <summary>
    /// 扫描一个「大版本文件夹」的**直接子文件夹**，返回其中所有合法的 SFS 版本目录。
    /// 判定标准与 <see cref="Scan"/> 完全一致：子文件夹根目录存在 SFS 启动文件即为一个版本
    /// （不递归更深层，也不看 Pck 之类的内部目录）。
    /// 用于「添加已有文件夹」——一个父文件夹里同时放着好几个版本的情况。
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
}
