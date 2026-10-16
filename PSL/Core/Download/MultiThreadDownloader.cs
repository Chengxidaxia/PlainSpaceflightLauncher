using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PSL.Core.Download;

/// <summary>下载进度快照。</summary>
public sealed class DownloadProgress
{
    public long TotalBytes { get; init; }

    public long ReceivedBytes { get; set; }

    public double BytesPerSecond { get; set; }

    public bool Indeterminate => TotalBytes <= 0;

    public double Percent => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)ReceivedBytes / TotalBytes * 100d, 0d, 100d);

    public TimeSpan? Eta =>
        BytesPerSecond > 1 && TotalBytes > 0 && ReceivedBytes < TotalBytes
            ? TimeSpan.FromSeconds((TotalBytes - ReceivedBytes) / BytesPerSecond)
            : null;
}

/// <summary>
/// 多线程（HTTP Range 分段）下载器。
/// 服务器不支持 Range 时自动退化为单线程流式下载。
/// </summary>
public sealed class MultiThreadDownloader
{
    private static readonly HttpClient ProbeClient = CreateClient();

    /// <summary>下载线程数。</summary>
    public int ThreadCount { get; set; } = 8;

    /// <summary>小于该大小不分段。</summary>
    public long MinSegmentSize { get; set; } = 1L * 1024 * 1024;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>下载到指定路径，返回最终文件路径。</summary>
    public async Task DownloadAsync(
        string url,
        string targetPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("下载地址为空。", nameof(url));

        string? dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 先写临时文件，成功后再改名，避免半成品被误用
        string tempPath = targetPath + ".psldownload";
        SafeDelete(tempPath);

        // 1) 探测是否支持分段
        (long length, bool rangeSupported) = await ProbeAsync(url, ct).ConfigureAwait(false);

        var state = new ProgressState();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = Stopwatch.StartNew();

        // 进度上报计时器
        using var timer = progress is null
            ? null
            : new Timer(_ =>
            {
                long received = Interlocked.Read(ref state.Received);
                double seconds = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                double speed = received / seconds;
                progress.Report(new DownloadProgress
                {
                    TotalBytes = length,
                    ReceivedBytes = received,
                    BytesPerSecond = speed
                });
            }, null, 200, 250);

        try
        {
            if (rangeSupported && length > MinSegmentSize && ThreadCount > 1)
            {
                await DownloadSegmentedAsync(url, tempPath, length, state, cts.Token).ConfigureAwait(false);
            }
            else
            {
                await DownloadSingleAsync(url, tempPath, length, state, cts.Token).ConfigureAwait(false);
            }

            // 完整性校验
            long finalLength = new FileInfo(tempPath).Length;
            if (length > 0 && finalLength != length)
            {
                throw new IOException($"下载不完整：期望 {length} 字节，实际 {finalLength} 字节。");
            }

            // 收尾
            SafeDelete(targetPath);
            File.Move(tempPath, targetPath);

            progress?.Report(new DownloadProgress
            {
                TotalBytes = finalLength,
                ReceivedBytes = finalLength,
                BytesPerSecond = finalLength / Math.Max(0.001, sw.Elapsed.TotalSeconds)
            });

            Log.Info($"下载完成：{Path.GetFileName(targetPath)}（{finalLength:N0} 字节，{sw.Elapsed.TotalSeconds:F1}s，{ThreadCount} 线程）");
        }
        catch
        {
            SafeDelete(tempPath);
            throw;
        }
    }

    // ---------- 探测 ----------

    private async Task<(long Length, bool RangeSupported)> ProbeAsync(string url, CancellationToken ct)
    {
        // 有些 CDN 不支持 HEAD，用 Range:0-0 的 GET 更可靠
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            using HttpResponseMessage resp = await ProbeClient
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            string? rangeHeader = resp.Headers.AcceptRanges.FirstOrDefault();
            bool acceptsRanges = resp.StatusCode == HttpStatusCode.PartialContent
                                 || string.Equals(rangeHeader, "bytes", StringComparison.OrdinalIgnoreCase);

            long? total = resp.Content.Headers.ContentRange?.Length;
            if (total is null or <= 0 && resp.Content.Headers.ContentLength is > 0)
                total = resp.Content.Headers.ContentLength;

            return (total ?? -1, acceptsRanges);
        }
        catch (Exception ex)
        {
            Log.Warn($"探测下载源失败（按单线程处理）：{ex.Message}");
            return (-1, false);
        }
    }

    // ---------- 分段下载 ----------

    private async Task DownloadSegmentedAsync(
        string url, string tempPath, long length, ProgressState state, CancellationToken ct)
    {
        int threads = Math.Clamp(ThreadCount, 1, 64);
        long segment = Math.Max(MinSegmentSize, length / threads);
        var ranges = new List<(long Start, long End)>();

        for (long start = 0; start < length; start += segment)
        {
            long end = Math.Min(start + segment - 1, length - 1);
            ranges.Add((start, end));
        }

        // 预分配文件
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            fs.SetLength(length);
        }

        using var handle = File.OpenHandle(
            tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, FileOptions.Asynchronous);

        Log.Info($"开始分段下载：{ranges.Count} 段，共 {length:N0} 字节 -> {url}");

        var tasks = ranges.Select(r => DownloadRangeAsync(url, handle, r.Start, r.End, state, ct)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task DownloadRangeAsync(
        string url, Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        long start, long end, ProgressState state, CancellationToken ct)
    {
        const int maxAttempts = 3;
        Exception? last = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            long writtenThisAttempt = 0;
            try
            {
                using var client = CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(start, end);

                using HttpResponseMessage resp = await client
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                await using Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                byte[] buffer = new byte[128 * 1024];
                long position = start;

                while (position <= end)
                {
                    int want = (int)Math.Min(buffer.Length, end - position + 1);
                    int read = await stream.ReadAsync(buffer.AsMemory(0, want), ct).ConfigureAwait(false);
                    if (read <= 0) break;

                    await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), position, ct).ConfigureAwait(false);
                    position += read;
                    writtenThisAttempt += read;
                    Interlocked.Add(ref state.Received, read);
                }

                if (position <= end)
                    throw new IOException($"分段未下载完整（{start}-{end}，停在 {position}）。");

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                // 重试前把这段已计入的进度回退，避免重复计数
                if (writtenThisAttempt > 0)
                {
                    Interlocked.Add(ref state.Received, -writtenThisAttempt);
                    writtenThisAttempt = 0;
                }

                Log.Warn($"分段 {start}-{end} 第 {attempt} 次失败：{ex.Message}");
                if (attempt < maxAttempts) await Task.Delay(500 * attempt, ct).ConfigureAwait(false);
            }
        }

        throw new IOException($"分段 {start}-{end} 下载失败。", last);
    }

    // ---------- 单线程下载 ----------

    private static async Task DownloadSingleAsync(
        string url, string tempPath, long length, ProgressState state, CancellationToken ct)
    {
        Log.Info($"单线程下载：{url}");

        using var client = CreateClient();
        using HttpResponseMessage resp = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using Stream source = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);

        byte[] buffer = new byte[128 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            Interlocked.Add(ref state.Received, read);
        }
    }

    // ---------- 工具 ----------

    private sealed class ProgressState
    {
        public long Received;
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.None
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 PSL/1.0");
        return client;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 忽略
        }
    }
}
