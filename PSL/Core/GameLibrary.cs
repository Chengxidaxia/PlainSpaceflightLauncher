using System.IO;

namespace PSL.Core;

/// <summary>应用级的游戏版本库状态（全局单例）。</summary>
public static class GameLibrary
{
    private static readonly List<GameVersion> _versions = new();
    private static readonly List<GameFolder> _folders = new();

    /// <summary>当前扫描到的所有版本（所有游戏文件夹的合集）。</summary>
    public static IReadOnlyList<GameVersion> Versions => _versions;

    /// <summary>
    /// 所有游戏文件夹（第 0 项恒为游戏库根目录，其后是「添加大版本文件夹」加进来的）。
    /// 对应 PCL 的 <c>McFolderList</c>。
    /// </summary>
    public static IReadOnlyList<GameFolder> Folders => _folders;

    /// <summary>当前选中的版本（全局唯一，主页启动的就是它）。</summary>
    public static GameVersion? Current { get; private set; }

    /// <summary>版本选择页左栏当前选中的游戏文件夹。</summary>
    public static GameFolder? SelectedFolder { get; private set; }

    /// <summary>版本库发生变化（扫描 / 添加 / 删除 / 切换文件夹 / 切换版本）。</summary>
    public static event Action? Changed;

    /// <summary>游戏库根目录。</summary>
    public static string LibraryPath => PslConfig.Current.SfsDirectory;

    /// <summary>重新扫描所有游戏文件夹。</summary>
    public static void Refresh()
    {
        _folders.Clear();
        _versions.Clear();

        // 1) 游戏库根目录本身就是一个「游戏文件夹」（PCL 的 Vanilla .minecraft）；
        //    它的显示名允许被 ExtraFolders 里的同路径条目覆盖（PCL 对 Vanilla 重命名的做法）。
        string? libraryCustom = PslConfig.Current.ExtraFolders
            .FirstOrDefault(e => PathEquals(e.Path, LibraryPath))?.Name;
        string libraryName = string.IsNullOrWhiteSpace(libraryCustom)
            ? GameFolder.DefaultName(LibraryPath)
            : libraryCustom;
        _folders.Add(new GameFolder(LibraryPath, libraryName, isLibrary: true));

        // 2) 用户额外添加的游戏文件夹
        foreach (FolderEntry entry in PslConfig.Current.ExtraFolders)
        {
            if (string.IsNullOrWhiteSpace(entry.Path)) continue;
            if (_folders.Any(f => PathEquals(f.Path, entry.Path))) continue;

            string name = string.IsNullOrWhiteSpace(entry.Name)
                ? GameFolder.DefaultName(entry.Path)
                : entry.Name;
            _folders.Add(new GameFolder(entry.Path, name, isLibrary: false));
        }

        // 3) 逐个文件夹扫描版本
        _versions.AddRange(VersionManager.ScanAll(_folders.Select(f => f.Path)));

        // 4) 恢复选中状态（旧配置里 LastVersion 存的是文件夹名，故按名字再回退匹配一次）
        string? lastVersion = PslConfig.Current.LastVersion;
        Current = _versions.FirstOrDefault(v => PathEquals(v.FolderPath, lastVersion))
                  ?? _versions.FirstOrDefault(v => string.Equals(v.FolderName, lastVersion, StringComparison.OrdinalIgnoreCase))
                  ?? _versions.FirstOrDefault();

        string? lastFolder = PslConfig.Current.LastFolder;
        SelectedFolder = _folders.FirstOrDefault(f => PathEquals(f.Path, lastFolder))
                         ?? FolderOf(Current)
                         ?? _folders.FirstOrDefault();

        Log.Info($"扫描完成，共 {_folders.Count} 个游戏文件夹、{_versions.Count} 个版本，" +
                 $"当前文件夹：{SelectedFolder?.Name ?? "（无）"}，当前版本：{Current?.DisplayName ?? "（无）"}");
        Changed?.Invoke();
    }

    /// <summary>切换当前版本。</summary>
    public static void Select(GameVersion? version)
    {
        Current = version;
        PslConfig.Current.LastVersion = version?.FolderPath;
        if (version is not null && FolderOf(version) is { } folder)
            PslConfig.Current.LastFolder = folder.Path;
        PslConfig.Current.Save();
        Changed?.Invoke();
    }

    /// <summary>切换版本选择页左栏选中的游戏文件夹（对应 PCL 的 <c>McFolderSelected</c>）。</summary>
    public static void SelectFolder(GameFolder? folder)
    {
        SelectedFolder = folder;
        PslConfig.Current.LastFolder = folder?.Path;
        PslConfig.Current.Save();
        Changed?.Invoke();
    }

    /// <summary>取某个游戏文件夹下的所有版本。</summary>
    public static List<GameVersion> VersionsOf(GameFolder? folder)
        => folder is null
            ? new List<GameVersion>()
            : _versions.Where(v => IsDirectChildOf(v.FolderPath, folder.Path)).ToList();

    /// <summary>取某个版本所属的游戏文件夹。</summary>
    public static GameFolder? FolderOf(GameVersion? version)
        => version is null
            ? null
            : _folders.FirstOrDefault(f => IsDirectChildOf(version.FolderPath, f.Path));

    /// <summary>
    /// 把一个已有的文件夹加进左栏列表（对应 PCL <c>PageSelectLeft.xaml.vb:168-245</c> 的
    /// <c>AddFolder</c>）：只登记路径不动文件，已存在时视为「改名字」。
    /// </summary>
    public static bool AddFolder(string path, string? displayName = null)
    {
        string full = GameFolder.Normalize(path);
        if (string.IsNullOrWhiteSpace(full) || !Directory.Exists(full)) return false;

        string name = string.IsNullOrWhiteSpace(displayName) ? GameFolder.DefaultName(full) : displayName.Trim();

        FolderEntry? entry = PslConfig.Current.ExtraFolders
            .FirstOrDefault(e => PathEquals(e.Path, full));
        bool added;
        if (entry is null)
        {
            PslConfig.Current.ExtraFolders.Add(new FolderEntry { Name = name, Path = full });
            added = true;
        }
        else
        {
            entry.Name = name;
            added = false;
        }

        PslConfig.Current.Save();
        Refresh();
        if (added) SelectFolder(Folders.FirstOrDefault(f => PathEquals(f.Path, full)));
        return added;
    }

    /// <summary>把某个游戏文件夹从左栏列表移除（游戏库根目录不允许移除）。</summary>
    public static bool RemoveFolder(GameFolder folder)
    {
        if (folder.IsLibrary) return false;

        int removed = PslConfig.Current.ExtraFolders
            .RemoveAll(e => PathEquals(e.Path, folder.Path));
        if (removed == 0) return false;

        PslConfig.Current.Save();
        Refresh();
        return true;
    }

    /// <summary>
    /// 重命名左栏里的游戏文件夹（只改显示名，不动真实目录）。
    /// 游戏库根目录会被写进 <see cref="PslConfig.ExtraFolders"/> 以覆盖默认名。
    /// </summary>
    public static void RenameFolder(GameFolder folder, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        string name = newName.Trim();
        FolderEntry? entry = PslConfig.Current.ExtraFolders
            .FirstOrDefault(e => PathEquals(e.Path, folder.Path));
        if (entry is null)
            PslConfig.Current.ExtraFolders.Add(new FolderEntry { Name = name, Path = folder.Path });
        else
            entry.Name = name;

        PslConfig.Current.Save();
        Refresh();
    }

    /// <summary>按完整路径找版本。</summary>
    public static GameVersion? Find(string? folderPath)
        => string.IsNullOrWhiteSpace(folderPath)
            ? null
            : _versions.FirstOrDefault(v => PathEquals(v.FolderPath, folderPath));

    /// <summary>设置游戏库目录并重新扫描。</summary>
    public static void SetLibraryPath(string path)
    {
        PslConfig.Current.SfsDirectory = path;
        PslConfig.Current.LastFolder = null;
        PslConfig.Current.Save();
        Refresh();
    }

    /// <summary>路径是否相等（忽略大小写与尾部分隔符）。</summary>
    public static bool PathEquals(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
           string.Equals(GameFolder.Normalize(a), GameFolder.Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary><paramref name="childPath"/> 是否是 <paramref name="parentPath"/> 的直接子文件夹。</summary>
    private static bool IsDirectChildOf(string childPath, string parentPath)
    {
        string? parent = Path.GetDirectoryName(GameFolder.Normalize(childPath));
        return parent is not null && PathEquals(parent, parentPath);
    }
}
