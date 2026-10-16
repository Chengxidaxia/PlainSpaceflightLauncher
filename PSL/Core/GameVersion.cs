using System.IO;
using System.Text.RegularExpressions;

namespace PSL.Core;

/// <summary>
/// SFS 的一个「版本」，即 .sfs 目录下的一个直接子文件夹。
/// 判定标准：该文件夹根目录存在 SFS 启动可执行文件。
/// </summary>
public sealed class GameVersion
{
    /// <summary>SFS 官方启动文件名。</summary>
    public const string GameExeName = "Spaceflight Simulator.exe";

    /// <summary>
    /// 版本判定只认 SFS 官方启动文件。
    /// 注意：汉化整合包里 Pck\SFSChineseX.exe 之类的内部文件不参与判定，
    /// 只有「文件夹根目录」存在该 exe 才算一个版本。
    /// </summary>
    private static readonly string[] ExeCandidates =
    {
        "Spaceflight Simulator.exe",
        "SpaceflightSimulator.exe"
    };

    /// <summary>游戏数据目录名（Unity 固定）。</summary>
    public const string DataDirName = "Spaceflight Simulator_Data";

    /// <summary>DLL 模组目录名。</summary>
    public const string ModsDirName = "Mods";

    private static readonly Regex VersionRegex =
        new(@"(?<!\d)(\d+\.\d+(?:\.\d+){0,3})(?!\d)", RegexOptions.Compiled);

    public GameVersion(string folderPath)
    {
        FolderPath = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        FolderName = Path.GetFileName(FolderPath);
        ExePath = FindExe(FolderPath);
        ParsedVersion = ParseVersion(FolderName);
        DisplayName = FolderName;
    }

    /// <summary>版本文件夹完整路径。</summary>
    public string FolderPath { get; }

    /// <summary>版本文件夹名。</summary>
    public string FolderName { get; }

    /// <summary>界面上显示的名称（默认等于文件夹名，可在配置里覆盖）。</summary>
    public string DisplayName { get; set; }

    /// <summary>从文件夹名解析出的版本号（如 1.6.00.12），解析失败为 null。</summary>
    public string? ParsedVersion { get; }

    /// <summary>启动可执行文件路径（找不到为 null）。</summary>
    public string? ExePath { get; }

    /// <summary>是否是一个合法的 SFS 版本文件夹。</summary>
    public bool IsValid => ExePath is not null;

    /// <summary>是否是完整的游戏本体（含 Data 目录）。</summary>
    public bool IsComplete => ExePath is not null && Directory.Exists(DataPath);

    // ---------- 各资源目录 ----------

    public string ModsDir => Path.Combine(FolderPath, ModsDirName);

    public string DataPath => Path.Combine(FolderPath, DataDirName);

    /// <summary>
    /// 自定义资源根目录。官方为 <c>Mods\Custom_Assets</c>，
    /// 但社区部分整合包写成 <c>Mods\Custom Assets</c>（空格），这里两者都识别。
    /// </summary>
    public string CustomAssetsDir
    {
        get
        {
            string underscore = Path.Combine(ModsDir, "Custom_Assets");
            if (Directory.Exists(underscore)) return underscore;
            string space = Path.Combine(ModsDir, "Custom Assets");
            if (Directory.Exists(space)) return space;
            return underscore;
        }
    }

    /// <summary>零件包目录（.pack）。</summary>
    public string PartsDir => Path.Combine(CustomAssetsDir, "Parts");

    /// <summary>涂装包目录。</summary>
    public string TexturePacksDir => Path.Combine(CustomAssetsDir, "Texture Packs");

    /// <summary>星系包目录（Custom Solar Systems）。</summary>
    public string SolarSystemsDir => Path.Combine(DataPath, "Custom Solar Systems");

    /// <summary>蓝图目录。</summary>
    public string BlueprintsDir => Path.Combine(FolderPath, "Saving", "Blueprints");

    /// <summary>存档根目录。</summary>
    public string SavingDir => Path.Combine(FolderPath, "Saving");

    // ---------- 判定与工具 ----------

    /// <summary>判断某个文件夹是否为单个 SFS 版本（根目录有启动文件）。</summary>
    public static bool IsGameFolder(string dir)
        => Directory.Exists(dir) && FindExe(dir) is not null;

    private static string? FindExe(string dir)
    {
        foreach (string name in ExeCandidates)
        {
            string full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }

        // 兜底：不区分大小写扫一遍根目录
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(file).Equals(GameExeName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch
        {
            // 无权限等情况直接返回 null
        }

        return null;
    }

    /// <summary>从任意字符串里提取版本号。</summary>
    public static string? ParseVersion(string text)
    {
        Match m = VersionRegex.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>尝试读取游戏自带版本信息文件（部分整合包会带 SFS 版本文本）。</summary>
    public string? ReadLocalVersionHint()
    {
        foreach (string candidate in new[] { "SLV.txt", "version.txt", "Version.txt" })
        {
            try
            {
                string path = Path.Combine(FolderPath, candidate);
                if (!File.Exists(path)) continue;
                string raw = File.ReadAllText(path).Trim();
                string? parsed = ParseVersion(raw);
                if (parsed is not null) return parsed;
            }
            catch
            {
                // 忽略
            }
        }

        return null;
    }

    public override string ToString() => DisplayName;
}
