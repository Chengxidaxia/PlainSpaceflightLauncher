using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using PSL.Controls;
using PSL.Core;
using PSL.Pages;

namespace PSL;

/// <summary>PSL 主窗口：浮动圆角窗体 + 标题栏导航 + 页面宿主。</summary>
public partial class MainWindow : Window
{
    private static MainWindow? _instance;

    private readonly Dictionary<string, UserControl> _pages = new();

    /// <summary>页面切换的延时器（对应 PCL 的 AaCode(...,110) 换页时机）。</summary>
    private DispatcherTimer? _pageSwapTimer;
    private readonly DispatcherTimer _toastTimer;
    private bool _navigating;

    // 自绘缩放状态
    private string? _resizeDir;
    private Point _resizeStartCursor;
    private double _resizeStartLeft, _resizeStartTop, _resizeStartWidth, _resizeStartHeight;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    public MainWindow()
    {
        InitializeComponent();
        _instance = this;

        PslConfig cfg = PslConfig.Current;
        if (cfg.WindowWidth >= MinWidth) Width = cfg.WindowWidth;
        if (cfg.WindowHeight >= MinHeight) Height = cfg.WindowHeight;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            FadeOutToast();
        };

        SizeChanged += (_, _) => UpdateFormClip();
        Loaded += OnLoaded;
    }

    /// <summary>当前实例（供页面调用）。</summary>
    public static MainWindow? Instance => _instance;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateFormClip();
        PlayWindowEnterAnimation();
        ApplyTheme();
        ApplyTitleBarSettings();
        ApplyNavVisibility();

        GameLibrary.Refresh();
        GameLibrary.Changed += OnLibraryChanged;
        Navigate(string.IsNullOrWhiteSpace(App.StartupPage) ? "home" : App.StartupPage!);
        StartNavSequenceIfRequested();
    }

    /// <summary>调试用 <c>--nav=a,b,c</c>：启动后按顺序自动切页，方便截图验证切换动画。</summary>
    private void StartNavSequenceIfRequested()
    {
        if (App.NavSequence.Count == 0) return;

        int index = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            if (index >= App.NavSequence.Count)
            {
                timer.Stop();
                return;
            }
            Navigate(App.NavSequence[index++]);
        };
        timer.Start();
    }

    /// <summary>
    /// 窗口入场动画（对应 PCL FormMain.xaml.vb:381-382）：
    /// 从「旋转 -4°、下移 60」回到原位；TranslateY 600ms、Rotate 500ms，延迟 100ms，
    /// 缓动为 AniEaseOutBack(Weak)。XAML 里 PanBack.RenderTransform 的初值就是 -4 / 60。
    /// </summary>
    private void PlayWindowEnterAnimation()
    {
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
        var begin = TimeSpan.FromMilliseconds(100);

        TransformRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(500)) { BeginTime = begin, EasingFunction = ease });
        TransformPos.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(600)) { BeginTime = begin, EasingFunction = ease });
    }

    private void UpdateFormClip()
    {
        if (RectForm is null || BorderForm is null) return;
        double w = BorderForm.ActualWidth;
        double h = BorderForm.ActualHeight;
        if (w > 0 && h > 0) RectForm.Rect = new Rect(0, 0, w, h);
    }

    private void OnLibraryChanged()
    {
        // 版本列表变化时让当前页面自行刷新（PCL 的做法也是由页面订阅数据变化）
        if (PageHost.Content is IPageRefresh refresh) refresh.OnPageEnter();
    }

    // ==================== 导航 ====================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _navigating) return;
        if (sender is not RadioButton rb) return;
        Navigate(NavKeyOf(rb));
    }

    private static string NavKeyOf(RadioButton rb) => rb.Name switch
    {
        "NavDownload" => "download",
        "NavFriendSite" => "friendsite",
        "NavSettings" => "settings",
        "NavHelp" => "help",
        _ => "home"
    };

    /// <summary>
    /// 副页面（PCL <c>FormMain.xaml.vb:1235</c> 的 <c>PageNameGet</c> 返回非空的那几个）。
    /// PCL 的 <c>InstanceSelect</c> / <c>InstanceSetup</c> 枚举注释都写着「这是一个副页面」，
    /// 它们不在标题栏导航里，只能从主页左栏的「版本选择」/「版本设置」按钮进入。
    /// </summary>
    private static readonly HashSet<string> SubPages = new(StringComparer.Ordinal) { "select", "instance", "detail", "chinese" };

    /// <summary>进入副页面之前所在的主页面，点返回时回到这里（对应 PCL 的 PageStack）。</summary>
    private string _lastMainKey = "home";

    /// <summary>标题栏当前是否处于副层。</summary>
    private bool _titleInnerShown;

    /// <summary>兼容旧键：<c>version</c> 已拆成 <c>select</c>（版本选择）与 <c>instance</c>（版本设置）。</summary>
    private static string NormalizeKey(string key) => key switch
    {
        "version" or "instance" => "instance",
        "select" => "select",
        "detail" => "detail",
        "chinese" => "chinese",
        "download" => "download",
        "friendsite" => "friendsite",
        "settings" => "settings",
        "help" => "help",
        _ => "home"
    };

    private static UserControl CreatePage(string key) => key switch
    {
        "download" => new PageDownload(),
        "friendsite" => new PageFriendSite(),
        "select" => new PageSelect(),
        "detail" => new PageDownloadDetail(),
        "chinese" => new PageChineseInstall(),
        "instance" => new PageInstance(),
        "settings" => new PageSettings(),
        "help" => new PageHelp(),
        _ => new PageHome()
    };

    /// <summary>副页面在标题栏上显示的名字（照 PCL <c>FormMain.xaml.vb:1235-1250</c> 的 PageNameGet）。</summary>
    private static string PageTitleName(string key) => key switch
    {
        "select" => "版本选择",
        "detail" => PageDownload.DownloadRowTitle(),
        "chinese" => PageChineseInstall.Target is { } rel ? "安装汉化版 - " + rel.DisplayName : "安装汉化版",
        "instance" => "版本设置" + (GameLibrary.Current is { } v ? " - " + v.DisplayName : ""),
        _ => ""
    };

    /// <summary>
    /// 切换页面。
    /// 时序与 PCL 的 <c>FrmMain.PageChangeAnim</c> 一致：
    /// 先播旧页退出动画（<b>不等它完成</b>）→ 110ms 后把新页放进容器 → 新页立即播进入动画。
    /// 旧页退出的同时新页进入，中间不留空白，也不会闪。
    /// </summary>
    public void Navigate(string key, bool animate = true)
    {
        key = NormalizeKey(key);
        bool isSub = SubPages.Contains(key);
        if (!isSub) _lastMainKey = key;

        if (!_pages.TryGetValue(key, out UserControl? page))
        {
            page = CreatePage(key);
            _pages[key] = page;
        }

        // 同步标题栏导航选中态（副页面不在导航栏里，保持主页面高亮）
        if (!isSub)
        {
            _navigating = true;
            RadioButton target = key switch
            {
                "download" => NavDownload,
                "friendsite" => NavFriendSite,
                "settings" => NavSettings,
                "help" => NavHelp,
                _ => NavHome
            };
            target.IsChecked = true;
            _navigating = false;
        }

        // 标题栏主层 / 副层互换
        if (isSub) ShowTitleInner(PageTitleName(key));
        else HideTitleInner();

        object? previous = PageHost.Content;

        // 首次进入、重复点击同一页、或非动画模式：直接安装
        if (!animate || previous is null || ReferenceEquals(previous, page))
        {
            Install(page);
            return;
        }

        // 旧页退出（不等待完成；退出动画结束时控件会保持透明，见 PclPageAnimation.Finish）
        if (previous is PSL.Controls.IPageAnimated leaving) leaving.TriggerHideAnimation();

        _pageSwapTimer?.Stop();
        _pageSwapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _pageSwapTimer.Tick += (_, _) =>
        {
            _pageSwapTimer?.Stop();
            _pageSwapTimer = null;
            Install(page);
        };
        _pageSwapTimer.Start();
    }

    // ==================== 标题栏双层（PCL 的 PanTitleMain / PanTitleInner） ====================

    /// <summary>
    /// 主层 → 副层。照 PCL <c>FormMain.xaml.vb:1427-1433</c>：
    /// 主层 150ms 淡出、同时把 Margin.Left 推到 12（AniEaseInFluent Weak，p=2）；
    /// 副层延迟 200ms 后 150ms 淡入、350ms 把 Margin.Left 从 -16 推回 0（AniEaseOutBack，p=3）。
    /// </summary>
    private void ShowTitleInner(string name)
    {
        if (_titleInnerShown)
        {
            // 副页面 → 另一个副页面：只换名字（PCL FormMain.xaml.vb:1407-1411）
            LabTitleInner.BeginAnimation(OpacityProperty, null);
            var swap = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
            swap.Completed += (_, _) =>
            {
                LabTitleInner.Text = name;
                LabTitleInner.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(150))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(30)
                    });
            };
            LabTitleInner.BeginAnimation(OpacityProperty, swap);
            return;
        }

        _titleInnerShown = true;
        PanTitleInner.Visibility = Visibility.Visible;
        PanTitleMain.IsHitTestVisible = false;
        PanTitleInner.IsHitTestVisible = true;
        LabTitleInner.Text = name;

        var fadeMain = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        fadeMain.Completed += (_, _) =>
        {
            PanTitleMain.BeginAnimation(OpacityProperty, null);
            PanTitleMain.Opacity = 0;
            PanTitleMain.Visibility = Visibility.Collapsed;
        };
        PanTitleMain.BeginAnimation(OpacityProperty, fadeMain);
        PanTitleMain.BeginAnimation(MarginProperty, new ThicknessAnimation(
            new Thickness(12, 0, 0, 0), TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new PclFluentEase { IsIn = true, Power = 2 }
        });

        var delay = TimeSpan.FromMilliseconds(200);
        PanTitleInner.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { BeginTime = delay });
        PanTitleInner.BeginAnimation(MarginProperty, new ThicknessAnimation(
            new Thickness(0, 0, 0, 0), TimeSpan.FromMilliseconds(350))
        {
            BeginTime = delay,
            EasingFunction = new PclBackEase { IsIn = false, Power = 3 }
        });
    }

    /// <summary>
    /// 副层 → 主层。照 PCL <c>FormMain.xaml.vb:1550-1556</c>：
    /// 副层 150ms 淡出、同时把 Margin.Left 推到 -18（AniEaseInFluent，p=3）；
    /// 主层延迟 200ms 后 150ms 淡入、350ms 把 Margin.Left 从 12 推回 0（AniEaseOutBack Weak，p=2）。
    /// </summary>
    private void HideTitleInner()
    {
        if (!_titleInnerShown) return;
        _titleInnerShown = false;

        PanTitleMain.Visibility = Visibility.Visible;
        PanTitleMain.IsHitTestVisible = true;
        PanTitleInner.IsHitTestVisible = false;

        var fadeInner = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        fadeInner.Completed += (_, _) =>
        {
            PanTitleInner.BeginAnimation(OpacityProperty, null);
            PanTitleInner.BeginAnimation(MarginProperty, null);
            PanTitleInner.Opacity = 0;
            PanTitleInner.Margin = new Thickness(-16, 0, 0, 0);
            PanTitleInner.Visibility = Visibility.Collapsed;
        };
        PanTitleInner.BeginAnimation(OpacityProperty, fadeInner);
        PanTitleInner.BeginAnimation(MarginProperty, new ThicknessAnimation(
            new Thickness(-18, 0, 0, 0), TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new PclFluentEase { IsIn = true, Power = 3 }
        });

        var delay = TimeSpan.FromMilliseconds(200);
        PanTitleMain.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { BeginTime = delay });
        PanTitleMain.BeginAnimation(MarginProperty, new ThicknessAnimation(
            new Thickness(0, 0, 0, 0), TimeSpan.FromMilliseconds(350))
        {
            BeginTime = delay,
            EasingFunction = new PclBackEase { IsIn = false, Power = 2 }
        });
    }

    // ==================== 个性化（照 PCL PageSetupUI 的设置项） ====================

    /// <summary>应用主题色（用 PCL 的 FromHSL2 算法实时重算 8 个角色色与标题栏渐变）。</summary>
    public void ApplyTheme()
    {
        PslConfig cfg = PslConfig.Current;
        PslTheme.Apply(cfg.UiHue, cfg.UiSat);
    }

    /// <summary>
    /// 应用标题栏 Logo 与背景图片（对应 PCL 的 UpdateBackgroundAndTitleBar）。
    /// 背景图挂在 PanForm 最底层，不透明度由 <see cref="PslConfig.BackgroundOpacity"/> 控制。
    /// </summary>
    public void ApplyTitleBarSettings()
    {
        PslConfig cfg = PslConfig.Current;

        // 标题栏 Logo：0=无 1=默认 2=文本
        TitleLogoDefault.Visibility = cfg.LogoType == 1 ? Visibility.Visible : Visibility.Collapsed;
        TitleLogoText.Visibility = cfg.LogoType == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (cfg.LogoType == 2) TitleLogoText.Text = string.IsNullOrWhiteSpace(cfg.LogoText) ? "PSL" : cfg.LogoText;

        // 背景图片
        double opacity = Net48Math.Clamp(cfg.BackgroundOpacity / 1000.0, 0, 1);
        if (opacity <= 0.01)
        {
            BackgroundHost.Visibility = Visibility.Collapsed;
            BackgroundHost.Background = null;
            return;
        }

        ImageSource? image = PslTheme.PickBackground();
        if (image is null)
        {
            BackgroundHost.Visibility = Visibility.Collapsed;
            BackgroundHost.Background = null;
            return;
        }

        var brush = new ImageBrush(image)
        {
            Stretch = Stretch.UniformToFill,
            Opacity = opacity
        };
        BackgroundHost.Background = brush;
        BackgroundHost.Visibility = Visibility.Visible;
    }

    /// <summary>按「功能隐藏」设置同步导航栏各页的可见性（PCL 的 HiddenRefresh）。</summary>
    public void ApplyNavVisibility()
    {
        PslConfig cfg = PslConfig.Current;
        NavDownload.Visibility = cfg.HideDownload ? Visibility.Collapsed : Visibility.Visible;
        NavFriendSite.Visibility = cfg.HideFriendSite ? Visibility.Collapsed : Visibility.Visible;
        NavSettings.Visibility = cfg.HideSettings ? Visibility.Collapsed : Visibility.Visible;
        NavHelp.Visibility = cfg.HideHelp ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>副页面的返回按钮（对应 PCL <c>FormMain.xaml.vb:1381</c> 的 <c>PageBack</c>）。</summary>
    private void BtnTitleInner_Click(object sender, RoutedEventArgs e) => PageBack();

    /// <summary>
    /// 返回进入副页面之前所在的主页面（对应 PCL 的 <c>PageBack</c>）。
    /// PCL 里版本选择页点中一个版本后就是直接 <c>FrmMain.PageBack()</c>
    /// （<c>PageSelectRight.xaml.vb:172</c>）。
    /// </summary>
    public void PageBack() => Navigate(_lastMainKey);

    /// <summary>把页面放进宿主并播放进入动画。整个过程在同一个 Dispatcher 回调里完成，不产生中间帧。</summary>
    private void Install(UserControl page)
    {
        PageHost.Content = page;

        // 先让页面把列表 / 卡片建好（否则进入动画开始时这些控件还没生成，会在动画中整块显形）
        PageHost.UpdateLayout();

        if (page is IPageRefresh refresh) refresh.OnPageEnter();
        PageHost.UpdateLayout();

        page.Opacity = 1;
        if (page is PSL.Controls.IPageAnimated animated) animated.TriggerShowAnimation();
    }

    // ==================== 标题栏与窗口操作 ====================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
        }
        catch
        {
            // 鼠标快速抬起等情况会抛错，忽略
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        PslConfig cfg = PslConfig.Current;
        if (WindowState == WindowState.Normal)
        {
            cfg.WindowWidth = Width;
            cfg.WindowHeight = Height;
        }
        cfg.Save();
    }

    // ==================== 自绘缩放（窗体为无边框透明窗口） ====================

    private void Resizer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not string dir) return;

        _resizeDir = dir;
        GetCursorPos(out NativePoint p);
        _resizeStartCursor = new Point(p.X, p.Y);
        _resizeStartLeft = Left;
        _resizeStartTop = Top;
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;

        rect.CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_resizeDir is null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndResize(e.OriginalSource as Rectangle);
            return;
        }

        GetCursorPos(out NativePoint p);
        double dx = p.X - _resizeStartCursor.X;
        double dy = p.Y - _resizeStartCursor.Y;

        double left = _resizeStartLeft;
        double top = _resizeStartTop;
        double width = _resizeStartWidth;
        double height = _resizeStartHeight;

        if (_resizeDir.Contains("Left"))
        {
            width = Math.Max(MinWidth, _resizeStartWidth - dx);
            left = _resizeStartLeft + (_resizeStartWidth - width);
        }
        else if (_resizeDir.Contains("Right"))
        {
            width = Math.Max(MinWidth, _resizeStartWidth + dx);
        }

        if (_resizeDir.Contains("Top"))
        {
            height = Math.Max(MinHeight, _resizeStartHeight - dy);
            top = _resizeStartTop + (_resizeStartHeight - height);
        }
        else if (_resizeDir.Contains("Bottom"))
        {
            height = Math.Max(MinHeight, _resizeStartHeight + dy);
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndResize(e.OriginalSource as Rectangle);
    }

    private void EndResize(Rectangle? rect)
    {
        if (_resizeDir is null) return;
        _resizeDir = null;
        if (rect is null || !rect.IsMouseCaptured) return;
        rect.ReleaseMouseCapture();
    }

    // ==================== 拖入文件一键安装 ====================

    /// <summary>拖放支持的文件类型：dll（模组）、pack（零件包）、zip（自动识别内容）。</summary>
    private static readonly string[] DroppableExtensions = { ".dll", ".pack", ".zip" };

    private static bool IsDroppable(string path)
        => DroppableExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (!hasFiles)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
        DropOverlay.Visibility = files?.Any(IsDroppable) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        string[] files = (string[])(e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>());
        InstallDroppedFiles(files);
    }

    /// <summary>
    /// 拖入文件一键安装到「当前选中的版本」。支持：
    /// <list type="bullet">
    /// <item><c>.dll</c> → DLL 模组</item>
    /// <item><c>.pack</c> → 零件包</item>
    /// <item><c>.zip</c> → 读取压缩包内的条目名自动判断是模组 / 涂装包 / 零件包 / 星系包 / 蓝图
    ///   （<see cref="ResourceManager.DetectArchiveKind"/>）；判不出来时弹窗让用户手动指定，避免装错目录。</item>
    /// </list>
    /// </summary>
    private async void InstallDroppedFiles(string[] files)
    {
        GameVersion? version = GameLibrary.Current;
        if (version is null)
        {
            Toast("还没有可用的版本，请先添加一个 SFS 版本。");
            Navigate("select");
            return;
        }

        List<string> targets = files.Where(IsDroppable).ToList();
        if (targets.Count == 0)
        {
            Toast("只支持拖入 .dll / .pack / .zip（zip 会自动识别是模组、涂装包、零件包、星系包还是蓝图）。");
            return;
        }

        // 类型判定（读 zip 条目）放到后台线程
        var jobs = new List<(string Path, SfsResourceKind Kind)>();
        var unknown = new List<string>();
        await Task.Run(() =>
        {
            foreach (string file in targets)
            {
                switch (System.IO.Path.GetExtension(file).ToLowerInvariant())
                {
                    case ".dll":
                        jobs.Add((file, SfsResourceKind.Mod));
                        break;
                    case ".pack":
                        jobs.Add((file, SfsResourceKind.PartsPack));
                        break;
                    case ".zip":
                        SfsResourceKind? detected = ResourceManager.DetectArchiveKind(file);
                        if (detected is null) unknown.Add(file);
                        else jobs.Add((file, detected.Value));
                        break;
                }
            }
        });

        // 识别不出来的压缩包：让用户指定类型
        foreach (string file in unknown)
        {
            var options = new[]
            {
                new PickDialog.Item("mod", "DLL 模组", "压缩包里有 .dll"),
                new PickDialog.Item("texture", "涂装包", "放到 Mods\\Custom_Assets\\Texture Packs"),
                new PickDialog.Item("parts", "零件包", "压缩包里有 .pack"),
                new PickDialog.Item("system", "星系包", "放到 Spaceflight Simulator_Data\\Custom Solar Systems"),
                new PickDialog.Item("blueprint", "蓝图", "压缩包里有 Blueprint.txt")
            };

            string? picked = PickDialog.Show(this, "无法自动识别类型",
                $"「{System.IO.Path.GetFileName(file)}」里没有找到可以判断类型的特征文件。\n请选择它属于哪一类：",
                options, "mod");
            if (picked is null) continue;

            jobs.Add((file, picked switch
            {
                "texture" => SfsResourceKind.TexturePack,
                "parts" => SfsResourceKind.PartsPack,
                "system" => SfsResourceKind.SolarSystem,
                "blueprint" => SfsResourceKind.Blueprint,
                _ => SfsResourceKind.Mod
            }));
        }

        if (jobs.Count == 0) return;

        int ok = 0;
        var failed = new List<string>();
        var installed = new List<string>();

        await Task.Run(() =>
        {
            foreach ((string path, SfsResourceKind kind) in jobs)
            {
                InstallResult r = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".dll" => ResourceManager.InstallDll(path, version),
                    ".pack" => ResourceManager.InstallPack(path, version),
                    _ => ResourceManager.InstallArchive(path, kind, version)
                };

                if (r.Success)
                {
                    ok++;
                    installed.Add($"{System.IO.Path.GetFileName(path)} → {ResourceManager.KindName(kind)}");
                }
                else
                {
                    failed.Add(System.IO.Path.GetFileName(path));
                }
            }
        });

        if (ok == 0 && failed.Count > 0)
        {
            Toast("安装失败：" + string.Join("、", failed));
            return;
        }

        string summary = $"已安装 {ok} 项到「{version.DisplayName}」";
        if (installed.Count > 0)
        {
            summary += "：" + string.Join("、", installed.Take(2))
                       + (installed.Count > 2 ? $" 等 {installed.Count} 项" : "");
        }
        if (failed.Count > 0)
        {
            summary += $"，{failed.Count} 项失败：{string.Join("、", failed.Take(2))}";
        }
        Toast(summary);

        foreach (UserControl p in _pages.Values)
        {
            if (p is IPageRefresh rf) rf.OnPageEnter();
        }
    }

    // ==================== 轻提示 ====================

    /// <summary>弹出一条轻提示。</summary>
    public static void Toast(string message) => _instance?.ShowToast(message);

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void FadeOutToast()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => ToastHost.Visibility = Visibility.Collapsed;
        ToastHost.BeginAnimation(OpacityProperty, fade);
    }
}

/// <summary>页面进入时刷新自身内容。</summary>
public interface IPageRefresh
{
    void OnPageEnter();
}
