using System.IO;

namespace PSL.Core;

/// <summary>
/// 版本选择页左栏里的一个「游戏文件夹」，对应 PCL 的 <c>McFolder</c>。
/// 它的**直接子文件夹**中、根目录含 SFS 启动文件的那些，各自是一个版本。
/// <para>
/// 游戏库根目录（<see cref="GameLibrary.LibraryPath"/>）本身即一个文件夹
/// （对应 PCL 的 <c>McFolder.Types.Vanilla</c>，「添加大版本文件夹」加进来的额外父目录
/// 对应 <c>McFolder.Types.Custom</c>）。
/// </para>
/// </summary>
public sealed class GameFolder
{
    public GameFolder(string path, string name, bool isLibrary)
    {
        Path = Normalize(path);
        Name = name;
        IsLibrary = isLibrary;
    }

    /// <summary>文件夹完整路径（无尾部反斜杠）。</summary>
    public string Path { get; }

    /// <summary>列表里显示的名称。</summary>
    public string Name { get; set; }

    /// <summary>
    /// 是否是游戏库根目录（PCL 的 Vanilla 文件夹）：不允许「移出列表」，删除时也只清空不删除。
    /// </summary>
    public bool IsLibrary { get; }

    /// <summary>目录是否真的存在。</summary>
    public bool Exists => Directory.Exists(Path);

    /// <summary>该文件夹下的所有版本（只认直接子文件夹，不递归）。</summary>
    public List<GameVersion> EnumerateVersions() => VersionManager.Scan(Path);

    /// <summary>
    /// 默认显示名。照 PCL <c>PageSelectLeft.xaml.vb:153-155</c> 的取名规则：
    /// 目录名是 <c>.sfs</c> / <c>.minecraft</c> 这种隐藏式目录时，改用上一级的名字。
    /// </summary>
    public static string DefaultName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

        string[] parts = Normalize(path).Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return path;

        string last = parts[^1];
        if (last is ".sfs" or ".minecraft")
            return parts.Length >= 2 ? parts[^2] : last;

        return last;
    }

    /// <summary>把路径规范化为「完整路径 + 无尾部分隔符」。</summary>
    public static string Normalize(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
    }

    public override string ToString() => Name;
}

/// <summary>
/// 配置里持久化的「额外游戏文件夹」（对应 PCL 设置里的 <c>LaunchFolders</c>，格式 <c>名称&gt;路径</c>）。
/// </summary>
public sealed class FolderEntry
{
    /// <summary>显示名。</summary>
    public string Name { get; set; } = "";

    /// <summary>文件夹完整路径。</summary>
    public string Path { get; set; } = "";
}
