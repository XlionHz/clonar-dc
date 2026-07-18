using System.Windows;
using System.Windows.Controls;

namespace ClonarDC;

public partial class MainWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        VersionText.Text = "v0.4.0 alpha";

        // Hide only the visual tab headers while preserving the selected content presenter.
        foreach (var item in Pages.Items.OfType<TabItem>())
        {
            item.Visibility = Visibility.Visible;
            item.Height = 0;
            item.MinHeight = 0;
            item.Padding = new Thickness(0);
            item.Margin = new Thickness(0);
        }
        Pages.SelectedIndex = 0;
    }
}