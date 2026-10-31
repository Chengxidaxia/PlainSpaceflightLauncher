using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PSL.Controls;
using PSL.Core;

namespace PSL.Pages;

/// <summary>
/// 版本选择页（PCL 的 <c>PageType.InstanceSelect</c> 副页面），由主页左栏的「版本选择」按钮进入。
/// <para>
/// 左右栏结构完全照 PCL：
/// <b>左栏 = 游戏文件夹列表</b>（<c>PageSelectLeft.xaml.vb:12-136</c> 的 <c>McFolderListUI()</c>），
/// <b>右栏 = 该文件夹下的版本列表</b>（<c>PageSelectRight.xaml.vb:25-115</c> 的 <c>McInstanceListUI()</c>）。
/// 点右栏的某个版本即选中它并返回上一页（<c>PageSelectRight.xaml.vb:167-177</c> 的 <c>Item_Click</c>）。
/// </para>
/// </summary>
public partial class PageSelect : PclPage, IPageRefresh
{
    /// <summary>左栏的文件夹项，用来同步勾选状态而不重建列表。</summary>
    private readonly List<PclListItem> _folderItems = new();

    /// <summary>本页自己触发的库变更不要反过来再刷一遍页面，避免重建两遍。</summary>
    private bool _ignoreLibraryChanged;

    public PageSelect()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public void OnPageEnter()
    {
        if (_ignoreLibraryChanged) return;
        Reload();
    }

    private void Reload()
    {
        BuildFolders();
        BuildVersions();
    }

    /// <summary>在修改游戏目录的这段时间内屏蔽 Changed 事件引起的重复刷新。</summary>
    private void IgnoreLibraryChanged(Action action)
    {
        _ignoreLibraryChanged = true;
        try
        {
            action();
        }
        finally
        {
            _ignoreLibraryChanged = false;
        }
    }

    // ==================== 左栏：游戏文件夹列表 ====================

    /// <summary>
    /// 生成左栏。逐项照 PCL <c>Pages/PageSelectLeft.xaml.vb:12-136</c> 的 <c>McFolderListUI()</c>：
    /// <list type="bullet">
    /// <item>标题「文件夹列表」：<c>Margin 13,18,5,4 / Opacity 0.6 / FontSize 12</c>（<c>:32</c>）</item>
    /// <item>每个文件夹：<c>MyListItem{ IsScaleAnimationEnabled=False, Type=RadioBox, Height=40,
    ///   MinPaddingRight=30, Title=名称, Info=路径 }</c>（<c>:75</c>）
    ///   外加一个 <c>MyIconButton{ Logo=IconButtonSetup, LogoScale=1.1 }</c>（<c>:77-82</c>）</item>
    /// <item>标题「添加或导入」（<c>:88</c>）</item>
    /// <item>添加项：<c>MyListItem{ Type=Clickable, Height=34 }</c>（<c>:92-111</c>）</item>    /// <item>末尾一个 10px 的占位（<c>:115</c>）</item>
    /// </list>
    /// PCL 的齿轮按钮弹的是「重命名 / 打开 / 刷新 / 删除」右键菜单（<c>:36-73</c>）；
    /// PSL 用同风格的选项对话框表达同样的四个操作。
    /// </summary>
    private void BuildFolders()
    {
        PanList.Children.Clear();
        _folderItems.Clear();

        PanList.Children.Add(SectionTitle("文件夹列表"));

        foreach (GameFolder folder in GameLibrary.Folders)
        {
            GameFolder f = folder;

            var item = new PclListItem
            {
                Type = PclListItem.CheckType.RadioBox,
                Height = 40,
                MinPaddingRight = 30,
                Cursor = Cursors.Hand,
                Title = f.Name,
                Info = f.Path,
                ToolTip = f.Path
            };

            var menu = new PclIconButton
            {
                Logo = TryFindResource("IconSetupGear") as Geometry,
                LogoScale = 1.1,
                ToolTip = "文件夹操作"
            };
            menu.Click += (_, _) => ShowFolderMenu(f);
            item.AddButton(menu);

            item.Click += (_, _) => SwitchFolder(f);

            item.Checked = GameLibrary.PathEquals(f.Path, GameLibrary.SelectedFolder?.Path);

            _folderItems.Add(item);
            PanList.Children.Add(item);
        }

        PanList.Children.Add(SectionTitle("添加或导入"));

        // 「添加已有文件夹」：照 PCL `PageSelectLeft.xaml.vb:100-105` 的 ItemAdd
        // （Title / Height=34 / Type=Clickable / ToolTip）。
        // 选中的目录本身就是一个版本时，会被抽象成一个只含该版本的文件夹（见 GameFolderAdder）。
        var addFolder = new PclListItem
        {
            Type = PclListItem.CheckType.Clickable,
            Height = 34,
            MinPaddingRight = 30,
            Cursor = Cursors.Hand,
            Title = "添加已有文件夹",
            LogoScale = 0.9,
            Logo = TryFindResource("IconAddFolder") as Geometry,
            ToolTip = "将一个已有的游戏文件夹添加到列表；直接选一个版本文件夹也可以"
        };
        addFolder.Click += (_, _) => AddFolder();
        PanList.Children.Add(addFolder);

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

    /// <summary>只同步左栏勾选状态，不重建列表（对应 PCL 里 RadioBox 自己维护选中态）。</summary>
    private void SyncFolderSelection()
    {
        int count = Math.Min(_folderItems.Count, GameLibrary.Folders.Count);
        for (int i = 0; i < count; i++)
        {
            _folderItems[i].Checked =
                GameLibrary.PathEquals(GameLibrary.Folders[i].Path, GameLibrary.SelectedFolder?.Path);
        }
    }

    /// <summary>
    /// 切换左栏选中的文件夹。照 PCL <c>PageSelectLeft.xaml.vb:391-403</c> 的 <c>Folder_Change</c>：
    /// 只换 <c>McFolderSelected</c> 并刷新右栏版本列表，不动左栏列表本身。
    /// </summary>
    private void SwitchFolder(GameFolder folder)
    {
        if (GameLibrary.PathEquals(folder.Path, GameLibrary.SelectedFolder?.Path)) return;

        IgnoreLibraryChanged(() => GameLibrary.SelectFolder(folder));
        SyncFolderSelection();
        BuildVersions();
    }

    /// <summary>齿轮按钮：文件夹操作（照 PCL <c>PageSelectLeft.xaml.vb:36-73</c> 的右键菜单项）。</summary>
    private void ShowFolderMenu(GameFolder folder)
    {
        var items = new List<PickDialog.Item>
        {
            new("open", "打开", folder.Path),
            new("refresh", "刷新", "重新读取这个文件夹下的版本列表")
        };
        if (!folder.IsLibrary)
        {
            items.Add(new("rename", "重命名", "只改列表里显示的名称，不会改动真实目录名"));
            items.Add(new("remove", "移出列表", "只是从列表里移除，文件夹本身不会被删除"));
        }

        string? picked = PickDialog.Show(Window.GetWindow(this), folder.Name,
            "对这个游戏文件夹执行操作：", items, "open");

        switch (picked)
        {
            case "open":
                VersionManager.OpenInExplorer(folder.Path);
                break;

            case "refresh":
                IgnoreLibraryChanged(GameLibrary.Refresh);
                Reload();
                MainWindow.Toast("已重新扫描");
                break;

            case "rename":
                string? name = InputDialog.Show(Window.GetWindow(this), "重命名文件夹",
                    "输入该文件夹在左边栏列表中显示的名称（只改显示名，不动真实目录）。",
                    folder.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                IgnoreLibraryChanged(() => GameLibrary.RenameFolder(folder, name.Trim()));
                Reload();
                break;

            case "remove":
                if (GameLibrary.RemoveFolder(folder))
                {
                    Reload();
                    MainWindow.Toast($"已把「{folder.Name}」移出列表（文件夹本身没有删除）");
                }
                break;
        }
    }

    // ==================== 右栏：该文件夹下的版本列表 ====================

    /// <summary>
    /// 生成右栏。照 PCL <c>Pages/PageSelectRight.xaml.vb:25-115</c> 的 <c>McInstanceListUI()</c>：
    /// 每个分组一张 <c>MyCard{ Title="名称 (数量)", Margin 0,0,0,15, SwapType=0 }</c>（<c>:75-80</c>），
    /// 卡片内容是一个 <c>StackPanel{ Margin = 20, 卡片标题高度, 18, 0 }</c>（<c>:77</c>），
    /// 里面按 <c>McInstance.ToListItem</c>（<c>ModMinecraft.vb:902-928</c>）生成版本项；
    /// 列表为空时显示居中的 <c>PanEmpty</c>（<c>:95-110</c>）。
    /// </summary>
    private void BuildVersions()
    {
        PanMain.Children.Clear();

        GameFolder? folder = GameLibrary.SelectedFolder;
        List<GameVersion> versions = GameLibrary.VersionsOf(folder);

        bool empty = versions.Count == 0;
        PanBackVersions.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        PanEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (empty)
        {
            LabEmptyTitle.Text = "无可用版本";
            LabEmptyContent.Text = folder is null
                ? "没有找到任何游戏文件夹。"
                : $"在「{folder.Name}」里没有找到任何 SFS 版本。\n\n" +
                  $"判定标准：「{folder.Name}」本身、或它的直接子文件夹的根目录下存在 {GameVersion.GameExeName}。\n" +
                  "只识别一层，不会递归更深层。";
            return;
        }

        var card = new PclCard
        {
            Title = $"版本 ({versions.Count})",
            Margin = new Thickness(0, 0, 0, 15)
        };
        var stack = new StackPanel { Margin = new Thickness(20, 38, 18, 0) };
        card.Children.Add(stack);
        PanMain.Children.Add(card);

        foreach (GameVersion version in versions)
            stack.Children.Add(BuildVersionItem(version));
    }

    /// <summary>
    /// 单个版本项。照 PCL <c>Modules/Minecraft/ModMinecraft.vb:902-928</c> 的 <c>ToListItem</c>：
    /// <c>Info / Height=42 / Type=Clickable / Logo / Title</c>；
    /// 右侧图标按钮照 <c>PageSelectRight.xaml.vb:116-160</c> 的 <c>McInstanceListContent</c>
    /// （PCL 是「收藏 / 删除 / 设置」，PSL 没有收藏功能，保留「移除 / 版本设置」）。
    /// </summary>
    private PclListItem BuildVersionItem(GameVersion version)
    {
        var item = new PclListItem
        {
            Type = PclListItem.CheckType.Clickable,
            Height = 42,
            Cursor = Cursors.Hand,
            Logo = TryFindResource("IconGameBox") as Geometry,
            LogoScale = 1.0,
            Title = version.DisplayName,
            Info = version.ParsedVersion is null ? "版本号未知" : "SFS " + version.ParsedVersion,
            ToolTip = version.FolderPath
        };

        var remove = new PclIconButton
        {
            Logo = TryFindResource("IconDelete") as Geometry,
            LogoScale = 1.1,
            ToolTip = "移除版本"
        };
        remove.Click += (_, _) => RemoveVersion(version);
        item.AddButton(remove);

        var setup = new PclIconButton
        {
            Logo = TryFindResource("IconSetupGear") as Geometry,
            LogoScale = 1.1,
            ToolTip = "版本设置"
        };
        setup.Click += (_, _) => OpenVersionSetup(version);
        item.AddButton(setup);

        item.Click += (_, _) => ChooseVersion(version);
        return item;
    }

    /// <summary>点整行 = 选中这个版本并返回（照 PCL <c>PageSelectRight.xaml.vb:167-177</c> 的 <c>Item_Click</c>）。</summary>
    private void ChooseVersion(GameVersion version)
    {
        IgnoreLibraryChanged(() => GameLibrary.Select(version));
        MainWindow.Instance?.PageBack();
    }

    /// <summary>齿轮按钮 = 进版本设置页（照 PCL <c>PageSelectRight.xaml.vb:140-151</c> 的 BtnCont）。</summary>
    private void OpenVersionSetup(GameVersion version)
    {
        IgnoreLibraryChanged(() => GameLibrary.Select(version));
        MainWindow.Instance?.Navigate("instance");
    }

    private void RemoveVersion(GameVersion version)
    {
        MessageBoxResult confirm = MessageBox.Show(
            Window.GetWindow(this),
            $"确定要把「{version.DisplayName}」从游戏目录移除吗？\n\n{version.FolderPath}\n\n" +
            "该文件夹会被移动到系统回收站（可从回收站恢复）。",
            "移除版本",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (confirm != MessageBoxResult.OK) return;

        try
        {
            IgnoreLibraryChanged(() => GameLibrary.RemoveVersion(version));
            Reload();
            MainWindow.Toast($"已移除版本「{version.DisplayName}」（可在回收站恢复）");
        }
        catch (Exception ex)
        {
            Log.Error("移除版本失败", ex);
            MessageBox.Show(Window.GetWindow(this), $"移除失败：{ex.Message}", "PlainSpaceflightLauncher",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 添加 ====================

    private void BtnEmptyAdd_Click(object sender, RoutedEventArgs e) => AddFolder();

    /// <summary>
    /// 「添加已有文件夹」：照 PCL <c>PageSelectLeft.xaml.vb:140-164</c> 的 <c>Add_Click</c>，
    /// 选一个目录、填显示名、登记进左栏列表。
    /// <para>
    /// 它既能加「装着多个版本的父目录」，也能直接加「本身就是版本的文件夹」——
    /// 后者会被抽象成一个只含该版本的游戏文件夹（SFS 没有 <c>versions\</c> 那一层，
    /// 玩家的版本目录通常就叫 <c>K://SFS1.6</c>）。整个过程只登记路径，**不动任何文件**，
    /// 不再有「移动到游戏目录 / 复制 / 创建目录联接」那套自创的导入方式。
    /// </para>
    /// </summary>
    private void AddFolder()
    {
        if (GameFolderAdder.Run(Window.GetWindow(this)) is null) return;
        Reload();
    }
}
