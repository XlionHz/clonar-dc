using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        FixLicenseSelectorContrast();
        InitializeUpdateUi();
        LocalizationService.Apply(this);
        Pages.SelectedIndex = 0;
    }

    private void FixLicenseSelectorContrast()
    {
        var fieldBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        var fieldForeground = new SolidColorBrush(Color.FromRgb(17, 24, 39));
        var accent = new SolidColorBrush(Color.FromRgb(139, 92, 246));

        LicenseBox.Background = fieldBackground;
        LicenseBox.Foreground = fieldForeground;
        LicenseBox.BorderBrush = accent;
        LicenseBox.BorderThickness = new Thickness(1);
        LicenseBox.Padding = new Thickness(10, 7, 10, 7);
        LicenseBox.MinHeight = 42;

        foreach (var item in LicenseBox.Items.OfType<ComboBoxItem>())
        {
            item.Background = fieldBackground;
            item.Foreground = fieldForeground;
            item.Padding = new Thickness(10, 8, 10, 8);
            item.HorizontalContentAlignment = HorizontalAlignment.Left;
        }
    }
}