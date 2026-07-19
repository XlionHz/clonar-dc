using System.Windows;
using ClonarDC.Services;

namespace ClonarDC;

public partial class TextConfirmWindow : Window
{
    private readonly string _expected;

    public TextConfirmWindow(string expected)
    {
        _expected = expected;
        InitializeComponent();
        LocalizationService.Apply(this);
        InstructionText.Text = $"To confirm this destructive operation, enter the destination server name exactly:\n\n{expected}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(ConfirmBox.Text.Trim(), _expected, StringComparison.Ordinal))
        {
            MessageBox.Show("The entered name does not match the destination server.", "Confirmation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}