using System.Windows;

namespace ClonarDC;

public partial class MainWindow
{
    public bool LogoutRequested { get; private set; }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Deseja sair desta conta e voltar para a tela de login?",
            "Sair da conta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;
        LogoutRequested = true;
        Close();
    }
}