using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PSL.Controls;
using PSL.Core;

namespace PSL.Pages;

/// <summary>主页：左栏版本信息与启动按钮，右栏快速操作与资源概览。</summary>
public partial class PageHome : PclPage, IPageRefresh
{
    public PageHome()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Refresh();
            LoadAnnouncement();
        };
    }

    public void OnPageEnter() => Refresh();

    private void Refresh()
    {
        GameVersion? v = GameLibrary.Current;

        if (v is null)
        {
            // PCL `PageLaunchLeft.xaml.vb:664-677` 的 Case 1/2（无版本）：
            // 启动按钮不可用、小字写「未找到可用的游戏版本」、版本选择可用、版本设置隐藏。
            TxtLeftVersionName.Text = "未选择版本";
            TxtLeftVersionNumber.Text = "点击选择版本";
            TxtVersionPath.Text = GameLibrary.LibraryPath;
            TxtLaunchHint.Text = "未找到可用的游戏版本";
            BadgeVersion.Visibility = Visibility.Collapsed;
            BtnLaunch.IsEnabled = false;
            BtnVersion.IsEnabled = true;
            BtnMore.Visibility = Visibility.Collapsed;
            StatMod.Text = StatTexture.Text = StatParts.Text = StatSystem.Text = StatBlueprint.Text = "–";
            return;
        }

        // PCL `PageLaunchLeft.xaml.vb:678-684` 的 Case 3（已选中版本）：
        // 启动按钮可用、按钮下方小字就是版本名、版本设置按钮显示。
        TxtLeftVersionName.Text = v.DisplayName;
        TxtLeftVersionNumber.Text = v.ParsedVersion is null ? "点击切换" : "v" + v.ParsedVersion;
        TxtVersionPath.Text = v.FolderPath;
        TxtLaunchHint.Text = v.DisplayName;
        BtnLaunch.IsEnabled = true;
        BtnVersion.IsEnabled = true;
        BtnMore.Visibility = Visibility.Visible;

        string? number = v.ParsedVersion ?? v.ReadLocalVersionHint();
        if (!string.IsNullOrWhiteSpace(number))
        {
            TxtVersionNumber.Text = "v" + number;
            BadgeVersion.Visibility = Visibility.Visible;
        }
        else
        {
            BadgeVersion.Visibility = Visibility.Collapsed;
        }

        UpdateStats(v);
    }

    private async void UpdateStats(GameVersion v)
    {
        try
        {
            (int mods, int textures, int parts, int systems, int blueprints) = await Task.Run(() =>
                (ResourceManager.ListModules(v).Count,
                 ResourceManager.ListTexturePacks(v).Count,
                 ResourceManager.ListPartsPacks(v).Count,
                 ResourceManager.ListSolarSystems(v).Count,
                 ResourceManager.ListBlueprints(v).Count));

            StatMod.Text = mods.ToString();
            StatTexture.Text = textures.ToString();
            StatParts.Text = parts.ToString();
            StatSystem.Text = systems.ToString();
            StatBlueprint.Text = blueprints.ToString();
        }
        catch (Exception ex)
        {
            Log.Error("统计资源失败", ex);
        }
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        GameVersion? v = GameLibrary.Current;
        if (v is null) return;

        try
        {
            if (PslConfig.Current.LaunchViaSteam)
            {
                VersionManager.LaunchViaSteam();
                MainWindow.Toast("正在通过 Steam 启动游戏…");
                return;
            }

            VersionManager.LaunchDirect(v, PslConfig.Current.LaunchAsAdmin);
            MainWindow.Toast($"正在启动「{v.DisplayName}」…");
        }
        catch (Exception ex)
        {
            Log.Error("启动失败", ex);
            MessageBox.Show($"启动失败：{ex.Message}", "PlainSpaceflightLauncher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnOpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(GameLibrary.LibraryPath);
        VersionManager.OpenInExplorer(GameLibrary.LibraryPath);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        GameLibrary.Refresh();
        Refresh();
        MainWindow.Toast("已重新扫描游戏目录");
    }

    /// <summary>点左栏中部的「版本图标 + 名字」进入版本选择。</summary>
    private void PanVersion_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnVersion_Click(sender, e);

    /// <summary>
    /// 「版本选择」按钮。照 PCL <c>PageLaunchLeft.xaml.vb:606-609</c> 的 <c>BtnVersion_Click</c>：
    /// 直接切到「版本选择」副页面，而不是弹对话框。
    /// </summary>
    private void BtnVersion_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.Navigate("select");

    /// <summary>
    /// 「版本设置」按钮。照 PCL <c>PageLaunchLeft.xaml.vb:698-703</c> 的 <c>BtnMore_Click</c>：
    /// 先把选中版本加载一遍再进「版本设置」副页面（PSL 的版本信息在扫描时已就绪，故直接进）。
    /// </summary>
    private void BtnMore_Click(object sender, RoutedEventArgs e)
    {
        if (GameLibrary.Current is null) return;
        MainWindow.Instance?.Navigate("instance");
    }

    private void BtnAddVersion_Click(object sender, RoutedEventArgs e)
    {
        string? picked = FolderPicker.Pick(Window.GetWindow(this), "选择 SFS 版本文件夹（根目录需含 Spaceflight Simulator.exe）");
        if (picked is null) return;

        string folder = picked;
        if (!VersionManager.Validate(folder, out string error))
        {
            MessageBox.Show(error, "无法添加这个文件夹", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var modes = new[]
        {
            new PickDialog.Item("move", "移动到游戏目录", "同盘时瞬间完成，原文件夹会消失"),
            new PickDialog.Item("copy", "复制到游戏目录", "保留原文件夹，耗时取决于体积"),
            new PickDialog.Item("link", "创建目录联接", "不占额外空间，仅在本机有效（需要管理员权限）")
        };

        string? mode = PickDialog.Show(Window.GetWindow(this), "添加方式",
            $"即将把「{Path.GetFileName(folder)}」加入游戏目录：\n{GameLibrary.LibraryPath}", modes, "move");

        if (mode is null) return;

        try
        {
            AddVersionMode addMode = mode switch
            {
                "copy" => AddVersionMode.Copy,
                "link" => AddVersionMode.Link,
                _ => AddVersionMode.Move
            };

            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            GameVersion added = VersionManager.Add(folder, addMode);
            GameLibrary.Refresh();
            GameLibrary.Select(GameLibrary.Find(added.FolderPath));
            Refresh();
            MainWindow.Toast($"已添加版本「{added.DisplayName}」");
        }
        catch (Exception ex)
        {
            Log.Error("添加版本失败", ex);
            MessageBox.Show($"添加失败：{ex.Message}", "PlainSpaceflightLauncher",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ==================== 公告 / 自动检查更新 ====================

    private bool _announceLoading;
    private string? _latestChineseVersion;

    /// <summary>拉仓库根目录的 new.html 渲染成公告，然后顺带检查汉化版更新。</summary>
    private async void LoadAnnouncement()
    {
        if (_announceLoading) return;
        _announceLoading = true;

        try
        {
            string? html = await ChineseRepo.LoadAnnouncementAsync();
            PanAnnounce.Children.Clear();

            if (string.IsNullOrWhiteSpace(html))
            {
                TxtAnnounceEmpty.Text = "暂时读不到公告（网络不通，或仓库里没有 new.html）。";
                TxtAnnounceEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                TxtAnnounceEmpty.Visibility = Visibility.Collapsed;
                foreach ((int level, string text) in ChineseRepo.ParseAnnouncement(html))
                {
                    var block = new TextBlock
                    {
                        Text = text,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = level > 0 ? 24 : 20,
                        Margin = new Thickness(0, level switch { 1 => 0, 2 => 14, 3 => 10, _ => 3 },
                                               0, level > 0 ? 4 : 0),
                        FontSize = level switch { 1 => 19, 2 => 15, 3 => 13.5, _ => 12.5 },
                        FontWeight = level > 0 ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = level > 0
                            ? (System.Windows.Media.Brush)FindResource("ColorBrush1")
                            : (System.Windows.Media.Brush)FindResource("ColorBrushGray1")
                    };
                    PanAnnounce.Children.Add(block);
                }

                if (PanAnnounce.Children.Count == 0)
                {
                    TxtAnnounceEmpty.Text = "公告为空。";
                    TxtAnnounceEmpty.Visibility = Visibility.Visible;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("加载公告失败", ex);
            TxtAnnounceEmpty.Text = "读取公告失败：" + ex.Message;
            TxtAnnounceEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            _announceLoading = false;
        }

        await CheckUpdateAsync();
    }

    /// <summary>
    /// 当前版本启用了汉化版功能时，读本机 SLV.txt 与仓库 version.html 比对。
    /// </summary>
    private async Task CheckUpdateAsync()
    {
        HintUpdate.Visibility = Visibility.Collapsed;

        GameVersion? version = GameLibrary.Current;
        if (version is null) return;
        if (!VersionSetup.ReadBool(version, "Localization", "Enabled")) return;

        string? localRaw = ChineseRepo.ReadLocalVersion(version);
        if (string.IsNullOrWhiteSpace(localRaw)) return;

        try
        {
            string? remoteRaw = await ChineseRepo.LoadLatestVersionAsync();
            string remote = ChineseRepo.NormalizeVersion(remoteRaw);
            if (remote.Length == 0) return;

            _latestChineseVersion = remote;
            string local = ChineseRepo.NormalizeVersion(localRaw);
            if (string.Equals(local, remote, StringComparison.OrdinalIgnoreCase)) return;

            TxtUpdate.Text = $"检测到汉化版更新：「{version.DisplayName}」当前是 {local}，仓库最新是 {remote}。";
            HintUpdate.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Warn($"检查汉化版更新失败：{ex.Message}");
        }
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.Navigate("download");
        if (MainWindow.Instance?.PageHost.Content is PageDownload page) page.ShowChinese();
        if (MainWindow.Instance?.PageHost.Content is PageDownload)
            MainWindow.Toast(_latestChineseVersion is null
                ? "已打开汉化版下载列表。"
                : $"已打开汉化版下载列表，最新版本 {_latestChineseVersion}。");
    }
}
