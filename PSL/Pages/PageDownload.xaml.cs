using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PSL.Controls;
using PSL.Core;
using PSL.Core.Download;

namespace PSL.Pages;

/// <summary>
/// 下载页（顶栏「下载」进入）。对应 PCL 的 <c>PageDownload</c>：
/// 左栏 <c>PageDownloadLeft.xaml</c>（<c>MyScrollViewer &gt; StackPanel Margin="0,12,0,0"</c> 的
/// 分类 <c>MyListItem</c>）+ 右栏 <c>Resource/PageResource.xaml</c>
/// （搜索卡 + <c>PanProjects</c> 里的 <c>MyResourceItem</c> 列表 + 分页卡）。
/// </summary>
public partial class PageDownload : PclPage, IPageRefresh
{
    /// <summary>每页显示的资源数。</summary>
    private const int PageSize = 20;

    /// <summary>左栏分类。</summary>
    private enum Tab { Game, Mod, TexturePack, PartsPack, Blueprint }

    private List<DownloadRow> _all = new();
    private List<DownloadRow> _view = new();
    private bool _loading;
    private int _page = 1;
    private Tab _tab = Tab.Game;
    private bool _syncingControls;

    public PageDownload()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            // 数据源下拉（候选项）
            _syncingControls = true;
            try
            {
                ComboSite.ItemsSource = ModSource.Sites.Select(s => s.Name).ToList();
                ComboSite.SelectedIndex = Math.Clamp(PslConfig.Current.ModSourceIndex, 0, ModSource.Sites.Length - 1);
            }
            finally
            {
                _syncingControls = false;
            }

            // 调试用 --tab= 指定初始分类
            switch (App.StartupDownloadTab?.ToLowerInvariant())
            {
                case "mod": Tab_Click(ItemMod, EventArgs.Empty); break;
                case "texture": Tab_Click(ItemTexture, EventArgs.Empty); break;
                case "parts": Tab_Click(ItemParts, EventArgs.Empty); break;
                case "blueprint": Tab_Click(ItemBlueprint, EventArgs.Empty); break;
                default: ApplyFilter(); break;
            }
        };
    }

    public void OnPageEnter()
    {
        // 首次进入由 Loaded 负责；之后仅在数据为空时补一次
        if (!_loading && _all.Count == 0 && _tab != Tab.Game) _ = LoadAsync(false);
    }

    /// <summary>列表行数据。</summary>
    public sealed class DownloadRow
    {
        public string Name { get; init; } = "";
        public string KindName { get; init; } = "";
        public string SubTitle { get; init; } = "";
        public string Desc { get; init; } = "";
        public string Size { get; init; } = "";
        public string Date { get; init; } = "";
        public List<string> Tags { get; init; } = new();
        public string ActionText { get; init; } = "下载";
        public SfsResourceKind Kind { get; init; }
        public SfsModItem? Mod { get; init; }
        public SfsBlueprintItem? Blueprint { get; init; }

        /// <summary>资源图标（站点返回的第一张图）。</summary>
        public string? ImageUrl => Mod?.FirstImage is { Length: > 0 } u ? u : Blueprint?.FirstImage;

        public bool HasDirectLink => !string.IsNullOrWhiteSpace(Mod?.InstallUrl);

        /// <summary>资源项 Logo。</summary>
        public string LogoKey => Kind switch
        {
            SfsResourceKind.Mod => "IconMod",
            SfsResourceKind.TexturePack => "IconTexturePack",
            SfsResourceKind.PartsPack => "IconPartsPack",
            SfsResourceKind.SolarSystem => "IconSystemPack",
            SfsResourceKind.Blueprint => "IconBlueprint",
            _ => "IconGameBox"
        };
    }

    // ==================== 数据 ====================

    private async Task LoadAsync(bool force)
    {
        if (_loading) return;
        _loading = true;

        // 游戏本体是 WIP 占位，不需要拉资源数据
        if (_tab == Tab.Game)
        {
            _loading = false;
            RenderGameTab();
            return;
        }

        PanEmpty.Visibility = Visibility.Visible;
        PanContent.Visibility = Visibility.Collapsed;
        TxtTitleEmpty.Text = "正在加载";
        TxtEmpty.Text = $"正在从 {ModSource.Current.Name} 获取资源列表…";

        try
        {
            Task<List<SfsModItem>> modsTask = ModSource.LoadModsAsync(force);
            Task<List<SfsBlueprintItem>> bpTask = ModSource.LoadBlueprintsAsync(force);
            await Task.WhenAll(modsTask, bpTask);

            List<SfsModItem> mods = modsTask.Result;
            List<SfsBlueprintItem> blueprints = bpTask.Result;

            _all = new List<DownloadRow>(mods.Count + blueprints.Count);

            foreach (SfsModItem m in mods)
            {
                _all.Add(new DownloadRow
                {
                    Name = m.Name,
                    KindName = m.KindName,
                    SubTitle = m.SubTitle,
                    Desc = m.Desc,
                    Size = m.Size,
                    Date = m.Date,
                    Tags = m.Tags,
                    Kind = m.Kind,
                    Mod = m,
                    ActionText = string.IsNullOrWhiteSpace(m.InstallUrl) ? "打开网盘" : "下载"
                });
            }

            foreach (SfsBlueprintItem b in blueprints)
            {
                _all.Add(new DownloadRow
                {
                    Name = b.Name,
                    KindName = "蓝图",
                    SubTitle = b.RequirementText,
                    Desc = b.Desc,
                    Size = b.Size,
                    Date = b.Date,
                    Tags = b.Tags,
                    Kind = SfsResourceKind.Blueprint,
                    Blueprint = b,
                    ActionText = "打开网盘"
                });
            }

            TxtSource.Text = ModSource.LastLoadFromCache
                ? $"数据来自本地缓存（{ModSource.Current.Name}，点「刷新数据」可联网更新）"
                : $"资源来自 {ModSource.Current.Name}（{ModSource.Current.BaseUrl}）";

            ApplyFilter();

            if (_all.Count == 0)
            {
                TxtTitleEmpty.Text = "没有获取到数据";
                TxtEmpty.Text = $"「{ModSource.Current.Name}」没有返回数据（这个站有时会很慢或不稳定）。\n" +
                                "请检查网络连接后点「刷新数据」重试，或在上方「数据源」里换一个站。";
                PanEmpty.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Log.Error("加载下载页数据失败", ex);
            TxtTitleEmpty.Text = "加载失败";
            TxtEmpty.Text = ex.Message;
            PanEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            _loading = false;
        }
    }

    // ==================== 左栏分类 ====================

    /// <summary>
    /// 左栏点击切换分类。<see cref="PclListItem"/> 不做单选组管理，
    /// 所以这里根据点击的那一项设置 <see cref="_tab"/>，再统一同步勾选态。
    /// </summary>
    private void Tab_Click(object? sender, EventArgs e)
    {
        if (sender is not PclListItem item) return;

        _tab = item.Name switch
        {
            "ItemMod" => Tab.Mod,
            "ItemTexture" => Tab.TexturePack,
            "ItemParts" => Tab.PartsPack,
            "ItemBlueprint" => Tab.Blueprint,
            _ => Tab.Game
        };

        _page = 1;

        if (_tab == Tab.Game)
        {
            RenderGameTab();
            return;
        }

        // 第一次进某个社区资源分类时才拉数据
        if (_all.Count == 0 && !_loading) _ = LoadAsync(false);
        else ApplyFilter();
    }

    /// <summary>游戏本体（WIP）页：只显示占位卡。</summary>
    private void RenderGameTab()
    {
        ItemGame.Checked = true;
        ItemMod.Checked = ItemTexture.Checked = ItemParts.Checked = ItemBlueprint.Checked = false;

        CardSearch.Visibility = Visibility.Collapsed;
        CardGame.Visibility = Visibility.Visible;
        PanContent.Visibility = Visibility.Collapsed;
        PanEmpty.Visibility = Visibility.Collapsed;
    }

    // ==================== 过滤 / 分页 ====================

    private void SearchName_TextChanged(object? sender, EventArgs e)
    {
        _page = 1;
        ApplyFilter();
    }

    private void ComboKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingControls) return;
        _page = 1;
        ApplyFilter();
    }

    private void ComboLink_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingControls) return;
        _page = 1;
        ApplyFilter();
    }

    /// <summary>切换数据源：写配置并重新拉数据。</summary>
    private void ComboSite_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingControls) return;
        if (ComboSite.SelectedIndex < 0) return;

        int index = ComboSite.SelectedIndex;
        if (index == PslConfig.Current.ModSourceIndex) return;

        PslConfig.Current.ModSourceIndex = index;
        PslConfig.Current.Save();
        MainWindow.Toast($"数据源已切换到：{ModSource.Current.Name}");
        _ = LoadAsync(true);
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        _page = 1;
        ApplyFilter();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _syncingControls = true;
        try
        {
            SearchName.Text = "";
            ComboLink.SelectedIndex = 0;
            ComboKind.SelectedIndex = 0;
        }
        finally
        {
            _syncingControls = false;
        }

        _page = 1;
        ApplyFilter();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_tab == Tab.Game)
        {
            MainWindow.Toast("游戏本体下载功能正在开发中（WIP）");
            return;
        }

        _ = LoadAsync(true);
    }

    private void BtnOpenSteam_Click(object sender, RoutedEventArgs e) => OpenUrl(VersionManager.SteamStoreUrl);

    private void BtnGoSelect_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.Navigate("select");

    private void ApplyFilter()
    {
        // 同步左栏勾选态（PclListItem 不自动互斥）
        ItemGame.Checked = _tab == Tab.Game;
        ItemMod.Checked = _tab == Tab.Mod;
        ItemTexture.Checked = _tab == Tab.TexturePack;
        ItemParts.Checked = _tab == Tab.PartsPack;
        ItemBlueprint.Checked = _tab == Tab.Blueprint;

        if (_tab == Tab.Game)
        {
            RenderGameTab();
            return;
        }

        CardSearch.Visibility = Visibility.Visible;
        CardGame.Visibility = Visibility.Collapsed;

        IEnumerable<DownloadRow> query = _all;

        // 左栏分类
        switch (_tab)
        {
            case Tab.Mod: query = query.Where(r => r.Kind == SfsResourceKind.Mod); break;
            case Tab.TexturePack: query = query.Where(r => r.Kind == SfsResourceKind.TexturePack); break;
            case Tab.PartsPack: query = query.Where(r => r.Kind == SfsResourceKind.PartsPack); break;
            case Tab.Blueprint: query = query.Where(r => r.Blueprint is not null); break;
        }

        // 搜索卡的「类型」下拉（与左栏同效，右栏也能直接选）
        string kindText = (ComboKind.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";
        if (kindText != "全部")
        {
            query = kindText switch
            {
                "DLL 模组" => query.Where(r => r.Kind == SfsResourceKind.Mod),
                "涂装包" => query.Where(r => r.Kind == SfsResourceKind.TexturePack),
                "零件包" => query.Where(r => r.Kind == SfsResourceKind.PartsPack),
                "蓝图" => query.Where(r => r.Blueprint is not null),
                _ => query
            };
        }

        // 链接类型
        string linkText = (ComboLink.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";
        if (linkText == "有直链（可多线程下载）") query = query.Where(r => r.HasDirectLink);
        else if (linkText == "仅蓝奏云") query = query.Where(r => !r.HasDirectLink);

        // 关键字
        string keyword = (SearchName.Text ?? "").Trim();
        if (keyword.Length > 0)
        {
            query = query.Where(r =>
                r.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.SubTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.Desc.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.Tags.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        _view = query.ToList();

        int pageCount = Math.Max(1, (_view.Count + PageSize - 1) / PageSize);
        if (_page > pageCount) _page = pageCount;
        if (_page < 1) _page = 1;

        RenderPage();
    }

    private void RenderPage()
    {
        PanProjects.Children.Clear();

        int pageCount = Math.Max(1, (_view.Count + PageSize - 1) / PageSize);
        bool empty = _view.Count == 0;

        PanContent.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        PanEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        CardPages.Visibility = empty || pageCount <= 1 ? Visibility.Collapsed : Visibility.Visible;

        if (empty)
        {
            TxtTitleEmpty.Text = _all.Count == 0 ? "没有获取到数据" : "没有符合条件的资源";
            TxtEmpty.Text = _all.Count == 0
                ? $"「{ModSource.Current.Name}」没有返回数据（这个站有时会很慢或不稳定）。\n" +
                  "请检查网络连接后点「刷新数据」重试，或在上方「数据源」里换一个站。"
                : "换个关键词或筛选条件试试。";
            return;
        }

        foreach (DownloadRow row in _view.Skip((_page - 1) * PageSize).Take(PageSize))
            PanProjects.Children.Add(BuildItem(row));

        LabPage.Text = $"{_page} / {pageCount}";
        BtnPageFirst.IsEnabled = BtnPageLeft.IsEnabled = _page > 1;
        BtnPageFirst.Opacity = BtnPageLeft.Opacity = _page > 1 ? 1 : 0.2;
        BtnPageRight.IsEnabled = _page < pageCount;
        BtnPageRight.Opacity = _page < pageCount ? 1 : 0.2;
    }

    /// <summary>
    /// 生成一个资源项，参数照 PCL <c>MyResourceItem.xaml</c>：
    /// 标题 14px / Info 12px Gray3 / 下边栏 版本·大小·日期·来源 四组。
    /// </summary>
    private PclResourceItem BuildItem(DownloadRow row)
    {
        var item = new PclResourceItem
        {
            Logo = TryFindResource(row.LogoKey) as Geometry,
            Title = row.Name,
            Info = string.IsNullOrWhiteSpace(row.Desc) ? row.SubTitle : row.Desc,
            Version = row.Size,
            DownloadCount = row.HasDirectLink ? "直链" : "网盘",
            Time = row.Date,
            Source = row.KindName,
            ToolTip = $"{row.Name}\n{row.SubTitle}"
        };

        var action = new PclIconButton
        {
            Logo = TryFindResource(row.HasDirectLink ? "IconDownload" : "IconResSource") as Geometry,
            LogoScale = 1.0,
            ToolTip = row.ActionText
        };
        action.Click += (_, _) => Download_Click(row);
        item.AddButton(action);

        // 图标：先取缓存，未命中再异步下载后回填（PCL 的 MyImage 行为）
        string? imageUrl = row.ImageUrl;
        if (imageUrl is not null)
        {
            System.Windows.Media.Imaging.BitmapImage? cached = WebImageCache.FromCache(imageUrl);
            if (cached is not null) item.Image = cached;
            else _ = Task.Run(async () =>
            {
                var bmp = await WebImageCache.LoadAsync(imageUrl);
                if (bmp is null) return;
                await Dispatcher.BeginInvoke(() =>
                {
                    if (!ReferenceEquals(item.DataContext, null) || true) item.Image = bmp;
                });
            });
        }

        item.Click += (_, _) => OpenDetail(row);
        return item;
    }

    /// <summary>点资源项 → 详情页（照 PCL 的 PageDownloadCompDetail）。</summary>
    private void OpenDetail(DownloadRow row)
    {
        PageDownloadDetail.Current = row;
        MainWindow.Instance?.Navigate("detail");
    }

    // ==================== 分页按钮 ====================

    private void BtnPageFirst_Click(object sender, RoutedEventArgs e)
    {
        _page = 1;
        RenderPage();
    }

    private void BtnPageLeft_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 1) _page--;
        RenderPage();
    }

    private void BtnPageRight_Click(object sender, RoutedEventArgs e)
    {
        int pageCount = Math.Max(1, (_view.Count + PageSize - 1) / PageSize);
        if (_page < pageCount) _page++;
        RenderPage();
    }

    // ==================== 下载 / 安装 ====================

    private void Download_Click(DownloadRow row) => DownloadResource(row, Window.GetWindow(this));

    /// <summary>下载 / 打开网盘（详情页也走这里）。</summary>
    internal static void DownloadResource(DownloadRow row, Window? owner)
    {
        GameVersion? version = GameLibrary.Current;
        if (version is null)
        {
            MainWindow.Toast("请先在「版本选择」里添加一个 SFS 版本。");
            MainWindow.Instance?.Navigate("select");
            return;
        }

        // 蓝图：只有蓝奏云链接
        if (row.Blueprint is not null)
        {
            OpenUrl(row.Blueprint.Link);
            MainWindow.Toast($"已用浏览器打开「{row.Name}」的蓝奏云下载页。");
            return;
        }

        SfsModItem? mod = row.Mod;
        if (mod is null) return;

        // 没有直链 → 浏览器打开蓝奏云
        if (string.IsNullOrWhiteSpace(mod.InstallUrl))
        {
            OpenUrl(mod.Link);
            MainWindow.Toast("该资源没有直链，已用浏览器打开蓝奏云下载页。");
            return;
        }

        string safeName = SanitizeFileName(mod.Name);
        string tempFile = Path.Combine(Paths.DownloadDir, $"{safeName}-{Guid.NewGuid():N}.zip");

        bool ok = false;
        string message = "";

        if (owner is null) return;

        bool finished = TaskDialog.Run(
            owner,
            "正在下载安装",
            $"「{mod.Name}」\n{row.KindName}  →  {version.DisplayName}",
            async (report, ct) =>
            {
                var downloader = new MultiThreadDownloader
                {
                    ThreadCount = Math.Clamp(PslConfig.Current.DownloadThreads, 1, 32)
                };

                var progress = new Progress<DownloadProgress>(p =>
                    report.Report((
                        p.Indeterminate
                            ? $"下载中… {FormatSize(p.ReceivedBytes)}"
                            : $"{FormatSize(p.ReceivedBytes)} / {FormatSize(p.TotalBytes)}　{FormatSpeed(p.BytesPerSecond)}",
                        p.Indeterminate ? -1 : p.Percent)));

                await downloader.DownloadAsync(mod.InstallUrl, tempFile, progress, ct);
                report.Report(("正在解压并安装…", 100));

                InstallResult result = ResourceManager.InstallArchive(tempFile, mod.Kind, version, mod.Name);
                ok = result.Success;
                message = result.Message;

                if (!result.Success)
                {
                    // 直链下载成功但安装失败时，退化为打开网盘
                    throw new IOException(result.Message);
                }
            });

        SafeDelete(tempFile);

        if (finished && ok)
        {
            MainWindow.Toast($"已安装：{message}");
            RefreshVersionPage();
        }
        else if (!finished)
        {
            MainWindow.Toast("已取消或失败，可尝试用浏览器打开蓝奏云手动下载。");
        }
    }

    /// <summary>装完之后让当前页刷新（资源列表在「版本设置」页里）。</summary>
    private static void RefreshVersionPage()
    {
        if (MainWindow.Instance?.PageHost.Content is IPageRefresh page) page.OnPageEnter();
    }

    // ==================== 工具 ====================

    private void BtnOpenSite_Click(object sender, RoutedEventArgs e) => OpenUrl(ModSource.HomeUrl);

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"打开链接失败：{url}", ex);
        }
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d:F1} MB",
        _ => $"{bytes / 1024d / 1024d / 1024d:F2} GB"
    };

    internal static string FormatSpeed(double bytesPerSecond) => FormatSize((long)bytesPerSecond) + "/s";

    internal static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "resource" : name.Trim();
    }

    /// <summary>详情页标题栏要显示的资源名。</summary>
    internal static string DownloadRowTitle()
        => PageDownloadDetail.Current is { } row ? "资源详情 - " + row.Name : "资源详情";

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 忽略
        }
    }
}


