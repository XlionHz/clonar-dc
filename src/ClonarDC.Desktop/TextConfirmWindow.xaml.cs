using System.Windows;

namespace ClonarDC;

public partial class TextConfirmWindow : Window
{
    private readonly string _expected;
    public TextConfirmWindow(string expected)
    {
        _expected = expected;
        InitializeComponent();
        InstructionText.Text = $"Para confirmar esta operação destrutiva, digite exatamente o nome do servidor de destino:\n\n{expected}";
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(ConfirmBox.Text.Trim(), _expected, StringComparison.Ordinal))
        {
            MessageBox.Show("O nome digitado não corresponde ao servidor de destino.", "Confirmação", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
