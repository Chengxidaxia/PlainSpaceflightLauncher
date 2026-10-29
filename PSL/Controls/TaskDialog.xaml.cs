using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PSL.Core;

namespace PSL.Controls;

/// <summary>通用的「正在处理」进度对话框。</summary>
public partial class TaskDialog : Window
{
    private CancellationTokenSource? _cts;

    private TaskDialog()
    {
        InitializeComponent();
    }

    /// <summary>进度上报器：<c>report(状态文字, 百分比 0-100)</c>；百分比小于 0 表示不确定进度。</summary>
    public sealed class Reporter : IProgress<(string Status, double Percent)>
    {
        private readonly TaskDialog _dialog;

        internal Reporter(TaskDialog dialog) => _dialog = dialog;

        public void Report((string Status, double Percent) value) => _dialog.UpdateUi(value.Status, value.Percent);
    }

    /// <summary>
    /// 以模态方式执行一个耗时任务。
    /// </summary>
    /// <returns>任务成功完成返回 true；取消或异常返回 false。</returns>
    public static bool Run(
        Window? owner,
        string title,
        string message,
        Func<Reporter, CancellationToken, Task> work)
    {
        var dialog = new TaskDialog
        {
            Owner = owner,
            TxtTitle = { Text = title },
            TxtMessage = { Text = message }
        };

        dialog._cts = new CancellationTokenSource();
        var reporter = new Reporter(dialog);

        bool ok = false;
        dialog.Loaded += async (_, _) =>
        {
            try
            {
                await work(reporter, dialog._cts.Token);
                ok = true;
            }
            catch (OperationCanceledException)
            {
                Log.Warn("用户取消了操作");
                reporter.Report(("已取消", 0));
            }
            catch (Exception ex)
            {
                Log.Error("任务执行失败", ex);
                reporter.Report(("失败：" + ex.Message, 0));
                MessageBox.Show(dialog, $"操作失败：{ex.Message}", "PlainSpaceflightLauncher",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                dialog.Close();
            }
        };

        dialog.ShowDialog();
        dialog._cts?.Dispose();
        return ok;
    }

    internal void UpdateUi(string status, double percent)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            TxtStatus.Text = status;

            if (percent < 0)
            {
                Bar.IsIndeterminate = true;
                TxtPercent.Text = "";
            }
            else
            {
                Bar.IsIndeterminate = false;
                Bar.Value = Net48Math.Clamp(percent, 0, 100);
                TxtPercent.Text = $"{percent:F1}%";
            }
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        BtnCancel.IsEnabled = false;
        TxtStatus.Text = "正在取消…";
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // 忽略
        }
    }
}
