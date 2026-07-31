using System.Windows;
using System.Windows.Media;
using ClonarDC.Services;

namespace ClonarDC;

public partial class RegisterWindow : Window
{
    private const string LocalPreviewUrl = "http://127.0.0.1:8787";
    private readonly AuthClient _auth;
    private bool _submitting;

    public string RegisteredEmail { get; private set; } = "";
    public bool UsedLocalFallback { get; private set; }
    public string LocalFallbackReason { get; private set; } = "";

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
            await RegisterWithRecoveryAsync(name, email, password);

            RegisteredEmail = email;
            FormPanel.Visibility = Visibility.Collapsed;
            SuccessTitleText.Text = UsedLocalFallback ? Copy("local-success-title") : Copy("success-title");
            SuccessBodyText.Text = UsedLocalFallback ? Copy("local-success-body") : Copy("success-body");
            SuccessPanel.Visibility = Visibility.Visible;

            await Task.Delay(900);
            DialogResult = true;
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

    private async Task RegisterWithRecoveryAsync(string name, string email, string password)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _auth.RegisterAsync(name, email, password, timeout.Token);
            return;
        }
        catch (Exception primaryFailure) when (_auth.UsesCentralBackend && CanUseLocalFallback(primaryFailure))
        {
            LocalFallbackReason = primaryFailure.Message;
        }

        using var localAuth = new AuthClient(LocalPreviewUrl);
        using var localTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        await localAuth.RegisterAsync(name, email, password, localTimeout.Token);
        UsedLocalFallback = true;
    }

    private static bool CanUseLocalFallback(Exception exception)
    {
        if (exception is OperationCanceledException or TimeoutException or HttpRequestException)
            return true;

        var text = exception.Message;
        if (text.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Já existe", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("valid", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("senha", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("password", StringComparison.OrdinalIgnoreCase))
            return false;

        return text.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("serviço", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("service", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("network", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("host", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("tempor", StringComparison.OrdinalIgnoreCase);
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
            return Copy("duplicate");

        if (raw.Contains("serviço de contas", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("account service", StringComparison.OrdinalIgnoreCase))
            return Copy("service");

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
            ("pt-BR", "success-body") => "Sua conta foi criada no serviço central. Entre para escolher ou ativar uma licença.",
            ("pt-BR", "local-success-title") => "Conta de pré-visualização criada!",
            ("pt-BR", "local-success-body") => "O serviço central estava indisponível, então o GuildSync criou uma conta local segura para você testar o aplicativo agora. Nenhuma alteração real será enviada ao Discord sem uma conexão aceita.",
            ("pt-BR", "duplicate") => "Já existe uma conta cadastrada com este e-mail.",
            ("pt-BR", "service") => "Não foi possível acessar o serviço de contas do GuildSync nem iniciar o modo local. Reinstale a versão completa e tente novamente.",
            ("pt-BR", "generic") => "Não foi possível criar sua conta GuildSync. Confira os dados e tente novamente.",

            ("es-ES", "valid-email") => "Introduce una dirección de correo válida.",
            ("es-ES", "creating") => "Creando cuenta…",
            ("es-ES", "success-title") => "¡Cuenta de GuildSync creada!",
            ("es-ES", "success-body") => "Tu cuenta fue creada en el servicio central. Inicia sesión para elegir o activar una licencia.",
            ("es-ES", "local-success-title") => "¡Cuenta de vista previa creada!",
            ("es-ES", "local-success-body") => "El servicio central no estaba disponible, así que GuildSync creó una cuenta local segura para probar la aplicación ahora.",
            ("es-ES", "duplicate") => "Ya existe una cuenta registrada con este correo.",
            ("es-ES", "service") => "No se pudo acceder al servicio de cuentas ni iniciar el modo local.",
            ("es-ES", "generic") => "No pudimos crear tu cuenta de GuildSync. Comprueba los datos e inténtalo de nuevo.",

            ("fr-FR", "valid-email") => "Saisissez une adresse e-mail valide.",
            ("fr-FR", "creating") => "Création du compte…",
            ("fr-FR", "success-title") => "Compte GuildSync créé !",
            ("fr-FR", "success-body") => "Votre compte a été créé sur le service central. Connectez-vous pour choisir ou activer une licence.",
            ("fr-FR", "local-success-title") => "Compte d’aperçu créé !",
            ("fr-FR", "local-success-body") => "Le service central était indisponible. GuildSync a créé un compte local sécurisé pour tester l’application maintenant.",
            ("fr-FR", "duplicate") => "Un compte existe déjà avec cette adresse e-mail.",
            ("fr-FR", "service") => "Le service de comptes et le mode local sont inaccessibles.",
            ("fr-FR", "generic") => "Impossible de créer votre compte GuildSync. Vérifiez les informations et réessayez.",

            ("de-DE", "valid-email") => "Gib eine gültige E-Mail-Adresse ein.",
            ("de-DE", "creating") => "Konto wird erstellt…",
            ("de-DE", "success-title") => "GuildSync-Konto erstellt!",
            ("de-DE", "success-body") => "Dein Konto wurde im zentralen Dienst erstellt. Melde dich an, um eine Lizenz auszuwählen oder zu aktivieren.",
            ("de-DE", "local-success-title") => "Vorschaukonto erstellt!",
            ("de-DE", "local-success-body") => "Der zentrale Dienst war nicht verfügbar. GuildSync hat ein sicheres lokales Konto zum sofortigen Testen erstellt.",
            ("de-DE", "duplicate") => "Mit dieser E-Mail-Adresse existiert bereits ein Konto.",
            ("de-DE", "service") => "Kontodienst und lokaler Modus konnten nicht gestartet werden.",
            ("de-DE", "generic") => "Das GuildSync-Konto konnte nicht erstellt werden. Prüfe die Angaben und versuche es erneut.",

            (_, "valid-email") => "Enter a valid email address.",
            (_, "creating") => "Creating account…",
            (_, "success-title") => "GuildSync account created!",
            (_, "success-body") => "Your account was created on the central service. Sign in to select or activate a license.",
            (_, "local-success-title") => "Preview account created!",
            (_, "local-success-body") => "The central service was unavailable, so GuildSync created a safe local account for testing the app now. No real Discord changes will be sent without an accepted connection.",
            (_, "duplicate") => "An account already exists with this email.",
            (_, "service") => "The GuildSync account service and local mode could not be reached.",
            _ => "We could not create your GuildSync account. Check the information and try again."
        };
    }
}
