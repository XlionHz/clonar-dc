using System.Windows;

namespace ClonarDC;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var login = new LoginWindow();
        var result = login.ShowDialog();
        if (result == true)
        {
            var main = new MainWindow(login.Session!);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        else
        {
            Shutdown();
        }
    }
}
