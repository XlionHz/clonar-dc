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

        if (Content is Grid root &&
            root.Children.OfType<Border>().FirstOrDefault(item => Grid.GetColumn(item) == 0) is Border sidebar &&
            sidebar.Child is Grid sidebarGrid)
        {
            var currentHeader = sidebarGrid.Children
                .OfType<UIElement>()
                .FirstOrDefault(item => Grid.GetRow(item) == 0);
            if (currentHeader is not null) sidebarGrid.Children.Remove(currentHeader);

            var brandedHeader = BrandPresentation.CreateSidebarHeader();
            Grid.SetRow(brandedHeader, 0);
            sidebarGrid.Children.Add(brandedHeader);
        }

        InitializeUpdateUi();
        LocalizationService.Apply(this);
    }
}
