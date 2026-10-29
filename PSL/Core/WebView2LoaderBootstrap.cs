using System.IO;
using System.Runtime.InteropServices;

namespace PSL.Core;

/// <summary>
/// WebView2 原生加载器（<c>WebView2Loader.dll</c>）的自举。
/// <para>
/// net48 的 WebView2 包默认把 <c>runtimes\win-x86\native\WebView2Loader.dll</c> 复制到输出目录，
/// 发布时就得跟着一起带。为了做成单文件，这里把它作为嵌入资源塞进 exe，
/// 启动时释放到数据目录（<c>PSL\WebView2Loader.dll</c>），再用 <c>SetDllDirectory</c>
/// 把它加进 DLL 搜索路径，WebView2 初始化时就能找到。
/// </para>
/// </summary>
public static class WebView2LoaderBootstrap
{
    private const string ResourceName = "PSL.Assets.WebView2Loader.dll";
    private const string FileName = "WebView2Loader.dll";

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    /// <summary>确保加载器已释放到磁盘并加入 DLL 搜索路径。</summary>
    public static void Ensure()
    {
        try
        {
            string dir = Paths.DataDir;
            string target = Path.Combine(dir, FileName);

            using Stream? resource = typeof(WebView2LoaderBootstrap).Assembly
                .GetManifestResourceStream(ResourceName);

            if (resource is null)
            {
                Log.Warn("未找到内嵌的 WebView2Loader.dll（友站页的内置浏览器可能无法使用）");
                return;
            }

            Directory.CreateDirectory(dir);

            bool needWrite = !File.Exists(target) || new FileInfo(target).Length != resource.Length;
            if (needWrite)
            {
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
                resource.CopyTo(output);
                Log.Info($"已释放 WebView2Loader.dll → {target}");
            }

            if (!SetDllDirectory(dir))
                Log.Warn("SetDllDirectory 失败，WebView2 可能找不到原生加载器");
        }
        catch (Exception ex)
        {
            Log.Error("释放 WebView2Loader.dll 失败", ex);
        }
    }
}
