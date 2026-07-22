using System.Windows;

namespace ClonarDC;

public partial class MainWindow
{
    private void PublishUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAdmin)
        {
            MessageBox.Show("Administrator access is required.", "Clonar DC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new PublishUpdateWindow { Owner = this };
        window.ShowDialog();
    }
}