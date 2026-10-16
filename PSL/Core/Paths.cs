using System.IO;

namespace PSL.Core;

/// <summary>PSL 的全局路径定义。</summary>
public static class Paths
{
    /// <summary>程序（exe）所在目录。</summary>
    public static string AppDir { get; } =
        AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>PSL 自身的数据目录（配置 / 日志 / 缓存）。</summary>
    public static string DataDir => Path.Combine(AppDir, "PSL");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    public static string LogDir => Path.Combine(DataDir, "Log");

    public static string CacheDir => Path.Combine(DataDir, "Cache");

    /// <summary>下载缓存目录（未安装完成的临时文件）。</summary>
    public static string DownloadDir => Path.Combine(CacheDir, "Download");

    /// <summary>默认的游戏库目录（.sfs）。</summary>
    public static string DefaultSfsDir => Path.Combine(AppDir, ".sfs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(DownloadDir);
    }
}
