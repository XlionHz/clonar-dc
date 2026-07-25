using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace ClonarDC;

internal static class BrandPresentation
{
    private static readonly Geometry ShieldGeometry = Geometry.Parse(
        "M 90,4 L 164,38 L 164,104 C 164,154 134,190 90,208 C 46,190 16,154 16,104 L 16,38 Z");

    private static readonly Geometry ShieldInsetGeometry = Geometry.Parse(
        "M 90,35 L 135,56 L 135,104 C 135,133 118,154 90,169 C 62,154 45,133 45,104 L 45,58 Z");

    private static readonly Geometry LetterGeometry = Geometry.Parse(
        "M 126,77 C 114,61 96,55 78,59 C 56,64 44,84 46,106 C 48,130 67,146 90,147 C 106,147 120,140 130,128 L 130,108 L 96,108");

    public static FrameworkElement CreateMark(double size, bool glow = true)
    {
        var viewbox = new Viewbox
        {
            Width = size,
            Height = size * 1.12,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var canvas = new Canvas { Width = 180, Height = 216 };

        if (glow)
        {
            canvas.Children.Add(new Ellipse
            {
                Width = 150,
                Height = 70,
                Fill = new SolidColorBrush(Color.FromArgb(82, 88, 74, 255)),
                Effect = new BlurEffect { Radius = 34 }
            });
            Canvas.SetLeft(canvas.Children[^1], 15);
            Canvas.SetTop(canvas.Children[^1], 144);
        }

        var outer = new Path
        {
            Data = ShieldGeometry,
            Fill = new LinearGradientBrush(
                Color.FromRgb(160, 92, 255),
                Color.FromRgb(62, 106, 255),
                35),
            Stroke = new SolidColorBrush(Color.FromArgb(180, 190, 170, 255)),
            StrokeThickness = 1.2,
            Effect = glow
                ? new DropShadowEffect
                {
                    Color = Color.FromRgb(83, 67, 255),
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.72
                }
                : null
        };
        canvas.Children.Add(outer);

        var inner = new Path
        {
            Data = ShieldInsetGeometry,
            Fill = new LinearGradientBrush(
                Color.FromRgb(13, 17, 42),
                Color.FromRgb(8, 11, 28),
                90),
            Stroke = new SolidColorBrush(Color.FromArgb(130, 120, 111, 255)),
            StrokeThickness = 1
        };
        canvas.Children.Add(inner);

        var letter = new Path
        {
            Data = LetterGeometry,
            Stroke = new LinearGradientBrush(
                Color.FromRgb(181, 103, 255),
                Color.FromRgb(74, 104, 255),
                25),
            StrokeThickness = 17,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        canvas.Children.Add(letter);

        var shine = new Path
        {
            Data = Geometry.Parse("M 30,49 L 90,20 L 150,49"),
            Stroke = new SolidColorBrush(Color.FromArgb(150, 230, 220, 255)),
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        canvas.Children.Add(shine);

        var floorLight = new Rectangle
        {
            Width = 118,
            Height = 2,
            RadiusX = 1,
            RadiusY = 1,
            Fill = new LinearGradientBrush(
                Colors.Transparent,
                Color.FromRgb(116, 81, 255),
                0),
            Opacity = glow ? 0.9 : 0
        };
        canvas.Children.Add(floorLight);
        Canvas.SetLeft(floorLight, 31);
        Canvas.SetTop(floorLight, 208);

        viewbox.Child = canvas;
        return viewbox;
    }

    public static Border CreateSidebarHeader()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mark = CreateMark(50, glow: false);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        mark.VerticalAlignment = VerticalAlignment.Center;
        layout.Children.Add(mark);

        var words = new StackPanel
        {
            Margin = new Thickness(11, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var wordmark = new TextBlock
        {
            Text = "GuildSync",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = new LinearGradientBrush(
                Color.FromRgb(248, 249, 255),
                Color.FromRgb(111, 104, 255),
                0)
        };
        words.Children.Add(wordmark);
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
}
