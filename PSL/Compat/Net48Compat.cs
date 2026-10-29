using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System
{
    /// <summary>
    /// .NET Framework 4.8 缺失的 BCL 便捷方法的补齐（扩展方法放在 System 命名空间，
    /// 这样任何文件不需要额外 using 就能用，迁移成本最低）。
    /// </summary>
    internal static class Net48Extensions
    {
        /// <summary>.NET Core 的 <c>string.Contains(string, StringComparison)</c>。</summary>
        public static bool Contains(this string source, string value, StringComparison comparison)
            => source is not null && source.IndexOf(value, comparison) >= 0;

        /// <summary>.NET Core 的 <c>string.Contains(char)</c>。</summary>
        public static bool Contains(this string source, char value)
            => source is not null && source.IndexOf(value) >= 0;

        /// <summary>.NET Core 的 <c>string.EndsWith(char)</c>。</summary>
        public static bool EndsWith(this string source, char value)
            => source is not null && source.Length > 0 && source[source.Length - 1] == value;

        /// <summary>.NET Core 的 <c>string.StartsWith(char)</c>。</summary>
        public static bool StartsWith(this string source, char value)
            => source is not null && source.Length > 0 && source[0] == value;

        /// <summary>.NET 6 的 <c>string.ReplaceLineEndings()</c>。</summary>
        public static string ReplaceLineEndings(this string source, string replacement = "\n")
            => (source ?? "").Replace("\r\n", replacement).Replace('\r', '\n');

        /// <summary>让 <c>foreach ((k, v) in dict)</c> 在 net48 上可用。</summary>
        public static void Deconstruct<TKey, TValue>(
            this System.Collections.Generic.KeyValuePair<TKey, TValue> pair,
            out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }

        /// <summary>取从 <paramref name="start"/> 到末尾的子串（替代 <c>s[start..]</c>）。</summary>
        public static string Sub(this string source, int start)
            => source.Substring(start);

        /// <summary>取 <c>[start, end)</c> 区间的子串（替代 <c>s[start..end]</c>）。</summary>
        public static string Sub(this string source, int start, int end)
            => source.Substring(start, end - start);
    }

    /// <summary><c>Math.Clamp</c> 的 net48 版（int / long / double）。</summary>
    internal static class Net48Math
    {
        public static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        public static long Clamp(long value, long min, long max)
            => value < min ? min : (value > max ? max : value);

        public static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);
    }
}

namespace PSL.Core
{
    /// <summary>
    /// 文件 / 进程 / 加密等 API 的 net48 兼容封装。
    /// </summary>
    public static class Net48Compat
    {
        // ---------- 异步文件 IO（net48 没有 File.ReadAllTextAsync 等） ----------

        public static Task<string> ReadAllTextAsync(string path)
            => Task.Run(() => File.ReadAllText(path));

        public static Task<string> ReadAllTextAsync(string path, Encoding encoding)
            => Task.Run(() => File.ReadAllText(path, encoding));

        public static Task WriteAllTextAsync(string path, string contents)
            => Task.Run(() => File.WriteAllText(path, contents));

        public static Task WriteAllTextAsync(string path, string contents, Encoding encoding)
            => Task.Run(() => File.WriteAllText(path, contents, encoding));

        public static Task<byte[]> ReadAllBytesAsync(string path)
            => Task.Run(() => File.ReadAllBytes(path));

        public static Task WriteAllBytesAsync(string path, byte[] bytes)
            => Task.Run(() => File.WriteAllBytes(path, bytes));

        // ---------- 其它 ----------

        /// <summary><c>SHA1.HashData</c> 的 net48 版。</summary>
        public static byte[] Sha1(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            return sha.ComputeHash(data);
        }

        /// <summary><c>Convert.ToHexString</c> 的 net48 版。</summary>
        public static string ToHexString(byte[] data) => BitConverter.ToString(data).Replace("-", "");

        /// <summary><c>Path.GetRelativePath</c> 的 net48 版。</summary>
        public static string GetRelativePath(string relativeTo, string path)
        {
            var from = new Uri(AppendSeparator(Path.GetFullPath(relativeTo)));
            var to = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(from.MakeRelativeUri(to).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendSeparator(string path)
            => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

        /// <summary><c>Random.Shared</c> 的 net48 版。</summary>
        public static readonly Random Random = new Random();

        /// <summary>整进程结束（net48 的 <c>Process.Kill</c> 没有 bool 重载）。</summary>
        public static void KillTree(Process process)
        {
            try { process.Kill(); }
            catch { /* 已退出或没有权限 */ }
        }
    }
}
