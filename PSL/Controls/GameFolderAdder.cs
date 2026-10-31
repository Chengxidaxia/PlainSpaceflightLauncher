using System.IO;
using System.Windows;
using PSL.Core;

namespace PSL.Controls;

/// <summary>
/// 「添加已有文件夹」的完整流程，主页与版本选择页共用。
/// <para>
/// 逐项照 PCL <c>Pages/PageSelectLeft.xaml.vb:140-164</c> 的 <c>Add_Click</c>：
/// 选目录（<c>Dialogs.SelectFolder</c>）→ 路径含 <c>!</c> / <c>;</c> 时拒绝（<c>:151</c>）
/// → 算默认名（<c>:153-155</c>，末段名；末段是 <c>.sfs</c> 这类隐藏目录时取上一级，超过 40 字截断）
/// → 弹输入框要显示名（<c>:156-158</c>）→ 登记进列表（<c>:160</c>）。
/// </para>
/// <para>
/// 唯一的差异在「选中的目录怎么理解」：PCL 找的是含 <c>versions\</c> 的那一层，
/// SFS 的判定是根目录有没有 <c>Spaceflight Simulator.exe</c>，
/// 所以选中的目录**本身是版本**时会直接被抽象成一个只含该版本的文件夹
/// （见 <see cref="VersionManager.ResolveGameFolder"/>）。
/// </para>
/// </summary>
public static class GameFolderAdder
{
    /// <summary>跑完整个添加流程。成功返回登记后的文件夹，用户取消或没有版本时返回 <see langword="null"/>。</summary>
    public static GameFolder? Run(Window? owner)
    {
        string? picked = FolderPicker.Pick(owner,
            "选择 SFS 游戏文件夹（里面直接放着版本，或它本身就是一个版本）");
        if (picked is null) return null;

        if (picked.Contains('!') || picked.Contains(';'))
        {
            MessageBox.Show(owner, "游戏文件夹路径中不能含有感叹号或分号。", "PlainSpaceflightLauncher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        string? folderPath = VersionManager.ResolveGameFolder(picked, out string message);
        if (folderPath is null)
        {
            MessageBox.Show(owner, message, "没有找到版本", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        string defaultName = GameFolder.DefaultName(folderPath);
        if (defaultName.Length > 40) defaultName = defaultName.Substring(0, 39);

        string? name = InputDialog.Show(owner, "输入显示名称",
            "输入该文件夹在左边栏列表中显示的名称。", defaultName);
        if (string.IsNullOrWhiteSpace(name)) return null;

        bool added = GameLibrary.AddFolder(folderPath, name.Trim());
        GameFolder? folder = GameLibrary.Folders.FirstOrDefault(f => GameLibrary.PathEquals(f.Path, folderPath));

        MainWindow.Toast(added
            ? $"已添加「{name.Trim()}」：{message}"
            : $"「{name.Trim()}」已在列表中，显示名已更新");
        return folder;
    }
}
