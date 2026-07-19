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
        LocalizationService.Apply(this);
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(EmailBox.Text))
        {
            StatusText.Text = LocalizationService.T("Fill in the name and email.");
            return;
        }
        if (PasswordBox.Password.Length < 8)
        {
            StatusText.Text = LocalizationService.T("Use a password with at least 8 characters.");
            return;
        }
        if (PasswordBox.Password != ConfirmBox.Password)
        {
            StatusText.Text = LocalizationService.T("The passwords do not match.");
            return;
        }
        CreateButton.IsEnabled = false;
        try
        {
            await _auth.RegisterAsync(NameBox.Text.Trim(), EmailBox.Text.Trim(), PasswordBox.Password);
            RegisteredEmail = EmailBox.Text.Trim();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }
}