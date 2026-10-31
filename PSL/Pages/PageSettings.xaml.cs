using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PSL.Controls;
using PSL.Core;

namespace PSL.Pages;

/// <summary>设置页（含「关于」）。</summary>
public partial class PageSettings : PclPage, IPageRefresh
{
    private bool _updating;

    public PageSettings()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public void OnPageEnter() => Reload();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyNav();
    }

    /// <summary>按左栏选中项切换右栏分组（初始加载时也要调一次，否则三个面板会同时可见）。</summary>
    private void ApplyNav()
    {
        PanGeneral.Visibility = NavGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanUi.Visibility = NavUi.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanAbout.Visibility = NavAbout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanLog.Visibility = NavLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NavUi.IsChecked == true) SyncUi();
    }

    private void Reload()
    {
        _updating = true;
        try
        {
            PslConfig cfg = PslConfig.Current;

            ApplyNav();

            TxtLibrary.Text = cfg.SfsDirectory;
            CmbThreads.ItemsSource = new[] { "1 线程", "2 线程", "4 线程", "8 线程", "16 线程", "32 线程" };
            int index = cfg.DownloadThreads switch
            {
                1 => 0,
                2 => 1,
                4 => 2,
                16 => 4,
                32 => 5,
                _ => 3
            };
            CmbThreads.SelectedIndex = index;

            ChkPreferDirect.IsChecked = cfg.PreferDirectLink;

            CmbGithubProxy.ItemsSource = GithubProxy.Options.Select(o => o.Name).ToList();
            CmbGithubProxy.SelectedIndex = GithubProxy.Index;
            RefreshProxyHint();

            SyncUi();

            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            TxtAboutVersion.Text = $"版本 {version}　·　GPL-3.0-only　·　作者：Chengxidaxia";

            TxtLogPath.Text = "日志目录：" + Paths.LogDir;
        }
        finally
        {
            _updating = false;
        }
    }

    // ================= 常规 =================

    private void ChangeLibrary_Click(object sender, RoutedEventArgs e)
    {
        string current = PslConfig.Current.SfsDirectory;
        string? picked = FolderPicker.Pick(Window.GetWindow(this), "选择游戏目录目录（.sfs）", current);
        if (picked is null) return;

        GameLibrary.SetLibraryPath(picked);
        Reload();
        MainWindow.Toast($"游戏目录已切换到：{picked}");
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(PslConfig.Current.SfsDirectory);
        VersionManager.OpenInExplorer(PslConfig.Current.SfsDirectory);
    }

    private void CmbThreads_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;

        int threads = CmbThreads.SelectedIndex switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            4 => 16,
            5 => 32,
            _ => 8
        };

        PslConfig.Current.DownloadThreads = threads;
        PslConfig.Current.Save();
        MainWindow.Toast($"下载线程数已设为 {threads}");
    }

    private void ChkPreferDirect_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        PslConfig.Current.PreferDirectLink = ChkPreferDirect.IsChecked == true;
        PslConfig.Current.Save();
    }

    // ================= 关于 =================

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string url) return;
        OpenUrl(url);
    }

    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        string[] candidates =
        {
            Path.Combine(Paths.AppDir, "LICENSE"),
            Path.Combine(Paths.AppDir, "..", "LICENSE"),
            Path.Combine(Paths.AppDir, "..", "..", "LICENSE")
        };

        foreach (string path in candidates)
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full)) continue;
            OpenUrl(full);
            return;
        }

        MessageBox.Show(Window.GetWindow(this),
            "没有在程序目录下找到 LICENSE 文件。\n\n完整协议文本可在 GNU 官网查看：\nhttps://www.gnu.org/licenses/gpl-3.0.html",
            "LICENSE", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Paths.LogDir);
        VersionManager.OpenInExplorer(Paths.LogDir);
    }

    private void OpenAppDir_Click(object sender, RoutedEventArgs e)
        => VersionManager.OpenInExplorer(Paths.AppDir);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"打开链接失败：{url}", ex);
        }
    }

    // ================= 个性化（照 PCL PageSetupUI.xaml 的分组与设置键名） =================

    private bool _syncingUi;

    /// <summary>把当前配置刷到个性化控件上，并生成预设 / 隐藏页的复选项。</summary>
    private void SyncUi()
    {
        if (_syncingUi) return;
        _syncingUi = true;
        try
        {
            PslConfig cfg = PslConfig.Current;

            // 主题预设
            if (PanThemePresets.Children.Count == 0)
            {
                foreach (ThemePreset preset in PslTheme.Presets)
                {
                    ThemePreset p = preset;
                    var radio = new RadioButton
                    {
                        Content = p.Name,
                        GroupName = "ThemePreset",
                        Style = (Style)FindResource("SegTab"),
                        Margin = new Thickness(0, 0, 8, 0),
                        ToolTip = $"色调 {p.Hue}° / 饱和度 {p.Sat}%"
                    };
                    radio.Checked += (_, _) =>
                    {
                        if (_syncingUi) return;
                        cfg.ThemePreset = p.Id;
                        cfg.UiHue = p.Hue;
                        cfg.UiSat = p.Sat;
                        cfg.Save();
                        ApplyTheme();
                        SyncSliders();
                    };
                    PanThemePresets.Children.Add(radio);
                }

                var custom = new RadioButton
                {
                    Content = "自定义",
                    GroupName = "ThemePreset",
                    Style = (Style)FindResource("SegTab")
                };
                custom.Checked += (_, _) =>
                {
                    if (_syncingUi) return;
                    cfg.ThemePreset = -1;
                    cfg.Save();
                };
                PanThemePresets.Children.Add(custom);
            }

            // 标题栏 Logo 类型
            if (PanLogoTypes.Children.Count == 0)
            {
                string[] names = { "无", "默认", "文本" };
                for (int i = 0; i < names.Length; i++)
                {
                    int id = i;
                    var radio = new RadioButton
                    {
                        Content = names[i],
                        GroupName = "LogoType",
                        Style = (Style)FindResource("SegTab"),
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    radio.Checked += (_, _) =>
                    {
                        if (_syncingUi) return;
                        cfg.LogoType = id;
                        cfg.Save();
                        MainWindow.Instance?.ApplyTitleBarSettings();
                    };
                    PanLogoTypes.Children.Add(radio);
                }
            }

            // 功能隐藏
            if (PanHiddenPages.Children.Count == 0)
            {
                (string Name, string Key)[] pages =
                {
                    ("下载", nameof(cfg.HideDownload)),
                    ("友站", nameof(cfg.HideFriendSite)),
                    ("设置", nameof(cfg.HideSettings)),
                    ("帮助", nameof(cfg.HideHelp))
                };
                foreach ((string name, string key) in pages)
                {
                    var box = new CheckBox
                    {
                        Content = $"隐藏{name}",
                        Margin = new Thickness(0, 0, 18, 0)
                    };
                    box.Click += (_, _) =>
                    {
                        typeof(PslConfig).GetProperty(key)!.SetValue(cfg, box.IsChecked == true);
                        cfg.Save();
                        MainWindow.Instance?.ApplyNavVisibility();
                        MainWindow.Toast($"已重启后生效：导航栏的「{name}」页会被隐藏（重新打开启动器即可看到）");
                    };
                    box.Tag = key;
                    PanHiddenPages.Children.Add(box);
                }
            }

            // 反写状态
            for (int i = 0; i < PanThemePresets.Children.Count; i++)
            {
                if (PanThemePresets.Children[i] is RadioButton rb)
                    rb.IsChecked = (cfg.ThemePreset == -1 ? i == PanThemePresets.Children.Count - 1 : i == Net48Math.Clamp(cfg.ThemePreset, 0, PslTheme.Presets.Length - 1));
            }

            SyncSliders();

            for (int i = 0; i < PanLogoTypes.Children.Count; i++)
                if (PanLogoTypes.Children[i] is RadioButton rb)
                    rb.IsChecked = i == Net48Math.Clamp(cfg.LogoType, 0, 2);

            PanLogoText.Visibility = cfg.LogoType == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (TxtLogoText.Text != cfg.LogoText) TxtLogoText.Text = cfg.LogoText;

            foreach (var box in PanHiddenPages.Children.OfType<CheckBox>())
            {
                box.IsChecked = (bool)(typeof(PslConfig).GetProperty((string)box.Tag)!.GetValue(cfg) ?? false);
            }
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void SyncSliders()
    {
        SliderHue.Value = Net48Math.Clamp(PslConfig.Current.UiHue, 0, 360);
        SliderSat.Value = Net48Math.Clamp(PslConfig.Current.UiSat, 0, 100);
        SliderBgOpacity.Value = Net48Math.Clamp(PslConfig.Current.BackgroundOpacity / 10, 0, 100);
    }

    private void SliderHueSat_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || SliderHue is null || SliderSat is null) return;

        PslConfig cfg = PslConfig.Current;
        cfg.UiHue = (int)SliderHue.Value;
        cfg.UiSat = (int)SliderSat.Value;
        cfg.ThemePreset = -1;
        cfg.Save();
        ApplyTheme();

        // 自定义滑条变化后把预设选中态改成「自定义」
        if (PanThemePresets.Children.Count > 0 && PanThemePresets.Children[PanThemePresets.Children.Count - 1] is RadioButton custom)
        {
            _syncingUi = true;
            custom.IsChecked = true;
            _syncingUi = false;
        }
    }

    private void SliderBgOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || SliderBgOpacity is null) return;
        PslConfig.Current.BackgroundOpacity = (int)(SliderBgOpacity.Value * 10);
        PslConfig.Current.Save();
        MainWindow.Instance?.ApplyTitleBarSettings();
    }

    private void TxtLogoText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi || TxtLogoText is null) return;
        PslConfig.Current.LogoText = TxtLogoText.Text;
        PslConfig.Current.Save();
        MainWindow.Instance?.ApplyTitleBarSettings();
    }

    private void BtnBackgroundOpen_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(PslTheme.BackgroundDir);
        VersionManager.OpenInExplorer(PslTheme.BackgroundDir);
    }

    private void BtnBackgroundRefresh_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.ApplyTitleBarSettings();
        MainWindow.Toast("已随机加载一张背景图片");
    }

    private void BtnBackgroundClear_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (string file in Directory.GetFiles(PslTheme.BackgroundDir)) File.Delete(file);
            MainWindow.Instance?.ApplyTitleBarSettings();
            MainWindow.Toast("已清空背景图片");
        }
        catch (Exception ex)
        {
            Log.Error("清空背景图片失败", ex);
        }
    }

    private static void ApplyTheme() => MainWindow.Instance?.ApplyTheme();

    // ==================== 汉化版下载源 ====================

    private void RefreshProxyHint()
    {
        int index = GithubProxy.Index;
        string prefix = GithubProxy.Prefix;

        TxtProxyHint.Text = index switch
        {
            3 => "GitHub 直连：不经过任何代理，链接原样使用（国内网络可能很慢或打不开）。",
            4 => prefix.Length == 0
                ? "还没有填写自定义前缀，点右边「修改前缀」填一个（用法与 gh-proxy.com 一致）。"
                : "自定义前缀：" + prefix + "github.com/…",
            _ => "当前前缀：" + prefix + " ；用法与 gh-proxy.com 一致（前缀 + 完整 GitHub 链接）。"
        };

        BtnProxyEdit.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CmbGithubProxy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || CmbGithubProxy.SelectedIndex < 0) return;

        PslConfig cfg = PslConfig.Current;
        int index = CmbGithubProxy.SelectedIndex;

        // 选到「自定义」但还没填前缀 → 直接弹输入框
        if (index == 4 && string.IsNullOrWhiteSpace(cfg.GithubProxyCustom))
        {
            string? prefix = InputDialog.Show(Window.GetWindow(this), "自定义下载源",
                "填一个前缀，用法与 gh-proxy.com 一致：\n前缀 + 完整 GitHub 链接（例如 https://example.com/）。",
                "https://");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                _updating = true;
                CmbGithubProxy.SelectedIndex = cfg.GithubProxyIndex;
                _updating = false;
                return;
            }
            cfg.GithubProxyCustom = GithubProxy.Normalize(prefix);
        }

        cfg.GithubProxyIndex = index;
        cfg.Save();
        RefreshProxyHint();
        MainWindow.Toast($"下载源已切换到：{GithubProxy.Options[index].Name}");
    }

    private void BtnProxyEdit_Click(object sender, RoutedEventArgs e)
    {
        PslConfig cfg = PslConfig.Current;
        string? prefix = InputDialog.Show(Window.GetWindow(this), "自定义下载源",
            "修改前缀（用法与 gh-proxy.com 一致：前缀 + 完整 GitHub 链接）：",
            string.IsNullOrWhiteSpace(cfg.GithubProxyCustom) ? "https://" : cfg.GithubProxyCustom);

        if (string.IsNullOrWhiteSpace(prefix)) return;

        cfg.GithubProxyCustom = GithubProxy.Normalize(prefix);
        cfg.GithubProxyIndex = 4;
        cfg.Save();

        _updating = true;
        CmbGithubProxy.SelectedIndex = 4;
        _updating = false;

        RefreshProxyHint();
        MainWindow.Toast("自定义下载源已保存：" + cfg.GithubProxyCustom);
    }
}
