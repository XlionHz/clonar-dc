using System.Windows;
using System.Windows.Media;

namespace ClonarDC;

internal static class BrandIcon
{
    private static readonly Geometry Shield = Geometry.Parse(
        "M 32,2 L 58,14 L 58,37 C 58,54 48,67 32,74 C 16,67 6,54 6,37 L 6,14 Z");

    private static readonly Geometry InnerShield = Geometry.Parse(
        "M 32,13 L 48,20 L 48,37 C 48,47 42,55 32,61 C 22,55 16,47 16,37 L 16,20 Z");

    private static readonly Geometry Letter = Geometry.Parse(
        "M 45,28 C 40,22 34,20 28,22 C 20,24 16,31 17,39 C 18,47 25,52 33,52 C 39,52 43,49 47,45 L 47,38 L 35,38");

    public static ImageSource Create()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new LinearGradientBrush(Color.FromRgb(163, 92, 255), Color.FromRgb(64, 105, 255), 35),
            new Pen(new SolidColorBrush(Color.FromRgb(196, 178, 255)), 0.7),
            Shield));
        group.Children.Add(new GeometryDrawing(
            new LinearGradientBrush(Color.FromRgb(14, 18, 43), Color.FromRgb(8, 11, 27), 90),
            new Pen(new SolidColorBrush(Color.FromRgb(104, 99, 187)), 0.6),
            InnerShield));
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(
                new LinearGradientBrush(Color.FromRgb(188, 107, 255), Color.FromRgb(74, 105, 255), 25),
                6)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            },
            Letter));

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}

public partial class LoginWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Icon = BrandIcon.Create();
    }
}

public partial class RegisterWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Icon = BrandIcon.Create();
    }
}

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Icon = BrandIcon.Create();
    }
}

public partial class PublishUpdateWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Icon = BrandIcon.Create();
    }
}

public partial class TextConfirmWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Icon = BrandIcon.Create();
    }
}
