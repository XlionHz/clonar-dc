using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClonarDC;

internal static class BrandIcon
{
    public static ImageSource Create()
    {
        const int size = 64;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = new LinearGradientBrush(Color.FromRgb(77, 35, 150), Color.FromRgb(50, 105, 220), 45);
            dc.DrawRoundedRectangle(bg, new Pen(new SolidColorBrush(Color.FromRgb(139, 92, 246)), 2), new Rect(2, 2, 60, 60), 15, 15);
            var p1 = new StreamGeometry();
            using (var g = p1.Open())
            {
                g.BeginFigure(new Point(18, 21), true, true);
                g.LineTo(new Point(32, 13), true, false); g.LineTo(new Point(46, 21), true, false);
                g.LineTo(new Point(46, 37), true, false); g.LineTo(new Point(32, 45), true, false); g.LineTo(new Point(18, 37), true, false);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), new Pen(Brushes.White, 2.5), p1);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(225, 217, 255)), null, new Rect(23, 24, 18, 5), 2, 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(201, 190, 255)), null, new Rect(23, 33, 18, 5), 2, 2);
            dc.DrawEllipse(Brushes.White, null, new Point(44, 26.5), 1.8, 1.8);
            dc.DrawEllipse(Brushes.White, null, new Point(44, 35.5), 1.8, 1.8);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
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

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Icon = BrandIcon.Create();
    }
}
