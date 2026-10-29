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
        : Net48Math.Clamp((double)ReceivedBytes / TotalBytes * 100d, 0d, 100d);

    public TimeSpan? Eta =>
        BytesPerSecond > 1 && TotalBytes > 0 && ReceivedBytes < TotalBytes
            ? TimeSpan.FromSeconds((TotalBytes - ReceivedBytes) / BytesPerSecond)
            : null;
}

/// <summary>
/// 多线程（HTTP Range 分段）下载器。
/// <para>
/// .NET Framework 没有 <c>RandomAccess</c>，所以每段各写一个临时分片文件，
/// 全部完成后按顺序拼接；服务器不支持 Range 时退化为单流下载。
/// </para>
/// </summary>
public sealed class MultiThreadDownloader
{
    private static readonly HttpClient Client = CreateClient();

    /// <summary>下载线程数。</summary>
    public int ThreadCount { get; set; } = 8;

    /// <summary>小于该大小不分段。</summary>
    public long MinSegmentSize { get; set; } = 1L * 1024 * 1024;

    /// <summary>单次请求超时。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        var client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 PSL/1.0");
        return client;
    }

    /// <summary>下载到 <paramref name="targetPath"/>。</summary>
    public async Task DownloadAsync(
        string url,
        string targetPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        string? dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        long total;
        bool supportsRange;
        try
        {
            (total, supportsRange) = await ProbeAsync(url, ct).ConfigureAwait(false);
        }
        catch
        {
            total = 0;
            supportsRange = false;
        }

        int threads = Math.Max(1, Math.Min(ThreadCount, 32));
        if (!supportsRange || total <= 0 || total < MinSegmentSize || threads == 1)
        {
            await DownloadSingleAsync(url, targetPath, total, progress, ct).ConfigureAwait(false);
            return;
        }

        await DownloadSegmentedAsync(url, targetPath, total, threads, progress, ct).ConfigureAwait(false);
    }

    private static async Task<(long Total, bool SupportsRange)> ProbeAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using HttpResponseMessage response = await Client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            long rangeTotal = response.Content.Headers.ContentRange?.Length ?? 0;
            return (rangeTotal, rangeTotal > 0);
        }

        return (response.Content.Headers.ContentLength ?? 0, false);
    }

    private static async Task DownloadSingleAsync(
        string url,
        string targetPath,
        long total,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await Client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long length = response.Content.Headers.ContentLength ?? total;
        using Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
            1 << 16, true);

        var state = new ProgressState(total > 0 ? total : length, progress);
        byte[] buffer = new byte[1 << 16];

        while (true)
        {
            int read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
            if (read <= 0) break;

            await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
            state.Add(read);
        }

        state.Complete();
    }

    private static async Task DownloadSegmentedAsync(
        string url,
        string targetPath,
        long total,
        int threads,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        long segmentSize = total / threads;
        var parts = new List<string>(threads);
        var state = new ProgressState(total, progress);

        try
        {
            var tasks = new List<Task>(threads);
            for (int i = 0; i < threads; i++)
            {
                long from = i * segmentSize;
                long to = i == threads - 1 ? total - 1 : (from + segmentSize - 1);
                string partPath = targetPath + ".part" + i;
                parts.Add(partPath);

                long rangeFrom = from;
                long rangeTo = to;

                tasks.Add(Task.Run(async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new RangeHeaderValue(rangeFrom, rangeTo);

                    using HttpResponseMessage response = await Client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    using Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var output = new FileStream(partPath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 1 << 16, true);

                    byte[] buffer = new byte[1 << 16];
                    while (true)
                    {
                        int read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                        if (read <= 0) break;

                        await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        state.Add(read);
                    }
                }, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            using (var final = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
                       1 << 16, true))
            {
                foreach (string part in parts)
                {
                    using var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read,
                        1 << 16, true);
                    await input.CopyToAsync(final, 1 << 16, ct).ConfigureAwait(false);
                }
            }

            state.Complete();
        }
        finally
        {
            foreach (string part in parts)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { /* 忽略 */ }
            }
        }
    }

    /// <summary>进度聚合 + 限速统计（每 200ms 汇报一次）。</summary>
    private sealed class ProgressState
    {
        private readonly long _total;
        private readonly IProgress<DownloadProgress>? _progress;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _received;
        private long _lastReport;

        public ProgressState(long total, IProgress<DownloadProgress>? progress)
        {
            _total = total;
            _progress = progress;
            Report();
        }

        public void Add(int bytes)
        {
            Interlocked.Add(ref _received, bytes);

            long now = _watch.ElapsedMilliseconds;
            if (now - Interlocked.Read(ref _lastReport) < 200) return;

            Interlocked.Exchange(ref _lastReport, now);
            Report();
        }

        public void Complete()
        {
            if (_total > 0) Interlocked.Exchange(ref _received, _total);
            Report();
        }

        private void Report()
        {
            if (_progress is null) return;

            long received = Interlocked.Read(ref _received);
            double seconds = Math.Max(0.001, _watch.Elapsed.TotalSeconds);
            _progress.Report(new DownloadProgress
            {
                TotalBytes = _total,
                ReceivedBytes = received,
                BytesPerSecond = received / seconds
            });
        }
    }
}
