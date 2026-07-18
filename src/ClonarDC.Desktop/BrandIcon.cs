using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClonarDC;

internal static class BrandIcon
{
    public static ImageSource Create()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/ClonarDC;component/Assets/ClonarDCLogo.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
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