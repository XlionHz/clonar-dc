using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        SourceGuildBox.LostKeyboardFocus -= EditableServerBox_LostKeyboardFocus;
        TargetGuildBox.LostKeyboardFocus -= EditableServerBox_LostKeyboardFocus;
        SourceGuildBox.PreviewKeyDown -= EditableServerBox_PreviewKeyDown;
        TargetGuildBox.PreviewKeyDown -= EditableServerBox_PreviewKeyDown;

        SourceGuildBox.LostKeyboardFocus += EditableServerBox_LostKeyboardFocus;
        TargetGuildBox.LostKeyboardFocus += EditableServerBox_LostKeyboardFocus;
        SourceGuildBox.PreviewKeyDown += EditableServerBox_PreviewKeyDown;
        TargetGuildBox.PreviewKeyDown += EditableServerBox_PreviewKeyDown;

        SidebarBrandHost.Content ??= BrandPresentation.CreateSidebarHeader();
        Pages.SelectedIndex = Pages.SelectedIndex < 0 ? 0 : Pages.SelectedIndex;
    }

    private void EditableServerBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ComboBox box)
            MaterializeTypedServer(box);
    }

    private void EditableServerBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox box) return;
        if (e.Key is Key.Enter or Key.Tab)
            MaterializeTypedServer(box);
    }

    private static GuildSummary? MaterializeTypedServer(ComboBox box)
    {
        if (box.SelectedItem is GuildSummary selected)
            return selected;

        var typed = (box.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        var items = box.ItemsSource is IEnumerable<GuildSummary> existing
            ? existing.ToList()
            : [];

        var match = items.FirstOrDefault(item =>
            string.Equals(item.Id, typed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, typed, StringComparison.CurrentCultureIgnoreCase));

        if (match is null)
        {
            match = new GuildSummary(typed, typed, null);
            items.Add(match);
            box.ItemsSource = items;
        }

        box.SelectedItem = match;
        box.Text = match.Name;
        return match;
    }

    private static GuildSummary RequireEditableGuild(ComboBox box, string label)
    {
        var selected = MaterializeTypedServer(box);
        if (selected is not null) return selected;

        throw new InvalidOperationException($"Enter or select the {label} ID.");
    }
}
