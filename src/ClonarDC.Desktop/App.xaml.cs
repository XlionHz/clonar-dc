using System.Windows;
using ClonarDC.Services;

namespace ClonarDC;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            while (true)
            {
                var login = new LoginWindow();
                if (login.ShowDialog() != true || login.Session is null) break;

                var main = new MainWindow(login.Session);
                MainWindow = main;
                main.ShowDialog();

                if (!main.LogoutRequested) break;
            }
        }
        finally
        {
            LocalBackendManager.Shutdown();
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LocalBackendManager.Shutdown();
        base.OnExit(e);
    }
}