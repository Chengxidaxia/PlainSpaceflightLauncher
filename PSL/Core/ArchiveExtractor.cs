using System.Diagnostics;
using System.IO;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace PSL.Core;

/// <summary>解压结果。</summary>
public sealed record ExtractResult(bool Success, bool UsedPassword, int Files, string Message, string? TargetDir = null);

/// <summary>
/// 压缩包解压（汉化版安装用）。
/// <para>
/// 汉化版 Release 挂的是 **WinRAR 自解压 exe**（RAR5、带安装密码）：
/// 先按魔数定位内嵌归档（SFX 存根在前、归档在后）。
/// <br/>· RAR 自解压：直接调用它自带的解压器
///    <c>SFX.exe -s -y -p&lt;密码&gt; -d&lt;目录&gt;</c>（静默、指定目标目录），实测最可靠；
/// <br/>· 7z / zip：用 SharpCompress 读，先不带密码试，加密异常再用安装密码重试。
/// </para>
/// 安装密码是常量，界面上不展示。
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>汉化版安装密码（按用户要求不在界面展示）。</summary>
    private const string InstallPassword = "3977216899";

    private static readonly byte[] SevenZipMagic = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };
    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] Rar5Magic = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };
    private static readonly byte[] Rar4Magic = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };

    /// <summary>识别到的归档类型。</summary>
    private enum Kind { Unknown, SevenZip, Zip, Rar, RarSfx }

    /// <summary>解压 <paramref name="archivePath"/> 到 <paramref name="targetDir"/>。</summary>
    public static ExtractResult Extract(
        string archivePath,
        string targetDir,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(archivePath))
            return new ExtractResult(false, false, 0, "压缩包不存在");

        Directory.CreateDirectory(targetDir);

        long offset = FindArchiveOffset(archivePath, out Kind kind);
        if (offset < 0)
            return new ExtractResult(false, false, 0, "无法识别压缩包格式（既不是 RAR、7z，也不是 zip）");

        progress?.Report("正在解压安装包…");

        if (kind is Kind.Rar or Kind.RarSfx)
            return ExtractRar(archivePath, offset, targetDir, progress, ct);

        return ExtractWithSharpCompress(archivePath, offset, targetDir, progress, ct);
    }

    // ==================== RAR ====================

    /// <summary>
    /// RAR：自解压包直接调用它自带的解压器（WinRAR SFX 支持
    /// <c>-s</c> 静默、<c>-p</c> 密码、<c>-d</c> 目标目录，且不弹窗）。
    /// </summary>
    private static ExtractResult ExtractRar(
        string archivePath,
        long offset,
        string targetDir,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (offset == 0)
        {
            // 纯 .rar：交给 SharpCompress
            return ExtractWithSharpCompress(archivePath, 0, targetDir, progress, ct);
        }

        progress?.Report("正在使用安装包自带的解压器解压…");

        var info = new ProcessStartInfo
        {
            FileName = archivePath,
            Arguments = $"-s -y -p{InstallPassword} -d\"{targetDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(archivePath) ?? Paths.DownloadDir
        };

        try
        {
            using Process process = Process.Start(info) ??
                                    throw new InvalidOperationException("无法启动安装包的解压过程");

            using var reg = ct.Register(() =>
            {
                try { process.Kill(); } catch { /* 忽略 */ }
            });

            if (!process.WaitForExit(10 * 60 * 1000))
            {
                try { process.Kill(); } catch { /* 忽略 */ }
                return new ExtractResult(false, true, 0, "解压超时（超过 10 分钟）");
            }

            int count = CountFiles(targetDir);
            if (count == 0)
                return new ExtractResult(false, true, 0, $"安装包解压失败（退出码 {process.ExitCode}）");

            return new ExtractResult(true, true, count,
                $"已解压 {count} 个文件（安装包带密码，已自动填入）", targetDir);
        }
        catch (Exception ex)
        {
            Log.Error("RAR 自解压失败", ex);
            return new ExtractResult(false, true, 0, ex.Message);
        }
    }

    private static int CountFiles(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    // ==================== 7z / zip ====================

    private static ExtractResult ExtractWithSharpCompress(
        string archivePath,
        long offset,
        string targetDir,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        // 先不带密码试一次；加密包会抛异常，再用安装密码重试
        try
        {
            int n = ExtractCore(archivePath, offset, targetDir, null, progress, ct);
            return new ExtractResult(true, false, n, $"已解压 {n} 个文件", targetDir);
        }
        catch (Exception ex) when (IsPasswordError(ex))
        {
            Log.Info("压缩包已加密，改用安装密码解压");
        }
        catch (Exception ex)
        {
            Log.Error("解压失败", ex);
            return new ExtractResult(false, false, 0, ex.Message);
        }

        try
        {
            int n = ExtractCore(archivePath, offset, targetDir, InstallPassword, progress, ct);
            return new ExtractResult(true, true, n,
                $"已解压 {n} 个文件（安装包带密码，已自动填入）", targetDir);
        }
        catch (Exception ex)
        {
            Log.Error("带密码解压失败", ex);
            return new ExtractResult(false, true, 0, ex.Message);
        }
    }

    private static bool IsPasswordError(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            string name = e.GetType().Name;
            string msg = e.Message ?? "";
            if (name.Contains("Crypt", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("password", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("encrypt", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("密", StringComparison.Ordinal)) return true;
            if (msg.Contains("Data Error", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Invalid", StringComparison.OrdinalIgnoreCase) &&
                msg.Contains("header", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int ExtractCore(
        string archivePath,
        long offset,
        string targetDir,
        string? password,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1 << 16, FileOptions.SequentialScan);
        using var stream = new OffsetStream(file, offset);

        var options = new ReaderOptions
        {
            LeaveStreamOpen = true,
            Password = password
        };

        using IReader reader = ReaderFactory.Open(stream, options);
        var extraction = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true
        };

        int count = 0;
        string root = Path.GetFullPath(targetDir);

        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory) continue;

            string key = reader.Entry.Key ?? "";
            progress?.Report($"解压中：{Path.GetFileName(key)}");

            // 防目录穿越：目标路径必须落在 targetDir 之内
            string full = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"压缩包内路径越界：{key}");

            reader.WriteEntryToDirectory(root, extraction);
            count++;
        }

        return count;
    }

    // ==================== 格式探测 ====================

    /// <summary>找内嵌归档的起始偏移与类型（文件本身就是归档时偏移为 0）。</summary>
    private static long FindArchiveOffset(string path, out Kind kind)
    {
        kind = Kind.Unknown;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20,
            FileOptions.SequentialScan);

        const int Chunk = 1 << 20;
        byte[] buffer = new byte[Chunk + 16];

        (byte[] Magic, Kind Kind)[] magics =
        {
            (SevenZipMagic, Kind.SevenZip),
            (Rar5Magic, Kind.Rar),
            (Rar4Magic, Kind.Rar),
            (ZipMagic, Kind.Zip)
        };

        long consumed = 0;   // 已从文件读走的总字节数
        int carry = 0;       // buffer 头部保留的上次尾部字节数

        while (true)
        {
            int read = fs.Read(buffer, carry, Chunk);
            if (read <= 0) break;

            int total = carry + read;
            long baseAbs = consumed - carry;   // buffer[0] 对应的文件绝对偏移

            // 取本块内出现位置最靠前的魔数（自解压存根在前、归档在后）
            int best = -1;
            Kind bestKind = Kind.Unknown;
            foreach ((byte[] magic, Kind k) in magics)
            {
                int idx = IndexOf(buffer, total, magic);
                if (idx >= 0 && (best < 0 || idx < best))
                {
                    best = idx;
                    bestKind = k;
                }
            }

            if (best >= 0)
            {
                long abs = baseAbs + best;
                kind = bestKind == Kind.Rar && abs > 0 ? Kind.RarSfx : bestKind;
                return abs;
            }

            consumed += read;

            // 保留尾部 7 字节，避免跨块漏匹配（最长魔数 8 字节）
            int keep = Math.Min(7, total);
            Array.Copy(buffer, total - keep, buffer, 0, keep);
            carry = keep;
        }

        return -1;
    }

    private static int IndexOf(byte[] haystack, int length, byte[] needle)
    {
        int last = length - needle.Length;
        for (int i = 0; i <= last; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    /// <summary>把底层流的读取起点挪到 <c>offset</c>，对外表现为一个从 0 开始的流（7z 需要可 Seek）。</summary>
    private sealed class OffsetStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _offset;

        public OffsetStream(Stream inner, long offset)
        {
            _inner = inner;
            _offset = offset;
            _inner.Position = offset;
        }

        public override bool CanRead => true;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length - _offset;

        public override long Position
        {
            get => _inner.Position - _offset;
            set => _inner.Position = value + _offset;
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => origin switch
        {
            SeekOrigin.Begin => _inner.Seek(offset + _offset, SeekOrigin.Begin) - _offset,
            SeekOrigin.Current => _inner.Seek(offset, SeekOrigin.Current) - _offset,
            _ => _inner.Seek(offset, SeekOrigin.End) - _offset
        };
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
