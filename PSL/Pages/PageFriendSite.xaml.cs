using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PSL.Controls;
using PSL.Core;

namespace PSL.Pages;

/// <summary>
/// 友站页（顶栏「友站」进入）：左侧是友站列表，右侧是内置浏览器。
/// 对应 PCL 顶栏的「联机」位（<c>FormMain.xaml:123</c> 的 <c>BtnTitleSelect2</c>）。
/// </summary>
public partial class PageFriendSite : PclPage, IPageRefresh
{
    /// <summary>一个友站。</summary>
    private sealed record Site(string Name, string Url, string Desc);

    /// <summary>友站清单。</summary>
    private static readonly Site[] Sites =
    {
        new("SFS 工具站", "https://sfstools.netlify.app/", "SFS 各类在线工具合集"),
        new("涂装包制作器", "https://sfs-texture-pack-maker.pages.dev/", "在线制作 / 打包涂装包"),
        new("星系包生成器", "https://sfs-planet-pack-generator.pages.dev/", "在线生成星系包"),
        new("涂装包", "https://sfs-tanguanbang.pages.dev/", "涂装包资源站"),
        new("整合包编辑器", "https://sfs-pack-editor.pages.dev/", "在线编辑整合包")
    };

    private readonly List<PclListItem> _siteItems = new();
    private Site _current = Sites[0];

    public PageFriendSite()
    {
        InitializeComponent();

        // WebView2 需要一块可写的用户数据目录；放在 PSL 自己的数据目录下，
        // 避免用系统临时目录导致的权限 / 清理问题。
        try
        {
            Browser.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
            {
                UserDataFolder = System.IO.Path.Combine(Paths.DataDir, "WebView2")
            };
        }
        catch (Exception ex)
        {
            Log.Error("设置 WebView2 用户数据目录失败", ex);
        }

        Browser.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                Log.Info("WebView2 初始化成功");
            }
            else
            {
                Log.Error("WebView2 初始化失败", e.InitializationException);
                ShowBrowserUnavailable(e.InitializationException?.Message ?? "未知错误");
            }
        };

        Browser.NavigationStarting += Browser_NavigationStarting;
        Browser.NavigationCompleted += Browser_NavigationCompleted;

        Loaded += (_, _) =>
        {
            BuildList();
            Navigate(_current);
        };
    }

    public void OnPageEnter()
    {
        // 每次进入都重建列表，保证勾选态与当前站点一致
        BuildList();
    }

    // ==================== 左栏 ====================

    private void BuildList()
    {
        PanList.Children.Clear();
        _siteItems.Clear();

        PanList.Children.Add(SectionTitle("友站列表"));

        foreach (Site site in Sites)
        {
            Site s = site;

            var item = new PclListItem
            {
                Type = PclListItem.CheckType.RadioBox,
                Height = 40,
                MinPaddingRight = 30,
                Cursor = Cursors.Hand,
                Title = s.Name,
                Info = Host(s.Url),
                ToolTip = s.Url,
                Checked = ReferenceEquals(s, _current)
            };

            var open = new PclIconButton
            {
                Logo = TryFindResource("IconDownload") as Geometry,
                LogoScale = 0.9,
                ToolTip = "在系统浏览器中打开"
            };
            open.Click += (_, _) => OpenExternal(s.Url);
            item.AddButton(open);

            item.Click += (_, _) => Switch(s);
            _siteItems.Add(item);
            PanList.Children.Add(item);
        }

        PanList.Children.Add(SectionTitle("说明"));
        PanList.Children.Add(new TextBlock
        {
            Text = "这些站点由 SFS 社区维护，与 PlainSpaceflightLauncher 无隶属关系。\n" +
                   "页面内容与可用性由各站自行负责。",
            Margin = new Thickness(13, 0, 13, 0),
            Opacity = 0.7,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        PanList.Children.Add(new FrameworkElement { Height = 10, IsHitTestVisible = false });
    }

    /// <summary>分组小标题（PCL <c>PageSelectLeft.xaml.vb:32</c> 的 TextBlock 参数）。</summary>
    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Margin = new Thickness(13, 18, 5, 4),
        Opacity = 0.6,
        FontSize = 12
    };

    private static string Host(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return url;
        }
    }

    private void Switch(Site site)
    {
        if (ReferenceEquals(site, _current))
        {
            Navigate(site);
            return;
        }

        _current = site;
        SyncSelection();
        Navigate(site);
    }

    private void SyncSelection()
    {
        for (int i = 0; i < _siteItems.Count && i < Sites.Length; i++)
            _siteItems[i].Checked = ReferenceEquals(Sites[i], _current);
    }

    // ==================== 浏览器 ====================

    /// <summary>让 WebView2 打开某个站点，并切到「正在加载」提示。</summary>
    private void Navigate(Site site)
    {
        TxtUrl.Text = site.Url;
        LabEmptyTitle.Text = "正在加载";
        LabEmptyContent.Text = $"正在打开「{site.Name}」…\n{Host(site.Url)}";
        PanEmpty.Visibility = Visibility.Visible;
        Browser.Visibility = Visibility.Collapsed;

        try
        {
            Browser.Source = new Uri(site.Url);
        }
        catch (Exception ex)
        {
            Log.Error("打开友站失败", ex);
            LabEmptyTitle.Text = "无法打开";
            LabEmptyContent.Text = ex.Message;
        }
    }

    private void Browser_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        Browser.Visibility = Visibility.Visible;
        PanEmpty.Visibility = Visibility.Collapsed;
    }

    /// <summary>WebView2 运行时不可用时给出提示，并引导用系统浏览器打开。</summary>
    private void ShowBrowserUnavailable(string reason)
    {
        Browser.Visibility = Visibility.Collapsed;
        PanEmpty.Visibility = Visibility.Visible;
        LabEmptyTitle.Text = "无法使用内置浏览器";
        LabEmptyContent.Text =
            $"WebView2 初始化失败：{reason}\n\n" +
            "请安装 Microsoft Edge WebView2 运行时后重试，\n" +
            "或点下面的「在浏览器打开」用系统浏览器访问友站。";
    }

    private void Browser_NavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        Browser.Visibility = Visibility.Collapsed;
        PanEmpty.Visibility = Visibility.Visible;
        LabEmptyTitle.Text = "正在加载";
        LabEmptyContent.Text = "正在打开友站…";
    }

    private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Go();
    }

    private void BtnGo_Click(object sender, RoutedEventArgs e) => Go();

    private void Go()
    {
        string text = TxtUrl.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
        {
            MainWindow.Toast("无法识别这个地址。");
            return;
        }

        Site? match = Sites.FirstOrDefault(s => s.Url.StartsWith(uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _current = match;
            SyncSelection();
        }

        Navigate(new Site(match?.Name ?? uri.Host, uri.AbsoluteUri, ""));
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2 is { } core) core.Reload();
        else Navigate(_current);
    }

    private void BtnOpenExternal_Click(object sender, RoutedEventArgs e) => OpenExternal(TxtUrl.Text.Trim());

    private static void OpenExternal(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("用系统浏览器打开友站失败", ex);
        }
    }
}
