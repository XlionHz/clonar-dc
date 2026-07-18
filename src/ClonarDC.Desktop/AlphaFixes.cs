using System.Windows;
using System.Windows.Controls;

namespace ClonarDC;

public partial class LoginWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Alpha builds must remain testable before the production backend is hosted.
        DevButton.Visibility = Visibility.Visible;
    }
}

public partial class MainWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
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
