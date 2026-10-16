using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PSL.Core;

/// <summary>PSL 配置（存放在 exe 同级的 PSL/config.json）。</summary>
public sealed class PslConfig
{
    /// <summary>游戏库目录（.sfs）。</summary>
    public string SfsDirectory { get; set; } = Paths.DefaultSfsDir;

    /// <summary>上次选中的版本文件夹完整路径（旧配置里可能是文件夹名，读取时会回退匹配）。</summary>
    public string? LastVersion { get; set; }

    /// <summary>
    /// 额外添加的游戏文件夹（版本选择页左栏。
    /// 对应 PCL 设置项 <c>LaunchFolders</c>，格式是「名称&gt;路径」以 | 分隔）。
    /// 游戏库根目录也可以出现在这里，此时只用于覆盖它的显示名（PCL 的 Vanilla 重命名行为）。
    /// </summary>
    public List<FolderEntry> ExtraFolders { get; set; } = new();

    /// <summary>上次选中的游戏文件夹（版本选择页左栏）。</summary>
    public string? LastFolder { get; set; }

    /// <summary>下载线程数。</summary>
    public int DownloadThreads { get; set; } = 8;

    /// <summary>下载是否优先使用直链（否则直接打开蓝奏云）。</summary>
    public bool PreferDirectLink { get; set; } = true;

    /// <summary>自定义版本显示名（key = 文件夹名）。</summary>
    public Dictionary<string, string> VersionNames { get; set; } = new();

    /// <summary>窗口宽度。</summary>
    public double WindowWidth { get; set; } = 1000;

    /// <summary>窗口高度。</summary>
    public double WindowHeight { get; set; } = 620;

    /// <summary>是否已显示过「DLL 仅支持 PC 版」提示。</summary>
    public bool DllNoticeShown { get; set; }

    /// <summary>
    /// 是否开启「汉化版功能」。
    /// <para>已改为**按版本**存储（见 <see cref="VersionSetup"/>），这个全局字段只是旧配置的兼容位。</para>
    /// </summary>
    [Obsolete("改为使用 VersionSetup.ReadBool(version, \"Localization\", \"Enabled\")")]
    public bool LocalizationEnabled { get; set; }

    /// <summary>是否通过 Steam 启动游戏（否则直接启动 exe）。</summary>
    public bool LaunchViaSteam { get; set; }

    /// <summary>是否以管理员身份启动游戏。</summary>
    public bool LaunchAsAdmin { get; set; }

    /// <summary>下载页使用的数据源索引（对应 <see cref="ModSource.Sites"/>）。</summary>
    public int ModSourceIndex { get; set; }

    // ---------- 个性化（对应 PCL 的 UiLauncher* 系列设置） ----------

    /// <summary>主题预设索引（-1 = 自定义色相/饱和度）。</summary>
    public int ThemePreset { get; set; }

    /// <summary>自定义色调（0–360，PCL 的 UiLauncherHue）。</summary>
    public int UiHue { get; set; } = 210;

    /// <summary>自定义饱和度（0–100，PCL 的 UiLauncherSat）。</summary>
    public int UiSat { get; set; } = 85;

    /// <summary>背景图不透明度（0–1000，PCL 的 UiBackgroundOpacity；0 = 不显示）。</summary>
    public int BackgroundOpacity { get; set; } = 450;

    /// <summary>标题栏 Logo 类型：0=无 1=默认（PSL 文字） 2=文本。</summary>
    public int LogoType { get; set; } = 1;

    /// <summary>标题栏自定义文本（LogoType = 2 时生效）。</summary>
    public string LogoText { get; set; } = "PlainSpaceflightLauncher";

    /// <summary>是否隐藏「下载」页（PCL 的 UiHiddenPageDownload）。</summary>
    public bool HideDownload { get; set; }

    /// <summary>是否隐藏「友站」页。</summary>
    public bool HideFriendSite { get; set; }

    /// <summary>是否隐藏「设置」页。</summary>
    public bool HideSettings { get; set; }

    /// <summary>是否隐藏「帮助」页。</summary>
    public bool HideHelp { get; set; }

    // ---------- 读写 ----------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static PslConfig? _current;

    /// <summary>当前生效的配置（首次访问时自动加载）。</summary>
    public static PslConfig Current => _current ??= Load();

    public static PslConfig Load()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
            {
                string json = File.ReadAllText(Paths.ConfigFile);
                PslConfig? cfg = JsonSerializer.Deserialize<PslConfig>(json, JsonOptions);
                if (cfg is not null)
                {
                    if (string.IsNullOrWhiteSpace(cfg.SfsDirectory)) cfg.SfsDirectory = Paths.DefaultSfsDir;
                    if (cfg.DownloadThreads is < 1 or > 64) cfg.DownloadThreads = 8;
                    cfg.ModSourceIndex = Math.Clamp(cfg.ModSourceIndex, 0, ModSource.Sites.Length - 1);
                    cfg.ExtraFolders = (cfg.ExtraFolders ?? new List<FolderEntry>())
                        .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Path))
                        .GroupBy(e => GameFolder.Normalize(e.Path), StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.Last())
                        .ToList();
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("读取配置失败，使用默认配置", ex);
        }

        return new PslConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("保存配置失败", ex);
        }
    }

    /// <summary>
    /// 获取某个版本的显示名。<paramref name="versionPath"/> 传版本文件夹的完整路径；
    /// 旧配置里存的是文件夹名，所以这里再按文件夹名回退查一次。
    /// </summary>
    public string GetVersionName(string versionPath)
    {
        string full = GameFolder.Normalize(versionPath);
        if (VersionNames.TryGetValue(full, out string? name) && !string.IsNullOrWhiteSpace(name)) return name;

        string folderName = Path.GetFileName(full);
        if (VersionNames.TryGetValue(folderName, out name) && !string.IsNullOrWhiteSpace(name)) return name;

        return folderName;
    }

    /// <summary>设置某个版本的显示名；与文件夹名相同（或为空）时清除自定义名。</summary>
    public void SetVersionName(string versionPath, string? displayName)
    {
        string full = GameFolder.Normalize(versionPath);
        string folderName = Path.GetFileName(full);

        if (string.IsNullOrWhiteSpace(displayName) || displayName == folderName)
            VersionNames.Remove(full);
        else
            VersionNames[full] = displayName.Trim();
        Save();
    }
}
