using System.Windows;

namespace ClonarDC;

public partial class MainWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Title = "GuildSync";
        VersionText.Text = "v0.7.0 alpha";
    }
}