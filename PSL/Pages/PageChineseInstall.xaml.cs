using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PSL.Controls;
using PSL.Core;
using PSL.Core.Download;

namespace PSL.Pages;

/// <summary>
/// 汉化版安装页（副页面 <c>chinese</c>）。照 PCL <c>PageDownloadInstall.xaml</c>：
/// 顶部 62 高卡片里是「返回 / 图标 / 名称输入框」，底部居中「开始下载」按钮；
/// 本项目删掉加载器选择那一排卡片，只保留取名与下载。
/// <para>
/// 下载完会先探测压缩包是否带密码：无密码直接解压到目标目录，
/// 有密码就用内置的安装密码解压（密码不在界面展示）。
/// </para>
/// </summary>
public partial class PageChineseInstall : PclPage
{
    /// <summary>要安装的 Release（由下载页设置）。</summary>
    public static ChineseRelease? Target { get; set; }

    /// <summary>安装进行中的互斥标记。</summary>
    private bool _busy;

    public PageChineseInstall()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        ChineseRelease? release = Target;
        if (release is null)
        {
            PanMain.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            ImgLogo.Source = new BitmapImage(new Uri(ChineseRepo.IconUri));
        }
        catch (Exception ex)
        {
            Log.Warn($"加载汉化版图标失败：{ex.Message}");
        }

        TxtName.Text = release.DisplayName;
        TxtVersion.Text = release.DisplayName;
        TxtSize.Text = release.SizeText;
        TxtDate.Text = release.PublishedAt == DateTimeOffset.MinValue
            ? "未知"
            : release.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        TxtNote.Text = string.IsNullOrWhiteSpace(release.Body)
            ? "（该版本没有填写说明）"
            : release.Body.Trim();

        bool localOn = GameLibrary.Current is { } current &&
                       VersionSetup.ReadBool(current, "Localization", "Enabled");
        TxtHint.Text =
            $"安装位置：{InstallRoot()}\\" +
            "<上面输入的名字>" + Environment.NewLine +
            "下载完成后会自动解压；安装包带密码时使用内置安装密码，无需手动输入。" +
            (localOn ? Environment.NewLine + "当前版本已开启汉化版功能，安装完成后可以在「版本设置」里继续管理字体与语言包。" : "");
    }

    /// <summary>
    /// 汉化版的安装根目录：默认是版本选择页左栏选中的游戏文件夹；
    /// 该文件夹本身就是版本时（<c>K:\SFS1.6</c> 这种布局）退到它的上一级，避免版本里套版本。
    /// </summary>
    private static string InstallRoot()
    {
        string? folder = GameLibrary.SelectedFolder?.Path;
        if (string.IsNullOrWhiteSpace(folder)) return GameLibrary.LibraryPath;

        if (!GameVersion.IsGameFolder(folder)) return folder;
        return System.IO.Path.GetDirectoryName(GameFolder.Normalize(folder)) ?? folder;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.PageBack();

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_busy) return;

        ChineseRelease? release = Target;
        if (release is null) return;

        string name = (TxtName.Text ?? "").Trim();
        if (name.Length == 0)
        {
            MainWindow.Toast("请先给这个版本起个名字。");
            TxtName.Focus();
            return;
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            if (name.Contains(c))
            {
                MainWindow.Toast("名字里不能包含 \\ / : * ? \" < > | 这些字符。");
                return;
            }
        }

        // 安装根目录：选中的游戏文件夹。若它本身就是个版本（如 K:\SFS1.6），
        // 装到它下面会变成「版本里套版本」，所以改用它的上一级。
        string root = InstallRoot();
        string targetDir = Path.Combine(root, name);
        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            MessageBoxResult overwrite = MessageBox.Show(
                $"「{name}」已经存在且不是空文件夹，要继续覆盖安装吗？",
                "PlainSpaceflightLauncher", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.OK) return;
        }

        _busy = true;
        BtnStart.IsEnabled = false;

        string tempFile = Path.Combine(Paths.DownloadDir,
            $"SFSChinese-{release.Tag}-{Guid.NewGuid():N}.exe");

        string resultMessage = "";
        bool ok = false;

        bool finished = TaskDialog.Run(
            Window.GetWindow(this),
            "正在安装汉化版",
            $"「{release.DisplayName}」\n{release.SizeText}  →  {targetDir}",
            async (report, ct) =>
            {
                Directory.CreateDirectory(Paths.DownloadDir);

                var downloader = new MultiThreadDownloader
                {
                    ThreadCount = Net48Math.Clamp(PslConfig.Current.DownloadThreads, 1, 32),
                    Timeout = TimeSpan.FromMinutes(10)
                };

                var progress = new Progress<DownloadProgress>(p => report.Report((
                    p.Indeterminate
                        ? $"下载中… {PageDownload.FormatSize(p.ReceivedBytes)}"
                        : $"{PageDownload.FormatSize(p.ReceivedBytes)} / " +
                          $"{PageDownload.FormatSize(p.TotalBytes)}　" +
                          PageDownload.FormatSpeed(p.BytesPerSecond),
                    p.Indeterminate ? -1 : p.Percent * 0.9)));

                await downloader.DownloadAsync(GithubProxy.Wrap(release.AssetUrl), tempFile, progress, ct);

                report.Report(("正在校验并解压安装包…", 92));

                ExtractResult extract = await Task.Run(
                    () => ArchiveExtractor.Extract(
                        tempFile,
                        targetDir,
                        new Progress<string>(entry =>
                            report.Report(($"解压中：{Path.GetFileName(entry)}", 95))),
                        ct),
                    ct);

                if (!extract.Success) throw new IOException(extract.Message);

                resultMessage = extract.UsedPassword
                    ? $"已解压 {extract.Files} 个文件（安装包带密码，已自动使用安装密码）"
                    : $"已解压 {extract.Files} 个文件";

                ok = true;
            });

        SafeDelete(tempFile);
        _busy = false;
        BtnStart.IsEnabled = true;

        if (!(finished && ok))
        {
            if (!finished) MainWindow.Toast("安装已取消或失败，可以重试。");
            return;
        }

        // 解压后定位真正的版本目录（压缩包内可能多套一层文件夹）
        string? versionDir = FindGameRoot(targetDir);
        if (versionDir is null)
        {
            MainWindow.Toast($"{resultMessage}，但没找到 Spaceflight Simulator.exe，请检查安装包内容。");
            return;
        }

        // 缺 SLV.txt 时补一份，供后续自动检查更新
        string slv = Path.Combine(versionDir, "SLV.txt");
        if (!File.Exists(slv))
        {
            try
            {
                File.WriteAllText(slv, ChineseRepo.NormalizeVersion(release.DisplayName));
            }
            catch (Exception ex)
            {
                Log.Warn($"写入 SLV.txt 失败：{ex.Message}");
            }
        }

        GameLibrary.Refresh();
        GameLibrary.Select(GameLibrary.Find(versionDir));
        MainWindow.Toast($"{resultMessage}，已安装到「{Path.GetFileName(versionDir)}」");
        MainWindow.Instance?.PageBack();
    }

    /// <summary>在解压结果里找含 <c>Spaceflight Simulator.exe</c> 的目录（最多往下两层）。</summary>
    private static string? FindGameRoot(string dir)
    {
        if (File.Exists(Path.Combine(dir, GameVersion.GameExeName))) return dir;

        try
        {
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                if (File.Exists(Path.Combine(sub, GameVersion.GameExeName))) return sub;

                foreach (string sub2 in Directory.EnumerateDirectories(sub))
                    if (File.Exists(Path.Combine(sub2, GameVersion.GameExeName))) return sub2;
            }
        }
        catch (Exception ex)
        {
            Log.Error("查找版本目录失败", ex);
        }

        return null;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"删除临时文件失败：{path} —— {ex.Message}");
        }
    }
}
