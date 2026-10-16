using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PSL.Controls;
using PSL.Core;

namespace PSL.Pages;

/// <summary>
/// 下载详情页（下载页点进一个资源后的副页面）。
/// 结构照 PCL <c>Pages/PageDownload/Resource/PageDownloadCompDetail.xaml</c>：
/// 顶部返回卡 + 简介 / 图片 / 获取 三张卡。
/// </summary>
public partial class PageDownloadDetail : PclPage, IPageRefresh
{
    /// <summary>当前展示的资源（由下载页进入时设置）。</summary>
    public static PageDownload.DownloadRow? Current { get; set; }

    public PageDownloadDetail()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public void OnPageEnter() => Reload();

    private PageDownload.DownloadRow? Row => Current;

    private void Reload()
    {
        PageDownload.DownloadRow? row = Row;
        if (row is null)
        {
            TxtName.Text = "没有选中资源";
            return;
        }

        TxtName.Text = row.Name;
        TxtSub.Text = row.SubTitle;
        TxtDesc.Text = string.IsNullOrWhiteSpace(row.Desc) ? "（这个资源没有填写简介）" : row.Desc;
        TxtSize.Text = string.IsNullOrWhiteSpace(row.Size) ? "未知" : row.Size;
        TxtDate.Text = row.Date;
        TxtDateInner.Text = row.Date;
        LabDate.Visibility = Visibility.Collapsed;

        TagList.ItemsSource = row.Tags;

        BtnAction.Content = row.ActionText;

        // 图片
        string? logo = row.ImageUrl;
        if (logo is not null)
        {
            SetImage(Img1, logo);
            CardImages.Visibility = Visibility.Visible;
        }
        else
        {
            CardImages.Visibility = Visibility.Collapsed;
        }

        // 顶部小图标
        if (logo is not null) SetImage(ImgLogo, logo);
    }

    private static async void SetImage(Image target, string? url)
    {
        if (url is null) return;
        BitmapImage? cached = WebImageCache.FromCache(url);
        if (cached is not null)
        {
            target.Source = cached;
            return;
        }

        BitmapImage? bmp = await WebImageCache.LoadAsync(url);
        if (bmp is not null) target.Source = bmp;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.PageBack();

    private void BtnOpenSite_Click(object sender, RoutedEventArgs e)
    {
        string? url = Row?.Mod?.Link ?? Row?.Blueprint?.Link ?? ModSource.HomeUrl;
        OpenUrl(url);
    }

    private void BtnAction_Click(object sender, RoutedEventArgs e)
    {
        PageDownload.DownloadRow? row = Row;
        if (row is null) return;

        PageDownload.DownloadResource(row, Window.GetWindow(this));
    }

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
}
