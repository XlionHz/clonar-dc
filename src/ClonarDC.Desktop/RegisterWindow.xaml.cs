using System.Windows;
using System.Windows.Media;
using ClonarDC.Services;

namespace ClonarDC;

public partial class RegisterWindow : Window
{
    private readonly AuthClient _auth;
    private bool _submitting;

    public string RegisteredEmail { get; private set; } = "";

    public RegisterWindow(AuthClient auth)
    {
        _auth = auth;
        InitializeComponent();
        LocalizationService.Apply(this);
        ApplyRegistrationCopy();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting) return;

        HideFeedback();
        var name = NameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            ShowError(LocalizationService.T("Fill in the name and email."));
            return;
        }

        if (!email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
        {
            ShowError(Copy("valid-email"));
            return;
        }

        if (password.Length < 8)
        {
            ShowError(LocalizationService.T("Use a password with at least 8 characters."));
            return;
        }

        if (password != ConfirmBox.Password)
        {
            ShowError(LocalizationService.T("The passwords do not match."));
            return;
        }

        _submitting = true;
        CreateButton.IsEnabled = false;
        CreateButton.Content = Copy("creating");
        BusyBar.Visibility = Visibility.Visible;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await _auth.RegisterAsync(name, email, password, timeout.Token);

            RegisteredEmail = email;
            FormPanel.Visibility = Visibility.Collapsed;
            SuccessTitleText.Text = Copy("success-title");
            SuccessBodyText.Text = Copy("success-body");
            SuccessPanel.Visibility = Visibility.Visible;

            await Task.Delay(1800);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            ShowError(Copy("timeout"));
        }
        catch (Exception ex)
        {
            ShowError(FriendlyRegistrationError(ex.Message));
        }
        finally
        {
            if (DialogResult != true)
            {
                _submitting = false;
                CreateButton.IsEnabled = true;
                CreateButton.Content = LocalizationService.T("Create account");
                BusyBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ApplyRegistrationCopy()
    {
        SuccessTitleText.Text = Copy("success-title");
        SuccessBodyText.Text = Copy("success-body");
    }

    private void HideFeedback()
    {
        FeedbackBox.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
    }

    private void ShowError(string message)
    {
        BusyBar.Visibility = Visibility.Collapsed;
        FeedbackBox.Visibility = Visibility.Visible;
        FeedbackBox.Background = new SolidColorBrush(Color.FromRgb(36, 21, 29));
        FeedbackBox.BorderBrush = new SolidColorBrush(Color.FromRgb(251, 113, 133));
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(253, 164, 175));
        StatusText.Text = message;
    }

    private static string FriendlyRegistrationError(string raw)
    {
        if (raw.Contains("Já existe uma conta", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return Copy("duplicate");
        }

        if (raw.Contains("serviço de contas", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("account service", StringComparison.OrdinalIgnoreCase))
        {
            return Copy("service");
        }

        return Copy("generic") + Environment.NewLine + raw;
    }

    private static string Copy(string key)
    {
        var language = LocalizationService.CurrentCode;
        return (language, key) switch
        {
            ("pt-BR", "valid-email") => "Informe um endereço de e-mail válido.",
            ("pt-BR", "creating") => "Criando conta…",
            ("pt-BR", "success-title") => "Conta GuildSync criada!",
            ("pt-BR", "success-body") => "Sua conta foi criada. Entre no aplicativo para escolher uma licença ou conecte o bot oficial do GuildSync ao seu Discord.",
            ("pt-BR", "timeout") => "A solicitação demorou mais que o esperado. Verifique sua conexão e tente novamente.",
            ("pt-BR", "duplicate") => "Já existe uma conta cadastrada com este e-mail.",
            ("pt-BR", "service") => "Não foi possível acessar o serviço de contas do GuildSync. Verifique sua conexão e tente novamente.",
            ("pt-BR", "generic") => "Não foi possível criar sua conta GuildSync. Confira os dados e tente novamente.",

            ("es-ES", "valid-email") => "Introduce una dirección de correo válida.",
            ("es-ES", "creating") => "Creando cuenta…",
            ("es-ES", "success-title") => "¡Cuenta de GuildSync creada!",
            ("es-ES", "success-body") => "Tu cuenta fue creada. Inicia sesión para elegir una licencia o conecta el bot oficial de GuildSync a Discord.",
            ("es-ES", "timeout") => "La solicitud tardó demasiado. Comprueba tu conexión e inténtalo de nuevo.",
            ("es-ES", "duplicate") => "Ya existe una cuenta registrada con este correo.",
            ("es-ES", "service") => "No se pudo acceder al servicio de cuentas de GuildSync. Comprueba tu conexión e inténtalo de nuevo.",
            ("es-ES", "generic") => "No pudimos crear tu cuenta de GuildSync. Comprueba los datos e inténtalo de nuevo.",

            ("fr-FR", "valid-email") => "Saisissez une adresse e-mail valide.",
            ("fr-FR", "creating") => "Création du compte…",
            ("fr-FR", "success-title") => "Compte GuildSync créé !",
            ("fr-FR", "success-body") => "Votre compte est créé. Connectez-vous pour choisir une licence ou reliez le bot officiel GuildSync à Discord.",
            ("fr-FR", "timeout") => "La demande a pris trop de temps. Vérifiez votre connexion et réessayez.",
            ("fr-FR", "duplicate") => "Un compte existe déjà avec cette adresse e-mail.",
            ("fr-FR", "service") => "Le service de comptes GuildSync est inaccessible. Vérifiez votre connexion et réessayez.",
            ("fr-FR", "generic") => "Impossible de créer votre compte GuildSync. Vérifiez les informations et réessayez.",

            ("de-DE", "valid-email") => "Gib eine gültige E-Mail-Adresse ein.",
            ("de-DE", "creating") => "Konto wird erstellt…",
            ("de-DE", "success-title") => "GuildSync-Konto erstellt!",
            ("de-DE", "success-body") => "Dein Konto wurde erstellt. Melde dich an, um eine Lizenz auszuwählen oder den offiziellen GuildSync-Bot mit Discord zu verbinden.",
            ("de-DE", "timeout") => "Die Anfrage hat zu lange gedauert. Prüfe deine Verbindung und versuche es erneut.",
            ("de-DE", "duplicate") => "Mit dieser E-Mail-Adresse existiert bereits ein Konto.",
            ("de-DE", "service") => "Der GuildSync-Kontodienst ist nicht erreichbar. Prüfe deine Verbindung und versuche es erneut.",
            ("de-DE", "generic") => "Das GuildSync-Konto konnte nicht erstellt werden. Prüfe die Angaben und versuche es erneut.",

            (_, "valid-email") => "Enter a valid email address.",
            (_, "creating") => "Creating account…",
            (_, "success-title") => "GuildSync account created!",
            (_, "success-body") => "Your account was created. Sign in to choose a license or connect the official GuildSync bot to Discord.",
            (_, "timeout") => "The request took too long. Check your connection and try again.",
            (_, "duplicate") => "An account already exists with this email.",
            (_, "service") => "The GuildSync account service could not be reached. Check your connection and try again.",
            _ => "We could not create your GuildSync account. Check the information and try again."
        };
    }
}