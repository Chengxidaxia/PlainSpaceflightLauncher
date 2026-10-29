using System.IO;
using System.Text;

namespace PSL.Core;

/// <summary>
/// 版本独立设置（对应 PCL 把版本独立设置写进 <c>版本目录\PCL\Setup.ini</c> 的做法；
/// PSL 写 <c>版本目录\PSL\Setup.ini</c>）。这样换电脑 / 移动版本文件夹时设置跟着走。
/// </summary>
public static class VersionSetup
{
    private static string Dir(GameVersion version) => Path.Combine(version.FolderPath, "PSL");
    private static string File(GameVersion version) => Path.Combine(Dir(version), "Setup.ini");

    public static bool Exists(GameVersion version) => System.IO.File.Exists(File(version));

    public static string? Read(GameVersion version, string section, string key)
    {
        string path = File(version);
        if (!System.IO.File.Exists(path)) return null;

        string? current = null;
        bool inSection = false;

        try
        {
            foreach (string line in System.IO.File.ReadLines(path))
            {
                string t = line.Trim();
                if (t.StartsWith('[') && t.EndsWith(']'))
                {
                    inSection = t.Substring(1, t.Length - 2).Equals(section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;

                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                if (t.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    current = t.Substring(eq + 1).Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"读取版本设置失败：{version.DisplayName}", ex);
        }

        return current;
    }

    public static bool ReadBool(GameVersion version, string section, string key, bool fallback = false)
        => bool.TryParse(Read(version, section, key), out bool v) ? v : fallback;

    /// <summary>写一个键（保留其余行 / BOM / CRLF）。</summary>
    public static void Write(GameVersion version, string section, string key, string value)
    {
        try
        {
            string path = File(version);
            Directory.CreateDirectory(Dir(version));

            Encoding encoding = new UTF8Encoding(false);
            List<string> lines = new();
            if (System.IO.File.Exists(path))
            {
                byte[] head = System.IO.File.ReadAllBytes(path);
                if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                    encoding = new UTF8Encoding(true);
                lines.AddRange(System.IO.File.ReadAllLines(path));
            }

            int sectionStart = -1, sectionEnd = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (!t.StartsWith('[') || !t.EndsWith(']')) continue;
                if (sectionStart >= 0) { sectionEnd = i; break; }
                if (t.Substring(1, t.Length - 2).Equals(section, StringComparison.OrdinalIgnoreCase)) sectionStart = i;
            }

            bool replaced = false;
            int from = sectionStart >= 0 ? sectionStart + 1 : 0;
            int to = sectionStart >= 0 ? sectionEnd : lines.Count;
            for (int i = from; i < to; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq <= 0 || !lines[i].Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                lines[i] = $"{key}={value}";
                replaced = true;
                break;
            }

            if (!replaced)
            {
                if (sectionStart >= 0) lines.Insert(sectionEnd, $"{key}={value}");
                else
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                    lines.Add($"[{section}]");
                    lines.Add($"{key}={value}");
                }
            }

            System.IO.File.WriteAllLines(path, lines, encoding);
        }
        catch (Exception ex)
        {
            Log.Error($"写入版本设置失败：{version.DisplayName}", ex);
        }
    }
}
