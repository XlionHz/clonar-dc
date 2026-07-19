using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ClonarDC.Services;

public sealed record LanguageOption(string Code, string DisplayName);

public static class LocalizationService
{
    private const string RegistryPath = @"Software\Clonar DC";
    private const string RegistryValue = "Language";

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("en-US", "English"),
        new("pt-BR", "Português (Brasil)"),
        new("es-ES", "Español"),
        new("fr-FR", "Français"),
        new("de-DE", "Deutsch")
    ];

    public static string CurrentCode { get; private set; } = "en-US";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pt-BR"] = Portuguese(),
            ["es-ES"] = Spanish(),
            ["fr-FR"] = French(),
            ["de-DE"] = German()
        };

    public static void Initialize()
    {
        string? configured = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            configured = key?.GetValue(RegistryValue) as string;
        }
        catch
        {
            // A missing or inaccessible setting must never prevent startup.
        }

        SetCulture(Normalize(configured));
    }

    public static void Save(string code)
    {
        code = Normalize(code);
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue(RegistryValue, code, RegistryValueKind.String);
        SetCulture(code);
    }

    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english) || CurrentCode.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            return english;

        return Translations.TryGetValue(CurrentCode, out var language)
               && language.TryGetValue(english, out var translated)
            ? translated
            : english;
    }

    public static string F(string english, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, T(english), values);

    public static void Apply(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        ApplyRecursive(root, visited);
    }

    private static void ApplyRecursive(DependencyObject node, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node)) return;

        switch (node)
        {
            case Window window:
                window.Title = T(window.Title);
                break;
            case TextBlock textBlock when !string.IsNullOrWhiteSpace(textBlock.Text):
                textBlock.Text = T(textBlock.Text);
                break;
            case HeaderedContentControl headeredContent:
                if (headeredContent.Header is string header) headeredContent.Header = T(header);
                if (headeredContent.Content is string headeredContentText) headeredContent.Content = T(headeredContentText);
                break;
            case HeaderedItemsControl headeredItems when headeredItems.Header is string itemsHeader:
                headeredItems.Header = T(itemsHeader);
                break;
            case ContentControl contentControl when contentControl.Content is string content:
                contentControl.Content = T(content);
                break;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            ApplyRecursive(child, visited);

        if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                ApplyRecursive(VisualTreeHelper.GetChild(node, i), visited);
        }
    }

    private static void SetCulture(string code)
    {
        CurrentCode = code;
        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en-US";
        code = code.Trim();
        return SupportedLanguages.Any(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            ? SupportedLanguages.First(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).Code
            : "en-US";
    }

    private static Dictionary<string, string> Portuguese() => new(StringComparer.Ordinal)
    {
        ["Server structures, organized with precision."] = "Estruturas de servidores, organizadas com precisão.",
        ["Sign in"] = "Entrar", ["Access your Clonar DC account"] = "Acesse sua conta Clonar DC",
        ["Email"] = "E-mail", ["Password"] = "Senha", ["Create account"] = "Criar conta",
        ["Waiting for approval"] = "Esperando autorização", ["Status: Pending"] = "Status: Pendente",
        ["Questions? Open a ticket in the Questions channel on Discord."] = "Qualquer dúvida, abra um ticket em Dúvidas no Discord.",
        ["After registration, the account will wait for administrator approval."] = "Após o cadastro, a conta ficará aguardando aprovação do administrador.",
        ["Name"] = "Nome", ["Repeat password"] = "Repita a senha",
        ["Overview"] = "Visão geral", ["Cloning"] = "Clonagem", ["Backups"] = "Backups", ["Operations"] = "Operações",
        ["License"] = "Licença", ["Settings"] = "Configurações", ["Administration"] = "Administração", ["Sign out"] = "Sair da conta",
        ["Quick control of your Clonar DC environment."] = "Controle rápido do seu ambiente Clonar DC.",
        ["Local backups"] = "Backups locais", ["Last operation"] = "Última operação", ["None in this session"] = "Nenhuma nesta sessão",
        ["Safe workflow"] = "Fluxo seguro",
        ["1. Enter the official bot Token  →  2. Test it  →  3. Select source and destination  →  4. Analyze differences  →  5. Create a backup  →  6. Clone."] = "1. Informe o Token do bot oficial  →  2. Teste  →  3. Selecione origem e destino  →  4. Analise as diferenças  →  5. Faça backup  →  6. Clone.",
        ["Analyze before changing anything. The Token stays on this computer."] = "Analise antes de alterar. O Token permanece neste computador.",
        ["Test Token"] = "Testar Token", ["Source server"] = "Servidor original", ["Destination server"] = "Servidor de destino", ["Mode"] = "Modo",
        ["Safe — only adds missing items"] = "Seguro — apenas adiciona o que falta", ["Merge — preserves matching items"] = "Mesclar — preserva correspondências", ["Safe exact — recreates the destination"] = "Exato seguro — recria o destino",
        ["Protections"] = "Proteções", ["Automatically back up the destination"] = "Criar backup automático do destino", ["Remember Token using Windows protection"] = "Lembrar Token com proteção do Windows",
        ["Analyze"] = "Analisar", ["Clone"] = "Clonar", ["Operation plan"] = "Plano da operação", ["Test the Token and run an analysis to generate the plan."] = "Teste o Token e faça uma análise para gerar o plano.", ["Live log"] = "Log ao vivo",
        ["Versioned local snapshots with integrity verification."] = "Snapshots locais versionados com verificação de integridade.", ["Refresh"] = "Atualizar", ["Open folder"] = "Abrir pasta", ["Selected backup"] = "Backup selecionado", ["Verify integrity"] = "Verificar integridade", ["Restore to selected server"] = "Restaurar no servidor selecionado",
        ["Session history and diagnostics without credentials."] = "Histórico desta sessão e diagnóstico sem credenciais.", ["Security"] = "Segurança", ["Delete saved Token"] = "Apagar Token salvo", ["Storage"] = "Armazenamento",
        ["Users, approvals and licenses."] = "Usuários, aprovações e licenças.", ["Refresh users"] = "Atualizar usuários", ["Actions"] = "Ações",
        ["1 month"] = "1 mês", ["3 months"] = "3 meses", ["6 months"] = "6 meses", ["12 months"] = "12 meses", ["Permanent"] = "Permanente",
        ["Approve / renew"] = "Aprovar / renovar", ["Suspend"] = "Suspender", ["Reactivate"] = "Reativar", ["Revoke"] = "Revogar",
        ["Updates"] = "Atualizações", ["The app checks for new versions automatically."] = "O app verifica novas versões automaticamente.", ["Check for updates"] = "Verificar atualizações", ["No update check has run yet."] = "Nenhuma verificação foi feita ainda.",
        ["Enter your email and password."] = "Informe o e-mail e a senha.", ["Email or password is incorrect."] = "E-mail ou senha incorretos.", ["Opening developer mode…"] = "Abrindo modo de desenvolvimento…", ["Signing in…"] = "Entrando…",
        ["Your account exists, but it does not have an active license yet."] = "Sua conta existe, mas ainda não possui uma licença ativa.", ["Account created successfully. It is now waiting for approval."] = "Conta criada com sucesso. Agora ela está esperando autorização.",
        ["Fill in the name and email."] = "Preencha nome e e-mail.", ["Use a password with at least 8 characters."] = "Use uma senha com pelo menos 8 caracteres.", ["The passwords do not match."] = "As senhas não coincidem.",
        ["Do you want to sign out and return to the login screen?"] = "Deseja sair desta conta e voltar para a tela de login?", ["Sign out of account"] = "Sair da conta"
    };

    private static Dictionary<string, string> Spanish() => new(StringComparer.Ordinal)
    {
        ["Server structures, organized with precision."] = "Estructuras de servidores, organizadas con precisión.",
        ["Sign in"] = "Iniciar sesión", ["Access your Clonar DC account"] = "Accede a tu cuenta de Clonar DC", ["Email"] = "Correo electrónico", ["Password"] = "Contraseña", ["Create account"] = "Crear cuenta",
        ["Waiting for approval"] = "Esperando autorización", ["Status: Pending"] = "Estado: Pendiente", ["Questions? Open a ticket in the Questions channel on Discord."] = "¿Tienes dudas? Abre un ticket en el canal de Preguntas de Discord.",
        ["After registration, the account will wait for administrator approval."] = "Después del registro, la cuenta quedará pendiente de aprobación del administrador.", ["Name"] = "Nombre", ["Repeat password"] = "Repetir contraseña",
        ["Overview"] = "Resumen", ["Cloning"] = "Clonación", ["Backups"] = "Copias de seguridad", ["Operations"] = "Operaciones", ["License"] = "Licencia", ["Settings"] = "Configuración", ["Administration"] = "Administración", ["Sign out"] = "Cerrar sesión",
        ["Quick control of your Clonar DC environment."] = "Control rápido de tu entorno Clonar DC.", ["Local backups"] = "Copias locales", ["Last operation"] = "Última operación", ["None in this session"] = "Ninguna en esta sesión", ["Safe workflow"] = "Flujo seguro",
        ["Analyze before changing anything. The Token stays on this computer."] = "Analiza antes de cambiar nada. El Token permanece en este equipo.", ["Test Token"] = "Probar Token", ["Source server"] = "Servidor de origen", ["Destination server"] = "Servidor de destino", ["Mode"] = "Modo",
        ["Safe — only adds missing items"] = "Seguro — solo añade lo que falta", ["Merge — preserves matching items"] = "Combinar — conserva las coincidencias", ["Safe exact — recreates the destination"] = "Exacto seguro — recrea el destino", ["Protections"] = "Protecciones",
        ["Automatically back up the destination"] = "Crear copia automática del destino", ["Remember Token using Windows protection"] = "Recordar Token con la protección de Windows", ["Analyze"] = "Analizar", ["Clone"] = "Clonar", ["Operation plan"] = "Plan de operación", ["Test the Token and run an analysis to generate the plan."] = "Prueba el Token y ejecuta un análisis para generar el plan.", ["Live log"] = "Registro en vivo",
        ["Versioned local snapshots with integrity verification."] = "Instantáneas locales versionadas con verificación de integridad.", ["Refresh"] = "Actualizar", ["Open folder"] = "Abrir carpeta", ["Selected backup"] = "Copia seleccionada", ["Verify integrity"] = "Verificar integridad", ["Restore to selected server"] = "Restaurar en el servidor seleccionado",
        ["Session history and diagnostics without credentials."] = "Historial de la sesión y diagnósticos sin credenciales.", ["Security"] = "Seguridad", ["Delete saved Token"] = "Eliminar Token guardado", ["Storage"] = "Almacenamiento",
        ["Users, approvals and licenses."] = "Usuarios, aprobaciones y licencias.", ["Refresh users"] = "Actualizar usuarios", ["Actions"] = "Acciones", ["1 month"] = "1 mes", ["3 months"] = "3 meses", ["6 months"] = "6 meses", ["12 months"] = "12 meses", ["Permanent"] = "Permanente",
        ["Approve / renew"] = "Aprobar / renovar", ["Suspend"] = "Suspender", ["Reactivate"] = "Reactivar", ["Revoke"] = "Revocar", ["Updates"] = "Actualizaciones", ["The app checks for new versions automatically."] = "La aplicación busca nuevas versiones automáticamente.", ["Check for updates"] = "Buscar actualizaciones", ["No update check has run yet."] = "Aún no se ha realizado ninguna comprobación.",
        ["Enter your email and password."] = "Introduce tu correo y contraseña.", ["Email or password is incorrect."] = "El correo o la contraseña son incorrectos.", ["Opening developer mode…"] = "Abriendo el modo de desarrollo…", ["Signing in…"] = "Iniciando sesión…", ["Your account exists, but it does not have an active license yet."] = "Tu cuenta existe, pero todavía no tiene una licencia activa.", ["Account created successfully. It is now waiting for approval."] = "Cuenta creada correctamente. Ahora está esperando aprobación.",
        ["Fill in the name and email."] = "Completa el nombre y el correo.", ["Use a password with at least 8 characters."] = "Usa una contraseña de al menos 8 caracteres.", ["The passwords do not match."] = "Las contraseñas no coinciden.", ["Do you want to sign out and return to the login screen?"] = "¿Quieres cerrar sesión y volver a la pantalla de inicio?", ["Sign out of account"] = "Cerrar sesión"
    };

    private static Dictionary<string, string> French() => new(StringComparer.Ordinal)
    {
        ["Server structures, organized with precision."] = "Structures de serveurs, organisées avec précision.", ["Sign in"] = "Se connecter", ["Access your Clonar DC account"] = "Accédez à votre compte Clonar DC", ["Email"] = "E-mail", ["Password"] = "Mot de passe", ["Create account"] = "Créer un compte",
        ["Waiting for approval"] = "En attente d’autorisation", ["Status: Pending"] = "Statut : En attente", ["Questions? Open a ticket in the Questions channel on Discord."] = "Une question ? Ouvrez un ticket dans le salon Questions sur Discord.", ["After registration, the account will wait for administrator approval."] = "Après l’inscription, le compte attendra l’approbation de l’administrateur.", ["Name"] = "Nom", ["Repeat password"] = "Répéter le mot de passe",
        ["Overview"] = "Vue d’ensemble", ["Cloning"] = "Clonage", ["Backups"] = "Sauvegardes", ["Operations"] = "Opérations", ["License"] = "Licence", ["Settings"] = "Paramètres", ["Administration"] = "Administration", ["Sign out"] = "Se déconnecter", ["Quick control of your Clonar DC environment."] = "Contrôle rapide de votre environnement Clonar DC.", ["Local backups"] = "Sauvegardes locales", ["Last operation"] = "Dernière opération", ["None in this session"] = "Aucune dans cette session", ["Safe workflow"] = "Flux sécurisé",
        ["Analyze before changing anything. The Token stays on this computer."] = "Analysez avant toute modification. Le Token reste sur cet ordinateur.", ["Test Token"] = "Tester le Token", ["Source server"] = "Serveur source", ["Destination server"] = "Serveur de destination", ["Mode"] = "Mode", ["Safe — only adds missing items"] = "Sûr — ajoute uniquement les éléments manquants", ["Merge — preserves matching items"] = "Fusion — conserve les éléments correspondants", ["Safe exact — recreates the destination"] = "Exact sécurisé — recrée la destination", ["Protections"] = "Protections", ["Automatically back up the destination"] = "Sauvegarder automatiquement la destination", ["Remember Token using Windows protection"] = "Mémoriser le Token avec la protection Windows", ["Analyze"] = "Analyser", ["Clone"] = "Cloner", ["Operation plan"] = "Plan de l’opération", ["Test the Token and run an analysis to generate the plan."] = "Testez le Token et lancez une analyse pour générer le plan.", ["Live log"] = "Journal en direct",
        ["Versioned local snapshots with integrity verification."] = "Instantanés locaux versionnés avec vérification d’intégrité.", ["Refresh"] = "Actualiser", ["Open folder"] = "Ouvrir le dossier", ["Selected backup"] = "Sauvegarde sélectionnée", ["Verify integrity"] = "Vérifier l’intégrité", ["Restore to selected server"] = "Restaurer sur le serveur sélectionné", ["Session history and diagnostics without credentials."] = "Historique de session et diagnostics sans identifiants.", ["Security"] = "Sécurité", ["Delete saved Token"] = "Supprimer le Token enregistré", ["Storage"] = "Stockage",
        ["Users, approvals and licenses."] = "Utilisateurs, approbations et licences.", ["Refresh users"] = "Actualiser les utilisateurs", ["Actions"] = "Actions", ["1 month"] = "1 mois", ["3 months"] = "3 mois", ["6 months"] = "6 mois", ["12 months"] = "12 mois", ["Permanent"] = "Permanent", ["Approve / renew"] = "Approuver / renouveler", ["Suspend"] = "Suspendre", ["Reactivate"] = "Réactiver", ["Revoke"] = "Révoquer", ["Updates"] = "Mises à jour", ["The app checks for new versions automatically."] = "L’application recherche automatiquement les nouvelles versions.", ["Check for updates"] = "Rechercher des mises à jour", ["No update check has run yet."] = "Aucune vérification n’a encore été effectuée.",
        ["Enter your email and password."] = "Saisissez votre e-mail et votre mot de passe.", ["Email or password is incorrect."] = "E-mail ou mot de passe incorrect.", ["Opening developer mode…"] = "Ouverture du mode développement…", ["Signing in…"] = "Connexion…", ["Your account exists, but it does not have an active license yet."] = "Votre compte existe, mais il ne possède pas encore de licence active.", ["Account created successfully. It is now waiting for approval."] = "Compte créé avec succès. Il attend maintenant une approbation.", ["Fill in the name and email."] = "Renseignez le nom et l’e-mail.", ["Use a password with at least 8 characters."] = "Utilisez un mot de passe d’au moins 8 caractères.", ["The passwords do not match."] = "Les mots de passe ne correspondent pas.", ["Do you want to sign out and return to the login screen?"] = "Voulez-vous vous déconnecter et revenir à l’écran de connexion ?", ["Sign out of account"] = "Se déconnecter"
    };

    private static Dictionary<string, string> German() => new(StringComparer.Ordinal)
    {
        ["Server structures, organized with precision."] = "Serverstrukturen, präzise organisiert.", ["Sign in"] = "Anmelden", ["Access your Clonar DC account"] = "Melde dich bei deinem Clonar-DC-Konto an", ["Email"] = "E-Mail", ["Password"] = "Passwort", ["Create account"] = "Konto erstellen", ["Waiting for approval"] = "Warten auf Freigabe", ["Status: Pending"] = "Status: Ausstehend", ["Questions? Open a ticket in the Questions channel on Discord."] = "Fragen? Eröffne ein Ticket im Fragen-Kanal auf Discord.", ["After registration, the account will wait for administrator approval."] = "Nach der Registrierung wartet das Konto auf die Freigabe durch einen Administrator.", ["Name"] = "Name", ["Repeat password"] = "Passwort wiederholen",
        ["Overview"] = "Übersicht", ["Cloning"] = "Klonen", ["Backups"] = "Sicherungen", ["Operations"] = "Vorgänge", ["License"] = "Lizenz", ["Settings"] = "Einstellungen", ["Administration"] = "Verwaltung", ["Sign out"] = "Abmelden", ["Quick control of your Clonar DC environment."] = "Schnellsteuerung deiner Clonar-DC-Umgebung.", ["Local backups"] = "Lokale Sicherungen", ["Last operation"] = "Letzter Vorgang", ["None in this session"] = "Keiner in dieser Sitzung", ["Safe workflow"] = "Sicherer Ablauf",
        ["Analyze before changing anything. The Token stays on this computer."] = "Vor Änderungen analysieren. Der Token bleibt auf diesem Computer.", ["Test Token"] = "Token testen", ["Source server"] = "Quellserver", ["Destination server"] = "Zielserver", ["Mode"] = "Modus", ["Safe — only adds missing items"] = "Sicher — fügt nur Fehlendes hinzu", ["Merge — preserves matching items"] = "Zusammenführen — behält Übereinstimmungen", ["Safe exact — recreates the destination"] = "Sicher exakt — erstellt das Ziel neu", ["Protections"] = "Schutzmaßnahmen", ["Automatically back up the destination"] = "Ziel automatisch sichern", ["Remember Token using Windows protection"] = "Token mit Windows-Schutz speichern", ["Analyze"] = "Analysieren", ["Clone"] = "Klonen", ["Operation plan"] = "Vorgangsplan", ["Test the Token and run an analysis to generate the plan."] = "Token testen und analysieren, um den Plan zu erstellen.", ["Live log"] = "Live-Protokoll",
        ["Versioned local snapshots with integrity verification."] = "Versionierte lokale Snapshots mit Integritätsprüfung.", ["Refresh"] = "Aktualisieren", ["Open folder"] = "Ordner öffnen", ["Selected backup"] = "Ausgewählte Sicherung", ["Verify integrity"] = "Integrität prüfen", ["Restore to selected server"] = "Auf ausgewähltem Server wiederherstellen", ["Session history and diagnostics without credentials."] = "Sitzungsverlauf und Diagnose ohne Zugangsdaten.", ["Security"] = "Sicherheit", ["Delete saved Token"] = "Gespeicherten Token löschen", ["Storage"] = "Speicher",
        ["Users, approvals and licenses."] = "Benutzer, Freigaben und Lizenzen.", ["Refresh users"] = "Benutzer aktualisieren", ["Actions"] = "Aktionen", ["1 month"] = "1 Monat", ["3 months"] = "3 Monate", ["6 months"] = "6 Monate", ["12 months"] = "12 Monate", ["Permanent"] = "Dauerhaft", ["Approve / renew"] = "Freigeben / verlängern", ["Suspend"] = "Sperren", ["Reactivate"] = "Reaktivieren", ["Revoke"] = "Widerrufen", ["Updates"] = "Updates", ["The app checks for new versions automatically."] = "Die App sucht automatisch nach neuen Versionen.", ["Check for updates"] = "Nach Updates suchen", ["No update check has run yet."] = "Es wurde noch keine Update-Prüfung durchgeführt.",
        ["Enter your email and password."] = "Gib E-Mail und Passwort ein.", ["Email or password is incorrect."] = "E-Mail oder Passwort ist falsch.", ["Opening developer mode…"] = "Entwicklermodus wird geöffnet…", ["Signing in…"] = "Anmeldung…", ["Your account exists, but it does not have an active license yet."] = "Dein Konto existiert, hat aber noch keine aktive Lizenz.", ["Account created successfully. It is now waiting for approval."] = "Konto erfolgreich erstellt. Es wartet jetzt auf Freigabe.", ["Fill in the name and email."] = "Name und E-Mail ausfüllen.", ["Use a password with at least 8 characters."] = "Verwende ein Passwort mit mindestens 8 Zeichen.", ["The passwords do not match."] = "Die Passwörter stimmen nicht überein.", ["Do you want to sign out and return to the login screen?"] = "Möchtest du dich abmelden und zum Anmeldebildschirm zurückkehren?", ["Sign out of account"] = "Abmelden"
    };
}