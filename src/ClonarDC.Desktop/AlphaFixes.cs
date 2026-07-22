using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build} alpha {version.Revision}";

        foreach (var item in Pages.Items.OfType<TabItem>())
        {
            item.Visibility = Visibility.Visible;
            item.Height = 0;
            item.MinHeight = 0;
            item.Padding = new Thickness(0);
            item.Margin = new Thickness(0);
        }

        InitializeUpdateUi();
        LocalizationService.Apply(this);
        Pages.SelectedIndex = 0;
    }
}