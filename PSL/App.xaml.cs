using System.Windows;
using System.Windows.Threading;
using PSL.Core;

namespace PSL;

/// <summary>应用入口。</summary>
public partial class App : Application
{
    /// <summary>启动参数 <c>--page=home|download|version|settings|help</c> 指定的初始页面。</summary>
    public static string? StartupPage { get; private set; }

    /// <summary>
    /// 调试用：启动参数 <c>--nav=home,version,download</c> 会在启动后按顺序自动切页（每 900ms 一次），
    /// 便于截图验证页面切换动画（含切换瞬间是否有闪帧）。
    /// </summary>
    public static IReadOnlyList<string> NavSequence { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 调试用：启动参数 <c>--tab=game|mod|texture|parts|blueprint</c> 指定下载页左栏初始选中的分类，
    /// 便于在无人工点击的环境下截图验证左栏分类切换是否生效。
    /// </summary>
    public static string? StartupDownloadTab { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        Paths.EnsureCreated();
        Log.Info("================ PSL 启动 ================");
        Log.Info($"版本 {typeof(App).Assembly.GetName().Version}，程序目录 {Paths.AppDir}");

        string? pageArg = e.Args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        StartupPage = pageArg is null ? null : pageArg[7..];

        string? navArg = e.Args.FirstOrDefault(a => a.StartsWith("--nav=", StringComparison.OrdinalIgnoreCase));
        if (navArg is not null)
        {
            NavSequence = navArg[6..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        string? tabArg = e.Args.FirstOrDefault(a => a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase));
        StartupDownloadTab = tabArg is null ? null : tabArg[6..];

        if (!string.IsNullOrWhiteSpace(StartupPage))
            Log.Info($"启动页参数：{StartupPage}");
        if (NavSequence.Count > 0)
            Log.Info($"切页序列参数：{string.Join(" → ", NavSequence)}");
        if (!string.IsNullOrWhiteSpace(StartupDownloadTab))
            Log.Info($"下载页分类参数：{StartupDownloadTab}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("未处理的异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("================ PSL 退出 ================");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("界面线程异常", e.Exception);
        MessageBox.Show(
            $"发生了一个未处理的错误：\n\n{e.Exception.Message}\n\n详细信息已写入日志：\n{Log.CurrentFile}",
            "PlainSpaceflightLauncher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
