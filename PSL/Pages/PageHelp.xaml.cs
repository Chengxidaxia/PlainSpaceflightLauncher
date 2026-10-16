using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PSL.Controls;
using PSL.Core;

namespace PSL.Pages;

/// <summary>帮助页（WIP）。</summary>
public partial class PageHelp : PclPage, IPageRefresh
{
    public PageHelp()
    {
        InitializeComponent();
    }

    public void OnPageEnter()
    {
        // 帮助内容尚未完成
    }

    private void OpenSite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = ModSource.HomeUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("打开帮助链接失败", ex);
        }
    }
}
