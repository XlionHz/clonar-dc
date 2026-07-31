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
        var displayVersion = version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{version.Build}";
        var localDeveloper = string.Equals(
            Environment.GetEnvironmentVariable("GUILDSYNC_API"),
            "http://127.0.0.1:8787",
            StringComparison.OrdinalIgnoreCase);
        VersionText.Text = localDeveloper
            ? $"v{displayVersion} recovery  •  LOCAL PREVIEW"
            : $"v{displayVersion} recovery";

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
        InitializeCompatibilityUi();
        InitializeUpdateUi();
        LocalizationService.Apply(this);
    }
}