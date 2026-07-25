using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ClonarDC;

internal static class BrandPresentation
{
    private static readonly Uri LogoUri = new(
        "pack://application:,,,/ClonarDC;component/Assets/GuildSyncLogo.png",
        UriKind.Absolute);

    public static FrameworkElement CreateMark(double size, bool glow = true)
    {
        var grid = new Grid
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (glow)
        {
            grid.Children.Add(new Border
            {
                Margin = new Thickness(size * 0.18),
                CornerRadius = new CornerRadius(size),
                Background = new SolidColorBrush(Color.FromArgb(66, 94, 95, 255)),
                Effect = new BlurEffect { Radius = size * 0.16 }
            });
        }

        var logo = new Border
        {
            Width = size,
            Height = size,
            Background = CreateCroppedLogoBrush()
        };
        grid.Children.Add(logo);
        return grid;
    }

    public static Border CreateSidebarHeader()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mark = CreateMark(54, glow: false);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        layout.Children.Add(mark);

        var words = new StackPanel
        {
            Margin = new Thickness(10, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        words.Children.Add(new TextBlock
        {
            Text = "GuildSync",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 249, 255))
        });
        words.Children.Add(new TextBlock
        {
            Text = "SYNC  •  CLONE  •  SCALE",
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(147, 151, 255)),
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(words, 1);
        layout.Children.Add(words);

        return new Border
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(23, 24, 42),
                Color.FromRgb(16, 17, 29),
                0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 50, 76)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 11, 12, 11),
            Margin = new Thickness(0, 2, 0, 22),
            Child = layout
        };
    }

    private static ImageBrush CreateCroppedLogoBrush()
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = LogoUri;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(0.28, 0.28, 0.44, 0.44)
        };
        brush.Freeze();
        return brush;
    }
}
