using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        var localDeveloper = string.Equals(
            Environment.GetEnvironmentVariable("GUILDSYNC_API"),
            "http://127.0.0.1:8787",
            StringComparison.OrdinalIgnoreCase);
        VersionText.Text = localDeveloper
            ? $"v{version.Major}.{version.Minor}.{version.Build} alpha  •  LOCAL DEV"
            : $"v{version.Major}.{version.Minor}.{version.Build} alpha";

        Pages.Template = (ControlTemplate)XamlReader.Parse(
            """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             TargetType="{x:Type TabControl}"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Border Background="{TemplateBinding Background}">
                    <ContentPresenter ContentSource="SelectedContent" />
                </Border>
            </ControlTemplate>
            """);
        Pages.SelectedIndex = 0;

        SidebarBrandHost.Content = BrandPresentation.CreateSidebarHeader();
        InitializeEditableServerInputs();
        InitializeUpdateUi();
        LocalizationService.Apply(this);
    }
}
