using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace PSL.Core;

/// <summary>
/// 网络图片加载器（带磁盘缓存），用于下载页资源项的图标。
/// 缓存目录 <see cref="Paths.CacheDir"/>\WebCache，文件名为 URL 的 SHA1。
/// </summary>
public static class WebImageCache
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    static WebImageCache()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 PSL/1.0");
    }

    private static string CachePath(string url)
    {
        string hash = Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        return Path.Combine(Paths.CacheDir, "WebCache", hash + Path.GetExtension(new Uri(url).AbsolutePath));
    }

    /// <summary>取缓存里的图（没有返回 null，不联网）。</summary>
    public static BitmapImage? FromCache(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string path = CachePath(url);
        if (!File.Exists(path)) return null;
        return Load(path);
    }

    /// <summary>异步下载并缓存，回调里带回 BitmapImage（已在 UI 线程外解码，Freeze 过）。</summary>
    public static async Task<BitmapImage?> LoadAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        string path = CachePath(url);
        if (File.Exists(path)) return Load(path);

        try
        {
            byte[] data = await Client.GetByteArrayAsync(url).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);
            return Load(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"下载图片失败：{url} —— {ex.Message}");
            return null;
        }
    }

    private static BitmapImage? Load(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 100; // 列表里最大 50×50，解码到 100 足够，省内存
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
