using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PSL.Controls;

/// <summary>通用「从列表里选一项」对话框。</summary>
public partial class PickDialog : Window
{
    private PickDialog()
    {
        InitializeComponent();
    }

    /// <summary>列表项。</summary>
    public sealed record Item(string Key, string Title, string Sub);

    /// <summary>弹出选择框，返回选中项的 Key；取消返回 null。</summary>
    public static string? Show(
        Window? owner,
        string title,
        string message,
        IEnumerable<Item> items,
        string? selectedKey = null)
    {
        var dialog = new PickDialog { Owner = owner };
        dialog.TxtTitle.Text = title;
        dialog.TxtMessage.Text = message;

        List<Item> list = items.ToList();
        dialog.List.ItemsSource = list;

        Item? selected = list.FirstOrDefault(i => i.Key == selectedKey) ?? list.FirstOrDefault();
        if (selected is not null) dialog.List.SelectedItem = selected;

        if (dialog.ShowDialog() == true && dialog.List.SelectedItem is Item picked)
            return picked.Key;

        return null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is null)
        {
            DialogResult = false;
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is not null) DialogResult = true;
    }
}
