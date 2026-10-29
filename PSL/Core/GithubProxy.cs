using System.Net.Http;

namespace PSL.Core;

/// <summary>
/// GitHub 下载源（用户要求：默认 gh-proxy.com，另给两个代理、GitHub 直连、自定义）。
/// <para>
/// 用法统一为「前缀 + 完整 GitHub 链接」，与 gh-proxy.com 一致：
/// <c>https://gh-proxy.com/https://github.com/owner/repo/releases/download/tag/file</c>。
/// 自定义项由用户在设置里填入自己的前缀（同样要求前缀 + 完整链接）。
/// </para>
/// </summary>
public static class GithubProxy
{
    /// <summary>一个可选下载源。</summary>
    /// <param name="Name">显示名。</param>
    /// <param name="Prefix">前缀；直连为空串，自定义为 null（取配置）。</param>
    public sealed record Option(string Name, string? Prefix);

    /// <summary>
    /// 候选源。前三个代理都是实测可用的（对 <c>releases/download</c> 返回 206 支持分段）；
    /// gh-proxy.com 额外支持 <c>api.github.com</c> 与 <c>raw.githubusercontent.com</c>。
    /// </summary>
    public static readonly Option[] Options =
    {
        new("gh-proxy.com（默认）", "https://gh-proxy.com/"),
        new("ghfast.top", "https://ghfast.top/"),
        new("ghproxy.net", "https://ghproxy.net/"),
        new("GitHub 直连", ""),
        new("自定义…", null)
    };

    /// <summary>当前选中的索引（已归一化到合法范围）。</summary>
    public static int Index
    {
        get
        {
            int i = PslConfig.Current.GithubProxyIndex;
            return i < 0 || i >= Options.Length ? 0 : i;
        }
    }

    /// <summary>当前生效的前缀（直连返回空串）。</summary>
    public static string Prefix
    {
        get
        {
            string? prefix = Options[Index].Prefix ?? PslConfig.Current.GithubProxyCustom;
            return Normalize(prefix);
        }
    }

    /// <summary>是否直连。</summary>
    public static bool IsDirect => Index == 3;

    /// <summary>把一个 GitHub 链接包上当前下载源的前缀。</summary>
    public static string Wrap(string githubUrl)
    {
        if (string.IsNullOrWhiteSpace(githubUrl)) return githubUrl;
        if (IsDirect) return githubUrl;

        string prefix = Prefix;
        if (prefix.Length == 0) return githubUrl;
        return prefix + githubUrl;
    }

    /// <summary>规范化前缀：补协议、补结尾斜杠。</summary>
    public static string Normalize(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return "";

        string p = prefix.Trim();
        if (!p.Contains("://", StringComparison.Ordinal)) p = "https://" + p;
        if (!p.EndsWith('/')) p += "/";
        return p;
    }

    /// <summary>抓取文本时用的候选地址（当前源失败时再试直连，兼容 API 不被代理支持的情况）。</summary>
    public static IEnumerable<string> Candidates(string githubUrl)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string url in new[] { Wrap(githubUrl), githubUrl })
            if (seen.Add(url)) yield return url;
    }

    /// <summary>共用 HttpClient（超时放宽到 30 秒，代理偶发慢）。</summary>
    public static HttpClient Client { get; } = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 PSL/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
