using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PSL.Controls;
using PSL.Core;

namespace PSL.Pages;

/// <summary>
/// 版本设置页。对应 PCL 的 <c>PageType.InstanceSetup</c> 副页面
/// （<c>PageInstanceLeft</c> + 各分类右栏），由主页左栏的「版本设置」按钮进入
/// （PCL <c>PageLaunchLeft.xaml.vb:698-703</c> 的 <c>BtnMore_Click</c> → <c>PageChange(InstanceSetup, 0)</c>）。
/// <para>
/// 右栏的卡片结构照 <c>Pages/PageInstance/PageInstanceOverall.xaml</c>（概览：卡片 <c>Margin="0,15"</c>
/// / <c>"0,0,0,15"</c>、内容 <c>Margin="25,40,25,15"</c> 或 <c>"25,30,25,20"</c> 的 WrapPanel）
/// 与 <c>Pages/PageInstance/PageInstanceSetup.xaml</c>（设置：行高 28 / 间距 9 的标签 + 控件网格）。
/// 资源列表改用 <see cref="PclLocalItem"/>（对应 PCL 的 <c>MyLocalModItem</c>）。
/// </para>
/// </summary>
public partial class PageInstance : PclPage, IPageRefresh
{
    /// <summary>左栏分类，顺序与 XAML 里 ItemOverall … ItemLanguage 一致。</summary>
    private enum Section
    {
        Overview, Setup, Mod, TexturePack, PartsPack, SolarSystem, Blueprint, Font, Language
    }

    private static readonly SolidColorBrush BrushOkBg = new(Color.FromArgb(0x22, 0x3F, 0xA3, 0x4D));
    private static readonly SolidColorBrush BrushOkFg = new(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush BrushOffBg = new(Color.FromArgb(0x22, 0x8C, 0x8C, 0x8C));
    private static readonly SolidColorBrush BrushOffFg = new(Color.FromRgb(0x73, 0x73, 0x73));

    private Section _section = Section.Overview;

    /// <summary>资源列表的筛选关键字。</summary>
    private string _filter = "";

    /// <summary>初始化 / 刷新时屏蔽开关的 Checked 事件，避免误弹提示。</summary>
    private bool _syncingSwitches;

    public PageInstance()
    {
        InitializeComponent();

        Loaded += (_, _) => Reload();
    }

    public void OnPageEnter() => Reload();

    // ==================== 刷新 ====================

    private void Reload()
    {
        GameVersion? current = GameLibrary.Current;
        TxtVersionName.Text = current is null ? "还没有可用的版本" : current.DisplayName;
        TxtVersionPath.Text = current is null
            ? "游戏目录：" + GameLibrary.LibraryPath
            : current.FolderPath;

        // 设置页的开关状态
        _syncingSwitches = true;
        try
        {
            bool localOn = current is not null && VersionSetup.ReadBool(current, "Localization", "Enabled");
            CheckLocalization.Checked = localOn;
            RadioLaunchDirect.IsChecked = !PslConfig.Current.LaunchViaSteam;
            RadioLaunchSteam.IsChecked = PslConfig.Current.LaunchViaSteam;
            RadioAdminOff.IsChecked = !PslConfig.Current.LaunchAsAdmin;
            RadioAdminOn.IsChecked = PslConfig.Current.LaunchAsAdmin;
        }
        finally
        {
            _syncingSwitches = false;
        }

        ApplySection();
        RefreshContent();
    }

    /// <summary>
    /// 左栏点击切换分类。PCL 的 <c>MyListItem.CheckType.RadioBox</c> 在同一个
    /// <c>StackPanel</c> 内自动互斥，PSL 的 <c>PclListItem</c> 不做分组管理，故这里手动维护。
    /// </summary>
    private void Section_Click(object? sender, EventArgs e)
    {
        if (sender is not PclListItem item) return;

        _section = item.Name switch
        {
            "ItemSetup" => Section.Setup,
            "ItemMod" => Section.Mod,
            "ItemTexture" => Section.TexturePack,
            "ItemParts" => Section.PartsPack,
            "ItemSystem" => Section.SolarSystem,
            "ItemBlueprint" => Section.Blueprint,
            "ItemFont" => Section.Font,
            "ItemLanguage" => Section.Language,
            _ => Section.Overview
        };

        ApplySection();
        RefreshContent();
    }

    /// <summary>同步左栏勾选态与右栏各卡片的显示。</summary>
    private void ApplySection()
    {
        ItemOverall.Checked = _section == Section.Overview;
        ItemSetup.Checked = _section == Section.Setup;
        ItemMod.Checked = _section == Section.Mod;
        ItemTexture.Checked = _section == Section.TexturePack;
        ItemParts.Checked = _section == Section.PartsPack;
        ItemSystem.Checked = _section == Section.SolarSystem;
        ItemBlueprint.Checked = _section == Section.Blueprint;
        ItemFont.Checked = _section == Section.Font;
        ItemLanguage.Checked = _section == Section.Language;

        // 「汉化版功能」关闭时隐藏字体 / 语言包两项
        bool local = GameLibrary.Current is not null &&
                     VersionSetup.ReadBool(GameLibrary.Current, "Localization", "Enabled");
        ItemFont.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        ItemLanguage.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        if (!local && _section is Section.Font or Section.Language) _section = Section.Overview;

        bool overview = _section == Section.Overview;
        bool setup = _section == Section.Setup;
        bool resource = _section is Section.Mod or Section.TexturePack or Section.PartsPack
            or Section.SolarSystem or Section.Blueprint;

        CardHeader.Visibility = overview ? Visibility.Visible : Visibility.Collapsed;
        CardFolder.Visibility = overview ? Visibility.Visible : Visibility.Collapsed;
        CardManage.Visibility = overview ? Visibility.Visible : Visibility.Collapsed;
        CardSetup.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
        CardResource.Visibility = resource ? Visibility.Visible : Visibility.Collapsed;
        CardFont.Visibility = _section == Section.Font ? Visibility.Visible : Visibility.Collapsed;
        CardLanguage.Visibility = _section == Section.Language ? Visibility.Visible : Visibility.Collapsed;

        if (overview || setup) ShowEmpty(null);
        else if (resource) ShowEmpty(KindName(CurrentKind));
    }

    private SfsResourceKind CurrentKind => _section switch
    {
        Section.TexturePack => SfsResourceKind.TexturePack,
        Section.PartsPack => SfsResourceKind.PartsPack,
        Section.SolarSystem => SfsResourceKind.SolarSystem,
        Section.Blueprint => SfsResourceKind.Blueprint,
        _ => SfsResourceKind.Mod
    };

    private static string KindName(SfsResourceKind kind) => kind switch
    {
        SfsResourceKind.Mod => "DLL 模组",
        SfsResourceKind.TexturePack => "涂装包",
        SfsResourceKind.PartsPack => "零件包",
        SfsResourceKind.SolarSystem => "星系包",
        SfsResourceKind.Blueprint => "蓝图",
        _ => "资源"
    };

    private void RefreshContent()
    {
        switch (_section)
        {
            case Section.Mod:
            case Section.TexturePack:
            case Section.PartsPack:
            case Section.SolarSystem:
            case Section.Blueprint:
                RefreshResourceList();
                break;
            case Section.Font:
                RefreshFontList();
                break;
            case Section.Language:
                RefreshLanguageList();
                break;
        }
    }

    // ==================== 资源列表（PclLocalItem） ====================

    private void RefreshResourceList()
    {
        PanList.Children.Clear();

        GameVersion? version = GameLibrary.Current;
        if (version is null)
        {
            ShowEmpty("尚未添加版本", "请先在主页点「版本选择」把 SFS 游戏文件夹加入游戏目录。");
            CardResource.Title = KindName(CurrentKind);
            return;
        }

        SfsResourceKind kind = CurrentKind;
        CardResource.Title = KindName(kind);

        List<ResourceItem> items = ResourceManager.List(version, kind);
        if (!string.IsNullOrWhiteSpace(_filter))
        {
            items = items.Where(i =>
                i.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                i.Extra.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (items.Count == 0)
        {
            ShowEmpty($"尚未安装{KindName(kind)}",
                $"当前版本的「{KindName(kind)}」目录里还没有内容。可以到「下载」页安装，或点「安装本地文件」。");
            return;
        }

        PanEmpty.Visibility = Visibility.Collapsed;

        foreach (ResourceItem item in items)
        {
            ResourceItem i = item;

            var row = new PclLocalItem
            {
                Logo = TryFindResource(KindIcon(kind)) as Geometry,
                Title = i.Name,
                Subtitle = i.Extra,
                // 照 PCL 的 LabInfo：短信息（大小 · 时间），完整路径放 ToolTip
                Info = $"{i.SizeText} · {i.Modified:yyyy-MM-dd HH:mm}",
                ToolTip = $"{i.Name}\n{i.Path}"
            };

            row.Status = i.Enabled ? "已启用" : "已禁用";
            row.StatusForeground = i.Enabled ? BrushOkFg : BrushOffFg;
            row.StatusBackground = i.Enabled ? BrushOkBg : BrushOffBg;

            var toggle = new PclIconButton
            {
                Logo = TryFindResource(i.Enabled ? "IconDisable" : "IconEnable") as Geometry,
                LogoScale = 1.0,
                ToolTip = i.Enabled ? "禁用" : "启用"
            };
            toggle.Click += (_, _) => ToggleResource(i);
            row.AddButton(toggle);

            var open = new PclIconButton
            {
                Logo = TryFindResource("IconGameBox") as Geometry,
                LogoScale = 1.0,
                ToolTip = "打开位置"
            };
            open.Click += (_, _) => OpenResourceLocation(i);
            row.AddButton(open);

            var remove = new PclIconButton
            {
                Logo = TryFindResource("IconDelete") as Geometry,
                LogoScale = 1.0,
                ToolTip = "卸载"
            };
            remove.Click += (_, _) => UninstallResource(i);
            row.AddButton(remove);

            row.Click += (_, _) => OpenResourceLocation(i);
            PanList.Children.Add(row);
        }
    }

    private static string KindIcon(SfsResourceKind kind) => kind switch
    {
        SfsResourceKind.Mod => "IconMod",
        SfsResourceKind.TexturePack => "IconTexturePack",
        SfsResourceKind.PartsPack => "IconPartsPack",
        SfsResourceKind.SolarSystem => "IconSystemPack",
        SfsResourceKind.Blueprint => "IconBlueprint",
        _ => "IconGameBox"
    };

    // ==================== 字体 / 语言包 ====================

    private void RefreshFontList()
    {
        PanFontList.Children.Clear();

        GameVersion? version = GameLibrary.Current;
        if (version is null)
        {
            ShowEmpty("尚未添加版本", "请先添加并选择一个版本。");
            return;
        }

        List<FontEntry> fonts = TranslationManager.ListFonts(version);
        if (fonts.Count == 0)
        {
            ShowEmpty("没有找到字体",
                $"未在以下目录找到字体文件：\n{TranslationManager.FontDir(version)}\n\n" +
                "汉化版的字体放在 版本目录\\Spaceflight Simulator_Data\\Custom Fonts\\ 下。");
            return;
        }

        PanEmpty.Visibility = Visibility.Collapsed;
        string? current = TranslationManager.CurrentFontName(version);

        foreach (FontEntry font in fonts)
        {
            FontEntry f = font;

            var row = new PclLocalItem
            {
                Logo = TryFindResource("IconTexturePack") as Geometry,
                Title = f.Name,
                Subtitle = "字体",
                Info = $"{f.SizeText} · {f.Path}",
                ToolTip = f.Path,
                Checked = string.Equals(f.Name, current, StringComparison.OrdinalIgnoreCase)
            };

            var apply = new PclIconButton
            {
                Logo = TryFindResource("IconEnable") as Geometry,
                LogoScale = 1.0,
                ToolTip = "应用这个字体"
            };
            apply.Click += (_, _) => ApplyFont(f);
            row.AddButton(apply);

            PanFontList.Children.Add(row);
        }
    }

    private void RefreshLanguageList()
    {
        PanLanguageList.Children.Clear();

        GameVersion? version = GameLibrary.Current;
        if (version is null)
        {
            ShowEmpty("尚未添加版本", "请先添加并选择一个版本。");
            return;
        }

        List<LanguageEntry> languages = TranslationManager.ListLanguages(version);
        if (languages.Count == 0)
        {
            ShowEmpty("没有找到语言包",
                $"未在以下目录找到语言包（.txt）：\n{TranslationManager.LanguageDir(version)}\n\n" +
                "汉化版的语言包放在 版本目录\\Spaceflight Simulator_Data\\Custom Translations\\ 下。");
            return;
        }

        PanEmpty.Visibility = Visibility.Collapsed;
        string? current = TranslationManager.CurrentLanguageName(version);

        foreach (LanguageEntry language in languages)
        {
            LanguageEntry l = language;

            var row = new PclLocalItem
            {
                Logo = TryFindResource("IconAbout") as Geometry,
                Title = l.Name,
                Subtitle = "语言包",
                Info = $"{l.Count} 条译文 · {l.Path}",
                ToolTip = l.Path,
                Checked = string.Equals(l.Name, current, StringComparison.OrdinalIgnoreCase)
            };
            var apply = new PclIconButton
            {
                Logo = TryFindResource("IconEnable") as Geometry,
                LogoScale = 1.0,
                ToolTip = "应用这个语言包"
            };
            apply.Click += (_, _) => ApplyLanguage(l);
            row.AddButton(apply);

            PanLanguageList.Children.Add(row);
        }
    }

    private void ApplyFont(FontEntry font)
    {
        GameVersion? version = GameLibrary.Current;
        if (version is null) return;

        if (!TranslationManager.HasTranslator(version))
        {
            TranslationManager.EnsureConfig(version);
            MainWindow.Toast("该版本原本没有翻译器，已按默认模板新建 AutoTranslator\\Config.ini");
        }

        try
        {
            TranslationManager.ApplyFont(version, font);
            RefreshFontList();
            MainWindow.Toast($"已为「{version.DisplayName}」应用字体：{font.Name}");
        }
        catch (Exception ex)
        {
            Log.Error("应用字体失败", ex);
            MessageBox.Show(Window.GetWindow(this), $"应用字体失败：{ex.Message}",
                "PlainSpaceflightLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyLanguage(LanguageEntry language)
    {
        GameVersion? version = GameLibrary.Current;
        if (version is null) return;

        if (!TranslationManager.HasTranslator(version))
        {
            TranslationManager.EnsureConfig(version);
            MainWindow.Toast("该版本原本没有翻译器，已按默认模板新建 AutoTranslator\\Config.ini");
        }

        try
        {
            TranslationManager.ApplyLanguage(version, language);
            RefreshLanguageList();
            MainWindow.Toast($"已为「{version.DisplayName}」应用语言包：{language.Name}");
        }
        catch (Exception ex)
        {
            Log.Error("应用语言包失败", ex);
            MessageBox.Show(Window.GetWindow(this), $"应用语言包失败：{ex.Message}",
                "PlainSpaceflightLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ==================== 空态 ====================

    /// <summary>显示 / 隐藏居中空态卡。<paramref name="kindName"/> 为空表示不显示。</summary>
    private void ShowEmpty(string? kindName, string? message = null)
    {
        bool show = kindName is not null;

        PanEmpty.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        TxtTitleEmpty.Text = kindName switch
        {
            "尚未添加版本" => "还没有添加版本",
            "没有找到字体" => "没有找到字体",
            "没有找到语言包" => "没有找到语言包",
            _ => $"尚未安装{kindName}"
        };
        TxtEmpty.Text = message ?? "";
    }

    // ==================== 设置页开关 ====================

    private void CheckLocalization_Changed(object? sender, bool on)
    {
        if (_syncingSwitches) return;

        GameVersion? version = GameLibrary.Current;
        if (version is null)
        {
            MainWindow.Toast("请先选择一个版本。");
            return;
        }

        // 汉化版功能是**按版本**存的（版本目录\PSL\Setup.ini）
        VersionSetup.Write(version, "Localization", "Enabled", on ? "true" : "false");
        ApplySection();
        RefreshContent();
        MainWindow.Toast($"已{(on ? "开启" : "关闭")}「{version.DisplayName}」的汉化版功能");
    }

    private void RadioLaunch_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingSwitches) return;
        PslConfig.Current.LaunchViaSteam = ReferenceEquals(sender, RadioLaunchSteam);
        PslConfig.Current.Save();
    }

    private void RadioAdmin_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingSwitches) return;
        PslConfig.Current.LaunchAsAdmin = ReferenceEquals(sender, RadioAdminOn);
        PslConfig.Current.Save();
    }

    // ==================== 搜索 ====================

    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        _filter = SearchBox.Text ?? "";
        RefreshResourceList();
    }

    // ==================== 文件夹 / 版本操作 ====================

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        GameLibrary.Refresh();
        Reload();
        MainWindow.Toast("已刷新");
    }

    private void BtnRefreshList_Click(object sender, RoutedEventArgs e)
    {
        RefreshContent();
        MainWindow.Toast("已刷新列表");
    }

    private void BtnOpenVersion_Click(object sender, RoutedEventArgs e)
    {
        if (GameLibrary.Current is { } v) VersionManager.OpenInExplorer(v.FolderPath);
    }

    private void BtnOpenTranslator_Click(object sender, RoutedEventArgs e)
    {
        GameVersion? v = GameLibrary.Current;
        if (v is null) return;

        if (!TranslationManager.HasTranslator(v))
        {
            MainWindow.Toast("这个版本没有 AutoTranslator 文件夹（未安装翻译器）");
            return;
        }

        VersionManager.OpenInExplorer(TranslationManager.TranslatorDir(v));
    }

    private void BtnOpenMods_Click(object sender, RoutedEventArgs e)
    {
        if (GameLibrary.Current is { } v) VersionManager.OpenInExplorer(v.ModsDir);
    }

    private void BtnOpenTexture_Click(object sender, RoutedEventArgs e)
    {
        if (GameLibrary.Current is { } v) VersionManager.OpenInExplorer(v.TexturePacksDir);
    }

    private void BtnOpenBlueprint_Click(object sender, RoutedEventArgs e)
    {
        if (GameLibrary.Current is { } v) VersionManager.OpenInExplorer(v.BlueprintsDir);
    }

    private void BtnOpenSaving_Click(object sender, RoutedEventArgs e)
    {
        if (GameLibrary.Current is { } v) VersionManager.OpenInExplorer(v.SavingDir);
    }

    private void BtnRemoveVersion_Click(object sender, RoutedEventArgs e)
    {
        GameVersion? version = GameLibrary.Current;
        if (version is null)
        {
            MainWindow.Toast("当前没有选中的版本。");
            return;
        }

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
            GameLibrary.RemoveVersion(version);
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

    private void BtnOpenResourceDir_Click(object sender, RoutedEventArgs e)
    {
        GameVersion? v = GameLibrary.Current;
        if (v is null) return;

        string dir = _section switch
        {
            Section.Font => TranslationManager.FontDir(v),
            Section.Language => TranslationManager.LanguageDir(v),
            _ => CurrentKind switch
            {
                SfsResourceKind.Mod => v.ModsDir,
                SfsResourceKind.TexturePack => v.TexturePacksDir,
                SfsResourceKind.PartsPack => v.PartsDir,
                SfsResourceKind.SolarSystem => v.SolarSystemsDir,
                SfsResourceKind.Blueprint => v.BlueprintsDir,
                _ => v.FolderPath
            }
        };

        VersionManager.OpenInExplorer(dir);
    }

    private void BtnEmptyOpenDir_Click(object sender, RoutedEventArgs e) => BtnOpenResourceDir_Click(sender, e);

    // ==================== 资源操作 ====================

    private async void BtnInstallLocal_Click(object sender, RoutedEventArgs e)
    {
        GameVersion? version = GameLibrary.Current;
        if (version is null)
        {
            MainWindow.Toast("请先添加并选择一个版本。");
            return;
        }

        SfsResourceKind kind = CurrentKind;
        var dialog = new OpenFileDialog
        {
            Title = $"选择要安装的{KindName(kind)}文件",
            Filter = kind switch
            {
                SfsResourceKind.Mod => "模组文件 (*.dll;*.zip)|*.dll;*.zip|所有文件 (*.*)|*.*",
                SfsResourceKind.PartsPack => "零件包 (*.pack;*.zip)|*.pack;*.zip|所有文件 (*.*)|*.*",
                _ => "压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*"
            },
            Multiselect = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        int ok = 0, fail = 0;
        string lastMessage = "";

        await Task.Run(() =>
        {
            foreach (string file in dialog.FileNames)
            {
                InstallResult result = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? ResourceManager.InstallDll(file, version)
                    : file.EndsWith(".pack", StringComparison.OrdinalIgnoreCase)
                        ? ResourceManager.InstallPack(file, version)
                        : ResourceManager.InstallArchive(file, kind, version);

                if (result.Success) ok++;
                else fail++;

                lastMessage = result.Message;
            }
        });

        RefreshResourceList();
        MainWindow.Toast(ok > 0
            ? $"已安装 {ok} 项{(fail > 0 ? $"，失败 {fail} 项" : "")}：{lastMessage}"
            : $"安装失败：{lastMessage}");
    }

    private void ToggleResource(ResourceItem item)
    {
        if (ResourceManager.SetEnabled(item, !item.Enabled))
        {
            MainWindow.Toast(item.Enabled ? $"已禁用「{item.Name}」" : $"已启用「{item.Name}」");
            RefreshResourceList();
        }
        else
        {
            MainWindow.Toast("操作失败，可能文件被占用。");
        }
    }

    private void OpenResourceLocation(ResourceItem item)
    {
        string path = item.Path;
        if (Directory.Exists(path)) VersionManager.OpenInExplorer(path);
        else if (Path.GetDirectoryName(path) is { } dir) VersionManager.OpenInExplorer(dir);
    }

    private void UninstallResource(ResourceItem item)
    {
        MessageBoxResult confirm = MessageBox.Show(
            Window.GetWindow(this),
            $"确定要卸载「{item.Name}」吗？\n\n{item.Path}\n\n该项目会被移动到系统回收站。",
            "卸载资源",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (confirm != MessageBoxResult.OK) return;

        if (ResourceManager.Uninstall(item))
        {
            MainWindow.Toast($"已卸载「{item.Name}」（可在回收站恢复）");
            RefreshResourceList();
        }
        else
        {
            MainWindow.Toast("卸载失败，详见日志。");
        }
    }
}
