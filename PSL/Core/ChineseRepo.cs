using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PSL.Core;

/// <summary>一个汉化版 Release。</summary>
public sealed class ChineseRelease
{
    public string Tag { get; init; } = "";
    public string Name { get; init; } = "";
    public DateTimeOffset PublishedAt { get; init; }
    public string Body { get; init; } = "";
    public string AssetName { get; init; } = "";
    public long AssetSize { get; init; }

    /// <summary>显示名用 Release 的 name（不是 tag）。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Tag : Name;

    /// <summary>资源直链（GitHub 原始地址，下载时再按下载源包前缀）。</summary>
    public string AssetUrl =>
        $"https://github.com/{ChineseRepo.Owner}/{ChineseRepo.Repo}/releases/download/{Tag}/{AssetName}";

    public string SizeText => AssetSize switch
    {
        <= 0 => "大小未知",
        < 1024 * 1024 => $"{AssetSize / 1024d:F1} KB",
        < 1024L * 1024 * 1024 => $"{AssetSize / 1024d / 1024d:F1} MB",
        _ => $"{AssetSize / 1024d / 1024d / 1024d:F2} GB"
    };
}

/// <summary>
/// 汉化版仓库（<c>Chengxidaxia/SFS1.6Chinese</c>）的数据访问：
/// Release 列表、根目录 <c>new.html</c>（公告）、<c>version.html</c>（最新版本号）。
/// 全部走 <see cref="GithubProxy"/> 的下载源，带磁盘缓存（离线也能看到上次的列表）。
/// </summary>
public static class ChineseRepo
{
    public const string Owner = "Chengxidaxia";
    public const string Repo = "SFS1.6Chinese";

    private const string ApiReleases =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=30";

    private const string RawBase =
        $"https://raw.githubusercontent.com/{Owner}/{Repo}/main/";

    private static string CacheReleases => Path.Combine(Paths.CacheDir, "sfs-chinese-releases.json");

    /// <summary>最近一次是否来自缓存。</summary>
    public static bool LastLoadFromCache { get; private set; }

    /// <summary>Github 直链（不包代理）。</summary>
    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";

    /// <summary>本项目使用的 7.ico 图标（随程序打包，见 PSL.csproj 的 Resource 项）。</summary>
    public static string IconUri => "pack://application:,,,/Assets/Chinese.ico";

    /// <summary>抓 Release 列表（先网络、失败回落缓存）。</summary>
    public static async Task<List<ChineseRelease>> LoadReleasesAsync(bool force = false)
    {
        if (!force && File.Exists(CacheReleases))
        {
            List<ChineseRelease>? cached = TryParse(await Net48Compat.ReadAllTextAsync(CacheReleases));
            if (cached is { Count: > 0 })
            {
                LastLoadFromCache = true;
                return cached;
            }
        }

        Exception? last = null;
        foreach (string url in GithubProxy.Candidates(ApiReleases))
        {
            try
            {
                string json = await GithubProxy.Client.GetStringAsync(url).ConfigureAwait(false);
                List<ChineseRelease> list = TryParse(json) ?? new List<ChineseRelease>();
                if (list.Count == 0) continue;

                Directory.CreateDirectory(Paths.CacheDir);
                await Net48Compat.WriteAllTextAsync(CacheReleases, json, new UTF8Encoding(false))
                    .ConfigureAwait(false);
                LastLoadFromCache = false;
                return list;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"拉取汉化版 Release 列表失败（{url}）：{ex.Message}");
            }
        }

        if (File.Exists(CacheReleases))
        {
            List<ChineseRelease>? cached = TryParse(await Net48Compat.ReadAllTextAsync(CacheReleases));
            if (cached is { Count: > 0 })
            {
                LastLoadFromCache = true;
                return cached;
            }
        }

        if (last is not null) throw last;
        return new List<ChineseRelease>();
    }

    private static List<ChineseRelease>? TryParse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<ChineseRelease>();

            var list = new List<ChineseRelease>();
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("draft", out JsonElement draft) &&
                    draft.ValueKind == JsonValueKind.True) continue;

                string tag = Str(item, "tag_name");
                if (tag.Length == 0) continue;

                string assetName = "";
                long size = 0;
                if (item.TryGetProperty("assets", out JsonElement assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement asset in assets.EnumerateArray())
                    {
                        string name = Str(asset, "name");
                        if (name.Length == 0) continue;
                        assetName = name;
                        if (asset.TryGetProperty("size", out JsonElement sz) &&
                            sz.TryGetInt64(out long bytes)) size = bytes;
                        break;
                    }
                }

                // 老版本里有没挂资产的，跳过（否则点了也没东西可下）
                if (assetName.Length == 0) continue;

                list.Add(new ChineseRelease
                {
                    Tag = tag,
                    Name = Str(item, "name"),
                    PublishedAt = DateTimeOffset.TryParse(Str(item, "published_at"), out DateTimeOffset d)
                        ? d
                        : DateTimeOffset.MinValue,
                    Body = Str(item, "body"),
                    AssetName = assetName,
                    AssetSize = size
                });
            }

            // 按版本号降序（版本号取自 Release 名称，如 1.6.0）；解析不出来的排后面
            list.Sort((a, b) =>
            {
                int cmp = CompareVersion(b.DisplayName, a.DisplayName);
                return cmp != 0 ? cmp : b.PublishedAt.CompareTo(a.PublishedAt);
            });
            return list;
        }
        catch (Exception ex)
        {
            Log.Error("解析汉化版 Release 列表失败", ex);
            return null;
        }
    }


    /// <summary>比较两个版本号字符串（1.6.0 &gt; 1.5.10），不是版本号的按文本比较。</summary>
    private static int CompareVersion(string left, string right)
    {
        static int[] Parts(string text)
        {
            var numbers = new List<int>();
            foreach (string piece in (text ?? "").Split('.'))
            {
                int value = 0;
                foreach (char c in piece)
                {
                    if (c < '0' || c > '9') break;
                    value = value * 10 + (c - '0');
                }
                numbers.Add(value);
            }
            return numbers.ToArray();
        }

        int[] a = Parts(left);
        int[] b = Parts(right);
        int count = Math.Min(a.Length, b.Length);
        for (int i = 0; i < count; i++)
        {
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        }
        return a.Length.CompareTo(b.Length);
    }

    private static string Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    /// <summary>抓仓库根目录的文本文件（<c>version.html</c> / <c>new.html</c>）。</summary>
    public static async Task<string?> LoadRawTextAsync(string fileName)
    {
        foreach (string url in GithubProxy.Candidates(RawBase + fileName))
        {
            try
            {
                string text = await GithubProxy.Client.GetStringAsync(url).ConfigureAwait(false);
                if (text.Length > 0) return text;
            }
            catch (Exception ex)
            {
                Log.Warn($"抓取 {fileName} 失败（{url}）：{ex.Message}");
            }
        }
        return null;
    }

    /// <summary>仓库里的最新汉化版版本号（<c>version.html</c> 的内容，如 <c>1.6.0</c>）。</summary>
    public static async Task<string?> LoadLatestVersionAsync()
    {
        string? text = await LoadRawTextAsync("version.html").ConfigureAwait(false);
        return text?.Trim();
    }

    /// <summary>公告原文（<c>new.html</c>）。</summary>
    public static Task<string?> LoadAnnouncementAsync() => LoadRawTextAsync("new.html");

    /// <summary>
    /// 读取某个版本的 <c>SLV.txt</c>（汉化版写入的本机版本号），没有则返回 null。
    /// </summary>
    public static string? ReadLocalVersion(GameVersion version)
    {
        string path = Path.Combine(version.FolderPath, "SLV.txt");
        if (!File.Exists(path)) return null;

        try
        {
            string text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text.Split('\n')[0].Trim();
        }
        catch (Exception ex)
        {
            Log.Warn($"读取 SLV.txt 失败：{path} —— {ex.Message}");
            return null;
        }
    }

    /// <summary>把版本号规范化成可比对的形式（去掉前缀 v / 空白）。</summary>
    public static string NormalizeVersion(string? version)
        => (version ?? "").Trim().TrimStart('v', 'V');

    /// <summary>
    /// 极简 HTML → 文本块（公告用的就是那几个标签：h1/h2/h3/p/br）。
    /// 返回「层级 + 文本」，层级 1/2/3 是标题，0 是正文。
    /// </summary>
    public static List<(int Level, string Text)> ParseAnnouncement(string html)
    {
        var result = new List<(int, string)>();
        if (string.IsNullOrWhiteSpace(html)) return result;

        string s = html.Replace("\r", "").Replace("\n", " ");
        int i = 0;
        while (i < s.Length)
        {
            int start = s.IndexOf('<', i);
            string text = start < 0 ? s.Substring(i) : s.Substring(i, start - i);
            AddText(result, text);

            if (start < 0) break;

            int end = s.IndexOf('>', start);
            if (end < 0) break;

            string tag = s.Substring(start + 1, end - start - 1).Trim().ToLowerInvariant();
            i = end + 1;

            if (tag.StartsWith("h1")) _pendingLevel = 1;
            else if (tag.StartsWith("h2")) _pendingLevel = 2;
            else if (tag.StartsWith("h3")) _pendingLevel = 3;
            else if (tag.StartsWith("p")) _pendingLevel = 0;
            else if (tag.StartsWith("br")) AddText(result, "\n");
        }

        _pendingLevel = 0;
        return result;
    }

    [ThreadStatic] private static int _pendingLevel;

    private static void AddText(List<(int Level, string Text)> result, string raw)
    {
        string text = Decode(raw).Replace("\u00a0", " ").Trim();
        if (text.Length == 0) return;
        result.Add((_pendingLevel, text));
    }

    private static string Decode(string text) => text
        .Replace("&nbsp;", " ")
        .Replace("&lt;", "<")
        .Replace("&gt;", ">")
        .Replace("&amp;", "&")
        .Replace("&quot;", "\"")
        .Replace("&#39;", "'");
}
