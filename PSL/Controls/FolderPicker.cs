using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace PSL.Controls;

/// <summary>
/// 选文件夹对话框（Vista+ 现代样式，和 PCL 的 <c>Dialogs.SelectFolder</c> 表现一致）。
/// <para>
/// <c>System.Windows.Forms.FolderBrowserDialog</c> 是老式树形窗口，不符合 PCL 风格；
/// 这里直接调 COM 的 <c>IFileOpenDialog</c> + <c>FOS_PICKFOLDERS</c>，
/// 得到资源管理器同款的选择文件夹对话框。
/// </para>
/// </summary>
public static class FolderPicker
{
    /// <summary>弹窗选一个文件夹；取消返回 null。</summary>
    public static string? Pick(Window? owner, string title, string? initial = null)
    {
        object? shell = null;
        try
        {
            shell = new FileOpenDialog();
            var dialog = (IFileOpenDialog)shell;

            dialog.GetOptions(out uint options);
            options |= FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
            dialog.SetOptions(options);

            if (!string.IsNullOrWhiteSpace(title)) dialog.SetTitle(title);

            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            {
                try
                {
                    SHCreateItemFromParsingName(initial, IntPtr.Zero, typeof(IShellItem).GUID,
                        out IShellItem folder);
                    dialog.SetFolder(folder);
                    Marshal.ReleaseComObject(folder);
                }
                catch (Exception ex)
                {
                    Core.Log.Warn($"设置初始目录失败：{ex.Message}");
                }
            }

            IntPtr hwnd = owner is null
                ? IntPtr.Zero
                : new System.Windows.Interop.WindowInteropHelper(owner).Handle;

            if (dialog.Show(hwnd) != 0) return null;

            dialog.GetResult(out IShellItem item);
            try
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr buffer);
                try
                {
                    return Marshal.PtrToStringUni(buffer);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(buffer);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex)
        {
            Core.Log.Error("选择文件夹失败", ex);
            return null;
        }
        finally
        {
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }

    // ---------------- COM 定义 ----------------

    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close([MarshalAs(UnmanagedType.Error)] int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItem ppv);
}
