using System.Windows;

namespace PSL.Controls;

/// <summary>通用「输入一行文本」对话框（对应 PCL 的 <c>MyMsgBoxInput</c>）。</summary>
public partial class InputDialog : Window
{
    private InputDialog()
    {
        InitializeComponent();
    }

    /// <summary>弹出入框，返回用户输入的文本；取消返回 null。</summary>
    public static string? Show(Window? owner, string title, string message, string? initial = null)
    {
        var dialog = new InputDialog { Owner = owner };
        dialog.TxtTitle.Text = title;
        dialog.TxtMessage.Text = message;
        dialog.TxtValue.Text = initial ?? string.Empty;

        dialog.Loaded += (_, _) =>
        {
            dialog.TxtValue.Focus();
            dialog.TxtValue.SelectAll();
        };

        return dialog.ShowDialog() == true ? dialog.TxtValue.Text : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
