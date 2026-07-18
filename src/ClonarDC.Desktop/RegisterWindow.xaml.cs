using System.Windows;
using ClonarDC.Services;

namespace ClonarDC;

public partial class RegisterWindow : Window
{
    private readonly AuthClient _auth;
    public string RegisteredEmail { get; private set; } = "";

    public RegisterWindow(AuthClient auth)
    {
        _auth = auth;
        InitializeComponent();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(EmailBox.Text))
        {
            StatusText.Text = "Preencha nome e e-mail.";
            return;
        }
        if (PasswordBox.Password.Length < 8)
        {
            StatusText.Text = "Use uma senha com pelo menos 8 caracteres.";
            return;
        }
        if (PasswordBox.Password != ConfirmBox.Password)
        {
            StatusText.Text = "As senhas não coincidem.";
            return;
        }
        CreateButton.IsEnabled = false;
        try
        {
            StatusText.Text = await _auth.RegisterAsync(NameBox.Text.Trim(), EmailBox.Text.Trim(), PasswordBox.Password);
            RegisteredEmail = EmailBox.Text.Trim();
            DialogResult = true;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { CreateButton.IsEnabled = true; }
    }
}
