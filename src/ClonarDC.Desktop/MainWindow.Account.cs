using System.Windows;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    public bool LogoutRequested { get; private set; }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            LocalizationService.T("Do you want to sign out and return to the login screen?"),
            LocalizationService.T("Sign out of account"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;
        LogoutRequested = true;
        Close();
    }
}