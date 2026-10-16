using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PSL.Core;

/// <summary>资源类型（对应 SFS 的安装位置）。</summary>
public enum SfsResourceKind
{
    /// <summary>DLL 模组 → Mods\&lt;名称&gt;\</summary>
    Mod,

    /// <summary>涂装包 → Mods\Custom_Assets\Texture Packs\</summary>
    TexturePack,

    /// <summary>零件包 → Mods\Custom_Assets\Parts\</summary>
    PartsPack,

    /// <summary>星系包 → Spaceflight Simulator_Data\Custom Solar Systems\</summary>
    SolarSystem,

    /// <summary>蓝图 → Saving\Blueprints\</summary>
    Blueprint
}

/// <summary>SFS 汉化模组站（sfszhmod）的一条模组数据，对应 data/data.json 的一项。</summary>
public sealed class SfsModItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "zip";
    [JsonPropertyName("size")] public string Size { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";
    [JsonPropertyName("link")] public string Link { get; set; } = "";
    [JsonPropertyName("pwd")] public int Pwd { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("images")] public List<string> Images { get; set; } = new();
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("compat")] public string Compat { get; set; } = "";
    [JsonPropertyName("installType")] public string InstallType { get; set; } = "";
    [JsonPropertyName("installUrl")] public string InstallUrl { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";

    /// <summary>热度，站内是 "8.9万" 这种字符串，也可能是 null，这里按字符串收。</summary>
    [JsonPropertyName("heat")] public string? Heat { get; set; }

    /// <summary>按 installType 推断资源类型。</summary>
    [JsonIgnore]
    public SfsResourceKind Kind => InstallType switch
    {
        "dll" => SfsResourceKind.Mod,
        "textures" => SfsResourceKind.TexturePack,
        "parts" => SfsResourceKind.PartsPack,
        _ => SfsResourceKind.Mod
    };

    /// <summary>界面上的分类名。</summary>
    [JsonIgnore]
    public string KindName => Kind switch
    {
        SfsResourceKind.Mod => "DLL 模组",
        SfsResourceKind.TexturePack => "涂装包",
        SfsResourceKind.PartsPack => "零件包",
        SfsResourceKind.SolarSystem => "星系包",
        SfsResourceKind.Blueprint => "蓝图",
        _ => "未知"
    };

    [JsonIgnore] public string SubTitle => string.Join(" · ", new[] { Author, Version, Compat }.Where(s => !string.IsNullOrWhiteSpace(s)));

    [JsonIgnore] public string FirstImage => Images.FirstOrDefault() ?? "";
}

/// <summary>蓝图数据（blueprints.json 的一项）。</summary>
public sealed class SfsBlueprintItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";
    [JsonPropertyName("link")] public string Link { get; set; } = "";
    [JsonPropertyName("pwd")] public int Pwd { get; set; }
    [JsonPropertyName("images")] public List<string> Images { get; set; } = new();
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("size")] public string Size { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("requirements")] public List<BlueprintRequirement> Requirements { get; set; } = new();

    [JsonIgnore] public string FirstImage => Images.FirstOrDefault() ?? "";

    [JsonIgnore]
    public string RequirementText => Requirements.Count == 0
        ? "无需额外模组"
        : "需要模组：" + string.Join("、", Requirements.Select(r => r.Name));
}

public sealed class BlueprintRequirement
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
}

internal sealed class BlueprintRoot
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("blueprints")] public List<SfsBlueprintItem> Blueprints { get; set; } = new();
}

/// <summary>一个资源站（数据源候选）。</summary>
/// <param name="Name">界面显示名。</param>
/// <param name="BaseUrl">站点根地址。</param>
/// <param name="ShortName">缓存文件名用的短名。</param>
public sealed record ModSourceSite(string Name, string BaseUrl, string ShortName)
{
    public string ModsUrl => BaseUrl + "/data/data.json";
    public string BlueprintsUrl => BaseUrl + "/data/blueprints.json";
    public string HomeUrl => BaseUrl + "/";
}

/// <summary>SFS 模组资源站的数据源（含本地缓存，离线可用）。</summary>
public static class ModSource
{
    /// <summary>候选数据源。两个站的数据结构完全一致（同一份 schema）。</summary>
    public static readonly ModSourceSite[] Sites =
    {
        new("SFS 汉化模组站", "https://sfszhmod.pages.dev", "sfszhmod"),
        new("SFS 中文模组站", "https://sfscnmod.7813.cc.cd", "sfscnmod")
    };

    /// <summary>当前选中的数据源。</summary>
    public static ModSourceSite Current
        => Sites[Math.Clamp(PslConfig.Current.ModSourceIndex, 0, Sites.Length - 1)];

    public static string ModsUrl => Current.ModsUrl;
    public static string BlueprintsUrl => Current.BlueprintsUrl;
    public static string HomeUrl => Current.HomeUrl;

    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>缓存不使用 BOM，避免二次读取时反序列化失败。</summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>缓存按数据源分开存，切换站点时不会互相覆盖。</summary>
    private static string ModsCache => Path.Combine(Paths.CacheDir, $"{Current.ShortName}-mods.json");
    private static string BlueprintsCache => Path.Combine(Paths.CacheDir, $"{Current.ShortName}-blueprints.json");

    /// <summary>加载模组列表。force=true 时忽略缓存强制联网。</summary>
    public static async Task<List<SfsModItem>> LoadModsAsync(bool force = false, CancellationToken ct = default)
    {
        string? json = await FetchAsync(ModsUrl, ModsCache, force, ct).ConfigureAwait(false);
        if (json is null) return new List<SfsModItem>();

        try
        {
            List<SfsModItem>? items = JsonSerializer.Deserialize<List<SfsModItem>>(json, JsonOptions);
            return items ?? new List<SfsModItem>();
        }
        catch (Exception ex)
        {
            Log.Error("解析模组数据失败", ex);
            return new List<SfsModItem>();
        }
    }

    /// <summary>加载蓝图列表。</summary>
    public static async Task<List<SfsBlueprintItem>> LoadBlueprintsAsync(bool force = false, CancellationToken ct = default)
    {
        string? json = await FetchAsync(BlueprintsUrl, BlueprintsCache, force, ct).ConfigureAwait(false);
        if (json is null) return new List<SfsBlueprintItem>();

        try
        {
            BlueprintRoot? root = JsonSerializer.Deserialize<BlueprintRoot>(json, JsonOptions);
            return root?.Blueprints ?? new List<SfsBlueprintItem>();
        }
        catch (Exception ex)
        {
            Log.Error("解析蓝图数据失败", ex);
            return new List<SfsBlueprintItem>();
        }
    }

    /// <summary>数据是否来自本地缓存（用于界面提示）。</summary>
    public static bool LastLoadFromCache { get; private set; }

    private static async Task<string?> FetchAsync(string url, string cachePath, bool force, CancellationToken ct)
    {
        if (!force && File.Exists(cachePath))
        {
            try
            {
                string cached = await File.ReadAllTextAsync(cachePath, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(cached))
                {
                    LastLoadFromCache = true;
                    Log.Info($"使用本地缓存：{Path.GetFileName(cachePath)}");
                    return cached;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"读取缓存失败：{ex.Message}");
            }
        }

        try
        {
            string body = await Client.GetStringAsync(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) throw new IOException("返回内容为空。");

            LastLoadFromCache = false;
            Directory.CreateDirectory(Paths.CacheDir);
            await File.WriteAllTextAsync(cachePath, body, Utf8NoBom, ct).ConfigureAwait(false);
            Log.Info($"已更新数据：{url}");
            return body;
        }
        catch (Exception ex)
        {
            Log.Error($"获取数据失败：{url}", ex);

            // 联网失败时退回缓存
            if (File.Exists(cachePath))
            {
                try
                {
                    LastLoadFromCache = true;
                    return await File.ReadAllTextAsync(cachePath, ct).ConfigureAwait(false);
                }
                catch
                {
                    // 忽略
                }
            }

            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        // 第二个候选站（sfscnmod）带宽较小，超时给宽一点
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 PSL/1.0");
        return client;
    }
}
