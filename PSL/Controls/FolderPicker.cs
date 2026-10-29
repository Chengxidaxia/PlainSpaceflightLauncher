using System.IO;
using System.Windows;

namespace PSL.Controls;

/// <summary>
/// 选文件夹对话框。
/// <para>
/// .NET 8 的 <c>OpenFolderDialog</c> 在 .NET Framework 4.8 上没有，
/// 这里用 WinForms 的 <c>FolderBrowserDialog</c> 等价替代（行为一致：单目录、可新建）。
/// </para>
/// </summary>
public static class FolderPicker
{
    /// <summary>弹窗选一个文件夹；取消返回 null。</summary>
    public static string? Pick(Window? owner, string title, string? initial = null)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = title,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            dialog.SelectedPath = initial;

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }
}
