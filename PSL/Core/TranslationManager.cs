using System.IO;
using System.Text;

namespace PSL.Core;

/// <summary>一个可用的字体（对应 <c>Spaceflight Simulator_Data\Custom Fonts\</c> 下的文件）。</summary>
public sealed class FontEntry
{
    public FontEntry(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
    }

    /// <summary>字体文件完整路径。</summary>
    public string Path { get; }

    /// <summary>字体名（文件名，无扩展名）。</summary>
    public string Name { get; }

    /// <summary>文件大小文本。</summary>
    public string SizeText => new FileInfo(Path).Length > 1024 * 1024
        ? $"{new FileInfo(Path).Length / 1024 / 1024.0:F1} MB"
        : $"{new FileInfo(Path).Length / 1024} KB";
}

/// <summary>一个可用的语言包（对应 <c>Spaceflight Simulator_Data\Custom Translations\*.txt</c>）。</summary>
public sealed class LanguageEntry
{
    public LanguageEntry(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>语言包文件完整路径。</summary>
    public string Path { get; }

    /// <summary>语言包名（不含 .txt）。</summary>
    public string Name { get; }

    /// <summary>条目数（文件中形如 <c>原文=译文</c> 的行数）。</summary>
    public int Count
    {
        get
        {
            try
            {
                int n = 0;
                foreach (string line in File.ReadLines(Path))
                {
                    if (line.Contains('=') && !line.StartsWith("//") && !line.TrimStart().StartsWith("[")) n++;
                    if (n >= 9999) break;
                }
                return n;
            }
            catch
            {
                return 0;
            }
        }
    }
}

/// <summary>
/// 汉化版（SFS 翻译器）功能：读写某个版本的 <c>AutoTranslator\Config.ini</c>。
/// <para>
/// 目录约定来自现成的汉化版（<c>K:\SFS1.6</c>）：
/// <list type="bullet">
/// <item>翻译器配置：<c>&lt;版本&gt;\AutoTranslator\Config.ini</c></item>
/// <item>字体：<c>&lt;版本&gt;\Spaceflight Simulator_Data\Custom Fonts\&lt;字体名&gt;</c>（无扩展名）</item>
/// <item>语言包：<c>&lt;版本&gt;\Spaceflight Simulator_Data\Custom Translations\&lt;名称&gt;.txt</c></item>
/// <item>文本目录：<c>&lt;版本&gt;\Spaceflight Simulator_Data\Custom Translations\Text\</c></item>
/// </list>
/// 注意 ini 里两类路径的基准不同：<c>[Behaviour]</c> 的字体路径相对**版本根目录**，
/// 而 <c>[Files]</c> 的各路径相对 <b>AutoTranslator 目录</b>（带 <c>..\</c> 前缀）。
/// </para>
/// </summary>
public static class TranslationManager
{
    /// <summary>翻译器文件夹名。</summary>
    public const string TranslatorDirName = "AutoTranslator";

    /// <summary>翻译器配置文件名。</summary>
    public const string ConfigFileName = "Config.ini";

    /// <summary>版本是否带翻译器（有 AutoTranslator 文件夹）。</summary>
    public static bool HasTranslator(GameVersion version)
        => Directory.Exists(TranslatorDir(version));

    /// <summary>翻译器文件夹（<c>&lt;版本&gt;\AutoTranslator</c>）。</summary>
    public static string TranslatorDir(GameVersion version)
        => System.IO.Path.Combine(version.FolderPath, TranslatorDirName);

    /// <summary>翻译器配置文件完整路径。</summary>
    public static string ConfigPath(GameVersion version)
        => System.IO.Path.Combine(TranslatorDir(version), ConfigFileName);

    /// <summary>字体目录（<c>&lt;版本&gt;\Spaceflight Simulator_Data\Custom Fonts</c>）。</summary>
    public static string FontDir(GameVersion version)
        => System.IO.Path.Combine(version.DataPath, "Custom Fonts");

    /// <summary>语言包目录（<c>&lt;版本&gt;\Spaceflight Simulator_Data\Custom Translations</c>）。</summary>
    public static string LanguageDir(GameVersion version)
        => System.IO.Path.Combine(version.DataPath, "Custom Translations");

    // ==================== 枚举 ====================

    /// <summary>列出该版本可用的字体（按文件名排序）。</summary>
    public static List<FontEntry> ListFonts(GameVersion version)
    {
        var list = new List<FontEntry>();
        string dir = FontDir(version);
        if (!Directory.Exists(dir)) return list;

        try
        {
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (System.IO.Path.GetFileName(file).StartsWith('.')) continue;
                list.Add(new FontEntry(file));
            }
        }
        catch (Exception ex)
        {
            Log.Error("枚举字体失败", ex);
        }

        return list.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>列出该版本可用的语言包（按文件名排序）。</summary>
    public static List<LanguageEntry> ListLanguages(GameVersion version)
    {
        var list = new List<LanguageEntry>();
        string dir = LanguageDir(version);
        if (!Directory.Exists(dir)) return list;

        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*.txt"))
            {
                // 以 _ 开头的是 AutoTranslator 的预处理/后处理/替换表，不是语言包
                if (System.IO.Path.GetFileName(file).StartsWith('_')) continue;
                list.Add(new LanguageEntry(file));
            }
        }
        catch (Exception ex)
        {
            Log.Error("枚举语言包失败", ex);
        }

        return list.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ==================== 应用 ====================

    /// <summary>
    /// 应用字体：写入 <c>[Behaviour]</c> 的 <c>OverrideFont</c> 与 <c>OverrideFontTextMeshPro</c>。
    /// 路径用相对**版本根目录** 的形式（与现成汉化版的写法一致）。
    /// </summary>
    public static void ApplyFont(GameVersion version, FontEntry font)
    {
        string relative = MakeRelative(version.FolderPath, font.Path);
        WriteConfig(version, writer =>
        {
            writer["Behaviour"]["OverrideFont"] = relative;
            writer["Behaviour"]["OverrideFontTextMeshPro"] = relative;
        });

        Log.Info($"已为 {version.DisplayName} 应用字体：{font.Name}");
    }

    /// <summary>
    /// 应用语言包：写入 <c>[Files]</c> 的 <c>OutputFile</c>（相对 AutoTranslator 目录），
    /// 并顺带把 <c>Directory</c> 指向 <c>Custom Translations\Text</c>（若存在）。
    /// </summary>
    public static void ApplyLanguage(GameVersion version, LanguageEntry language)
    {
        string outputRelative = MakeRelative(TranslatorDir(version), language.Path);

        WriteConfig(version, writer =>
        {
            writer["Files"]["OutputFile"] = outputRelative;

            string textDir = System.IO.Path.Combine(LanguageDir(version), "Text");
            if (Directory.Exists(textDir))
                writer["Files"]["Directory"] = MakeRelative(TranslatorDir(version), textDir);
        });

        Log.Info($"已为 {version.DisplayName} 应用语言包：{language.Name}");
    }

    /// <summary>读取配置里当前选中的字体名（取 <c>OverrideFont</c> 的文件名）。</summary>
    public static string? CurrentFontName(GameVersion version)
    {
        string? value = ReadValue(version, "Behaviour", "OverrideFont");
        return value is null ? null : System.IO.Path.GetFileName(value.TrimEnd('\\', '/'));
    }

    /// <summary>读取配置里当前选中的语言包名（取 <c>OutputFile</c> 的文件名，不含扩展名）。</summary>
    public static string? CurrentLanguageName(GameVersion version)
    {
        string? value = ReadValue(version, "Files", "OutputFile");
        return value is null ? null : System.IO.Path.GetFileNameWithoutExtension(value);
    }

    /// <summary>确保配置文件夹与文件存在（没有就按现成汉化版的模板建一个）。</summary>
    public static void EnsureConfig(GameVersion version)
    {
        string configPath = ConfigPath(version);
        if (File.Exists(configPath)) return;

        Directory.CreateDirectory(TranslatorDir(version));
        File.WriteAllText(configPath, DefaultConfigTemplate(), new UTF8Encoding(true));
    }

    /// <summary>默认配置模板（照现成汉化版 <c>K:\SFS1.6\AutoTranslator\Config.ini</c> 的关键字段）。</summary>
    public static string DefaultConfigTemplate() =>
        "[General]\r\nLanguage=zh\r\nFromLanguage=en\r\n\r\n" +
        "[Files]\r\n" +
        "Directory=..\\Spaceflight Simulator_Data\\Custom Translations\\Text\r\n" +
        "OutputFile=..\\Spaceflight Simulator_Data\\Custom Translations\\Chinese.txt\r\n" +
        "SubstitutionFile=..\\Spaceflight Simulator_Data\\Custom Translations\\Text\\_Substitutions.txt\r\n\r\n" +
        "[TextFrameworks]\r\nEnableIMGUI=True\r\nEnableUGUI=True\r\nEnableNGUI=True\r\n" +
        "EnableTextMeshPro=True\r\nEnableTextMesh=True\r\n\r\n" +
        "[Behaviour]\r\n" +
        "OverrideFont=\r\nOverrideFontSize=\r\nOverrideFontTextMeshPro=\r\n" +
        "EnableUIResizing=True\r\nUseStaticTranslations=True\r\nHandleRichText=True\r\n";

    // ==================== ini 读写 ====================

    private static string MakeRelative(string baseDir, string fullPath)
    {
        string rel = System.IO.Path.GetRelativePath(baseDir, fullPath);
        return rel.Replace('/', '\\');
    }

    private static string? ReadValue(GameVersion version, string section, string key)
    {
        string path = ConfigPath(version);
        if (!File.Exists(path)) return null;

        string? current = null;
        bool inSection = false;

        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inSection = trimmed[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection) continue;

            int eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            if (trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                current = trimmed[(eq + 1)..].Trim();
        }

        return current;
    }

    /// <summary>
    /// 以「改指定键值、其余原样保留」的方式写 ini。
    /// 保留原有注释行、空行、键顺序与 BOM。
    /// </summary>
    private static void WriteConfig(GameVersion version, Action<IniWriter> edit)
    {
        string path = ConfigPath(version);
        EnsureConfig(version);

        Encoding encoding = DetectEncoding(path);
        List<string> lines = File.ReadAllLines(path).ToList();
        var writer = new IniWriter(lines);
        edit(writer);
        writer.Flush();
        File.WriteAllLines(path, writer.Lines, encoding);
    }

    private static Encoding DetectEncoding(string path)
    {
        byte[] buffer = File.ReadAllBytes(path);
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            return new UTF8Encoding(true);
        return new UTF8Encoding(false);
    }

    /// <summary>行式的 ini 编辑器：只改指定键，其余行原样保留。</summary>
    private sealed class IniWriter
    {
        private readonly Dictionary<string, Dictionary<string, string>> _pending = new(StringComparer.OrdinalIgnoreCase);

        public IniWriter(List<string> lines) => Lines = lines;

        public List<string> Lines { get; }

        public Dictionary<string, string> this[string section]
        {
            get
            {
                if (!_pending.TryGetValue(section, out Dictionary<string, string>? keys))
                {
                    keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _pending[section] = keys;
                }
                return keys;
            }
        }

        /// <summary>把所有待写入的键值应用到行列表里（节不存在则在文件末尾新建）。</summary>
        public void Flush()
        {
            foreach ((string section, Dictionary<string, string> keys) in _pending)
            {
                int sectionStart = -1;
                int sectionEnd = Lines.Count;

                for (int i = 0; i < Lines.Count; i++)
                {
                    string t = Lines[i].Trim();
                    if (t.StartsWith('[') && t.EndsWith(']'))
                    {
                        if (sectionStart >= 0)
                        {
                            sectionEnd = i;
                            break;
                        }

                        if (t[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase))
                            sectionStart = i;
                    }
                }

                foreach ((string key, string value) in keys)
                {
                    string line = $"{key}={value}";
                    bool replaced = false;

                    int from = sectionStart >= 0 ? sectionStart + 1 : 0;
                    int to = sectionStart >= 0 ? sectionEnd : Lines.Count;

                    for (int i = from; i < to; i++)
                    {
                        int eq = Lines[i].IndexOf('=');
                        if (eq <= 0) continue;
                        if (!Lines[i][..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

                        Lines[i] = line;
                        replaced = true;
                        break;
                    }

                    if (replaced) continue;

                    if (sectionStart >= 0)
                    {
                        Lines.Insert(sectionEnd, line);
                    }
                    else
                    {
                        if (Lines.Count > 0 && !string.IsNullOrWhiteSpace(Lines[^1])) Lines.Add("");
                        Lines.Add($"[{section}]");
                        Lines.Add(line);
                    }
                }
            }
        }
    }
}
