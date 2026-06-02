using System.Globalization;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services;

/// <summary>All user-facing strings for one language.</summary>
public sealed class Strings
{
    // Menu
    public string Dashboard { get; init; } = "Dashboard";
    public string Refresh { get; init; } = "Refresh now";
    public string PanelWindow { get; init; } = "Panel window";
    public string Position { get; init; } = "Position";
    public string PosBottomRight { get; init; } = "Bottom right";
    public string PosBottomLeft { get; init; } = "Bottom left";
    public string PosTopRight { get; init; } = "Top right";
    public string PosTopLeft { get; init; } = "Top left";
    public string PosCenter { get; init; } = "Center";
    public string PosCustom { get; init; } = "Custom (drag the panel)";
    public string Sticky { get; init; } = "Pinned (don't auto-close)";
    public string AlwaysOnTop { get; init; } = "Always on top";
    public string UpdateFrequency { get; init; } = "Update frequency";
    public string Sec30 { get; init; } = "30 seconds";
    public string Min1 { get; init; } = "1 minute";
    public string Min5 { get; init; } = "5 minutes";
    public string Min15 { get; init; } = "15 minutes";
    public string Notifications { get; init; } = "Notifications";
    public string Enabled { get; init; } = "Enabled";
    public string NotifyWhenReaching { get; init; } = "Notify when reaching…";
    public string ColorThreshold { get; init; } = "Color threshold";
    public string DefaultTag { get; init; } = "(default)";
    public string Settings { get; init; } = "Settings";
    public string ShowSpend { get; init; } = "Show estimated spend";
    public string StartWithWindows { get; init; } = "Start with Windows";
    public string EditConfig { get; init; } = "Edit config (advanced)…";
    public string OpenDataFolder { get; init; } = "Open data folder";
    public string Language { get; init; } = "Language";
    public string SystemDefault { get; init; } = "System default";
    public string Exit { get; init; } = "Exit";

    // Dashboard
    public string SessionWord { get; init; } = "Session";
    public string WeekWord { get; init; } = "Week";
    public string ResetsIn { get; init; } = "resets in";
    public string Resetting { get; init; } = "resetting…";
    public string RollingHint { get; init; } = "rolling 5h window from your 1st request";
    public string SpendHeaderFormat { get; init; } = "Estimated spend ({0}d, API-equiv)";
    public string Loading { get; init; } = "Loading…";
    public string UpdatedAt { get; init; } = "Updated";
    /// <summary>{0} = elapsed text (e.g. "5 min"). "ago 5 min" / "hace 5 min".</summary>
    public string AgoFormat { get; init; } = "{0} ago";
    public string StaleLabel { get; init; } = "stale";
    public string LocalSeal { get; init; } = "Your credentials and data never leave this device · no telemetry";
    public string HintClickToHide { get; init; } = "click the icon to hide";
    public string HintPinnedClose { get; init; } = "pinned · ✕ to close";
    public string PreviousDataTip { get; init; } = "⚠ previous data (offline)";
    public string PreviousDataFooter { get; init; } = "previous data";

    // States
    public string StateNoCredentials { get; init; } = "Not signed in — log in to Claude Code";
    public string StateAuthExpired { get; init; } = "Session expired — open Claude Code";
    public string StateRateLimited { get; init; } = "Rate limited — retrying";
    public string StateNetworkError { get; init; } = "No connection to Anthropic";

    // Notifications: {0} = milestone percent
    public string NotifQuotaFormat { get; init; } = "Claude {0}%+ quota used";

    // Theme
    public string Theme { get; init; } = "Theme";
    public string ThemeSystem { get; init; } = "System";
    public string ThemeDark { get; init; } = "Dark";
    public string ThemeLight { get; init; } = "Light";
    public string ThemeCli { get; init; } = "CLI";
    public string ThemeImported { get; init; } = "Imported";
    public string ImportTheme { get; init; } = "Import .itermcolors…";

    // Service health
    public string ShowServiceStatus { get; init; } = "Show service status";
    public string HealthOk { get; init; } = "Operational";
    public string HealthDegraded { get; init; } = "Degraded";
    public string HealthOutage { get; init; } = "Outage";

    // Dashboard sections (v0.3)
    public string SectionQuota { get; init; } = "Quota";
    public string SectionSessions { get; init; } = "Sessions";
    public string SectionSpend { get; init; } = "Spend";
    public string SectionChart { get; init; } = "Chart";

    // Settings panel (v0.3)
    public string Back { get; init; } = "Back";
    public string MascotSizeLabel { get; init; } = "Mascot size";
    public string MascotSizeCompact { get; init; } = "compact";
    public string MascotSizeLarge { get; init; } = "large";

    // Chart & misc
    public string UsageChart { get; init; } = "Usage chart";
    public string NoData { get; init; } = "No data in this range";
    public string OpenBilling { get; init; } = "Open billing…";
    public string ChartPeak { get; init; } = "peak";
    public string ChartTotal { get; init; } = "total";
    public string ChartTabSpend { get; init; } = "Spend $";
    public string ChartTabPct { get; init; } = "Quota %";
    public string Opacity { get; init; } = "Opacity";

    // Pace
    public string IconMode { get; init; } = "Icon mode";
    public string PaceAlerts { get; init; } = "Pace alerts";
    public string WinSession { get; init; } = "session (5h)";
    public string WinWeekly { get; init; } = "weekly (7d)";
    public string PaceAlertTitle { get; init; } = "⚠ Quota pace";
    /// <summary>{0} = window name, {1} = ETA time.</summary>
    public string PaceAlertBodyFmt { get; init; } = "At this rate you'll run out of {0} quota by {1}, before the reset";

    // Menu groups + updates
    public string MenuAppearance { get; init; } = "Appearance";
    public string MenuSections { get; init; } = "Sections";
    public string MenuIcon { get; init; } = "Icon";
    public string MenuAdvanced { get; init; } = "Advanced";
    public string About { get; init; } = "About";
    public string CheckUpdates { get; init; } = "Check for updates";
    public string Checking { get; init; } = "Checking for updates…";
    /// <summary>{0} = latest version.</summary>
    public string UpdateAvailableFmt { get; init; } = "⬇ Update available {0}";
    /// <summary>{0} = current version.</summary>
    public string UpToDateFmt { get; init; } = "You're up to date (v{0}).";
    /// <summary>{0} = current, {1} = latest.</summary>
    public string UpdatePromptFmt { get; init; } = "Current version: v{0}\nLatest available: {1}\n\nDownload it now?";
    /// <summary>{0} = path.</summary>
    public string UpdateDownloadedFmt { get; init; } = "Downloaded to:\n{0}\n\nClose ClaudeBar and replace the running .exe with this file.";
    public string UpdateFailed { get; init; } = "Couldn't check/download the update.";

    // Live sessions / Mascot
    public string MenuLiveSessions { get; init; } = "Live sessions";
    public string MenuShowMascot { get; init; } = "Show mascot";
    public string MenuSuppressWhenFocused { get; init; } = "Mute when terminal focused";
    public string MenuInstallHooks { get; init; } = "Enable (install hooks)…";
    public string MenuUninstallHooks { get; init; } = "Disable (remove hooks)";
    public string NoActiveSessions { get; init; } = "No active sessions";
    public string SessionPhaseIdle { get; init; } = "idle";
    public string SessionPhaseProcessing { get; init; } = "working";
    public string SessionPhaseWaitingApproval { get; init; } = "waiting for OK";
    public string SessionPhaseWaitingInput { get; init; } = "your turn";
    public string SessionPhaseCompacting { get; init; } = "compacting";
    /// <summary>{0} = nombre del proyecto.</summary>
    public string NotifWaitingApprovalFmt { get; init; } = "Claude is waiting for your OK in {0}";
    /// <summary>{0} = nombre del proyecto.</summary>
    public string NotifWaitingInputFmt { get; init; } = "Claude finished in {0}";
    public string LiveSessionsTitle { get; init; } = "Claude sessions";
    /// <summary>{0} = ruta del backup de settings.json.</summary>
    public string HooksInstalledFmt { get; init; } = "Live sessions on. Backup: {0}";
    public string HooksRemoved { get; init; } = "Live sessions off. Hooks removed.";
    public string Changelog { get; init; } = "What's new";
}

public static class Localization
{
    /// <summary>(code, native name) in menu order. "system" handled separately.</summary>
    public static readonly (string Code, string Native)[] Languages =
    {
        ("en", "English"),
        ("es", "Español"),
        ("nl", "Nederlands"),
        ("fr", "Français"),
        ("de", "Deutsch"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("zh-Hant", "繁體中文")
    };

    public static Strings ForConfig(AppConfig cfg)
    {
        var code = string.IsNullOrEmpty(cfg.Language) || cfg.Language == "system"
            ? ResolveSystemCode()
            : cfg.Language;
        return Get(code);
    }

    /// <summary>Orden de ciclo del selector de idioma del panel: "system" + los 8 códigos.</summary>
    public static readonly string[] LanguageCycle =
        new[] { "system" }.Concat(Languages.Select(l => l.Code)).ToArray();

    /// <summary>Nombre legible de un código de idioma ("system" → SystemDefault; resto → nativo).</summary>
    public static string LanguageDisplayName(string code, Strings s)
    {
        if (string.IsNullOrEmpty(code) || code == "system") return s.SystemDefault;
        foreach (var (c, native) in Languages)
            if (c == code) return native;
        return code;
    }

    /// <summary>Siguiente idioma en el ciclo (envuelve al principio).</summary>
    public static string NextLanguage(string current)
    {
        var cur = string.IsNullOrEmpty(current) ? "system" : current;
        int i = Array.IndexOf(LanguageCycle, cur);
        return LanguageCycle[(i < 0 ? 0 : (i + 1) % LanguageCycle.Length)];
    }

    public static string ResolveSystemCode()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return two switch
        {
            "es" => "es",
            "nl" => "nl",
            "fr" => "fr",
            "de" => "de",
            "ja" => "ja",
            "ko" => "ko",
            "zh" => "zh-Hant",
            _ => "en"
        };
    }

    public static Strings Get(string code) => code switch
    {
        "es" => Spanish,
        "nl" => Dutch,
        "fr" => French,
        "de" => German,
        "ja" => Japanese,
        "ko" => Korean,
        "zh-Hant" => TradChinese,
        _ => English
    };

    private static readonly Strings English = new();

    private static readonly Strings Spanish = new()
    {
        Dashboard = "Panel",
        Refresh = "Actualizar ahora",
        PanelWindow = "Ventana del panel",
        Position = "Posición",
        PosBottomRight = "Abajo derecha",
        PosBottomLeft = "Abajo izquierda",
        PosTopRight = "Arriba derecha",
        PosTopLeft = "Arriba izquierda",
        PosCenter = "Centro",
        PosCustom = "Personalizada (arrastra el panel)",
        Sticky = "Fijado (no se cierra solo)",
        AlwaysOnTop = "Siempre encima",
        UpdateFrequency = "Frecuencia de actualización",
        Sec30 = "30 segundos",
        Min1 = "1 minuto",
        Min5 = "5 minutos",
        Min15 = "15 minutos",
        Notifications = "Notificaciones",
        Enabled = "Activadas",
        NotifyWhenReaching = "Avisar al llegar a…",
        ColorThreshold = "Umbral de color",
        DefaultTag = "(def.)",
        Settings = "Ajustes",
        ShowSpend = "Mostrar gasto estimado",
        StartWithWindows = "Iniciar con Windows",
        EditConfig = "Editar config (avanzado)…",
        OpenDataFolder = "Abrir carpeta de datos",
        Language = "Idioma",
        SystemDefault = "Sistema (predeterminado)",
        Exit = "Salir",
        SessionWord = "Sesión",
        WeekWord = "Semana",
        ResetsIn = "resetea en",
        Resetting = "reseteando…",
        RollingHint = "ventana móvil de 5h desde tu 1ª petición",
        SpendHeaderFormat = "Gasto estimado ({0}d, equiv. API)",
        Loading = "Cargando…",
        UpdatedAt = "Actualizado",
        AgoFormat = "hace {0}",
        StaleLabel = "desfasado",
        LocalSeal = "Tus credenciales y datos no salen del equipo · sin telemetría",
        HintClickToHide = "clic en el icono para ocultar",
        HintPinnedClose = "fijado · ✕ para cerrar",
        PreviousDataTip = "⚠ datos previos (sin conexión)",
        PreviousDataFooter = "datos previos",
        StateNoCredentials = "No autenticado — inicia sesión en Claude Code",
        StateAuthExpired = "Sesión caducada — abre Claude Code",
        StateRateLimited = "Límite de peticiones — reintentando",
        StateNetworkError = "Sin conexión con Anthropic",
        NotifQuotaFormat = "Claude {0}%+ de cuota usada",
        Theme = "Tema",
        ThemeSystem = "Sistema",
        ThemeDark = "Oscuro",
        ThemeLight = "Claro",
        ThemeCli = "CLI",
        ThemeImported = "Importado",
        ImportTheme = "Importar .itermcolors…",
        ShowServiceStatus = "Mostrar estado del servicio",
        HealthOk = "Operativo",
        HealthDegraded = "Degradado",
        HealthOutage = "Caído",
        SectionQuota = "Cuota",
        SectionSessions = "Sesiones",
        SectionSpend = "Gasto",
        SectionChart = "Gráfica",
        Back = "Volver",
        MascotSizeLabel = "Tamaño mascota",
        MascotSizeCompact = "compacta",
        MascotSizeLarge = "grande",
        UsageChart = "Gráfica de uso",
        NoData = "Sin datos en este rango",
        OpenBilling = "Abrir facturación…",
        ChartPeak = "máx",
        ChartTotal = "total",
        ChartTabSpend = "Gasto $",
        ChartTabPct = "Cuota %",
        Opacity = "Opacidad",
        IconMode = "Modo de icono",
        PaceAlerts = "Avisos de ritmo",
        WinSession = "de sesión (5h)",
        WinWeekly = "semanal (7d)",
        PaceAlertTitle = "⚠ Ritmo de cuota",
        PaceAlertBodyFmt = "A este ritmo te quedas sin cuota {0} el {1}, antes del reset",
        MenuAppearance = "Apariencia",
        MenuSections = "Secciones",
        MenuIcon = "Icono",
        MenuAdvanced = "Avanzado",
        About = "Acerca de",
        CheckUpdates = "Buscar actualizaciones",
        Checking = "Buscando actualizaciones…",
        UpdateAvailableFmt = "⬇ Actualización disponible {0}",
        UpToDateFmt = "Estás al día (v{0}).",
        UpdatePromptFmt = "Versión actual: v{0}\nÚltima disponible: {1}\n\n¿Descargar ahora?",
        UpdateDownloadedFmt = "Descargado en:\n{0}\n\nCierra ClaudeBar y reemplaza el .exe en uso por este archivo.",
        UpdateFailed = "No se pudo comprobar/descargar la actualización.",
        MenuLiveSessions = "Sesiones en vivo",
        MenuShowMascot = "Mostrar mascota",
        MenuSuppressWhenFocused = "Silenciar si la terminal tiene foco",
        MenuInstallHooks = "Activar (instalar hooks)…",
        MenuUninstallHooks = "Desactivar (quitar hooks)",
        NoActiveSessions = "Sin sesiones activas",
        SessionPhaseIdle = "en reposo",
        SessionPhaseProcessing = "trabajando",
        SessionPhaseWaitingApproval = "espera tu OK",
        SessionPhaseWaitingInput = "tu turno",
        SessionPhaseCompacting = "compactando",
        NotifWaitingApprovalFmt = "Claude espera tu OK en {0}",
        NotifWaitingInputFmt = "Claude terminó en {0}",
        LiveSessionsTitle = "Sesiones de Claude",
        HooksInstalledFmt = "Sesiones en vivo activadas. Backup: {0}",
        HooksRemoved = "Sesiones en vivo desactivadas. Hooks quitados.",
        Changelog = "Novedades"
    };

    private static readonly Strings Dutch = new()
    {
        Dashboard = "Dashboard",
        Refresh = "Nu verversen",
        PanelWindow = "Paneelvenster",
        Position = "Positie",
        PosBottomRight = "Rechtsonder",
        PosBottomLeft = "Linksonder",
        PosTopRight = "Rechtsboven",
        PosTopLeft = "Linksboven",
        PosCenter = "Midden",
        PosCustom = "Aangepast (sleep het paneel)",
        Sticky = "Vastgezet (niet automatisch sluiten)",
        AlwaysOnTop = "Altijd op voorgrond",
        UpdateFrequency = "Verversingsfrequentie",
        Sec30 = "30 seconden",
        Min1 = "1 minuut",
        Min5 = "5 minuten",
        Min15 = "15 minuten",
        Notifications = "Meldingen",
        Enabled = "Ingeschakeld",
        NotifyWhenReaching = "Melden bij het bereiken van…",
        ColorThreshold = "Kleurdrempel",
        DefaultTag = "(standaard)",
        Settings = "Instellingen",
        ShowSpend = "Geschatte uitgaven tonen",
        StartWithWindows = "Starten met Windows",
        EditConfig = "Config bewerken (geavanceerd)…",
        OpenDataFolder = "Datamap openen",
        Language = "Taal",
        SystemDefault = "Systeemstandaard",
        Exit = "Afsluiten",
        SessionWord = "Sessie",
        WeekWord = "Week",
        ResetsIn = "reset over",
        Resetting = "resetten…",
        RollingHint = "voortschrijdend venster van 5u vanaf je 1e verzoek",
        SpendHeaderFormat = "Geschatte uitgaven ({0}d, API-equiv.)",
        Loading = "Laden…",
        UpdatedAt = "Bijgewerkt",
        AgoFormat = "{0} geleden",
        StaleLabel = "verouderd",
        LocalSeal = "Je inloggegevens en data verlaten dit apparaat nooit · geen telemetrie",
        HintClickToHide = "klik op het pictogram om te verbergen",
        HintPinnedClose = "vastgezet · ✕ om te sluiten",
        PreviousDataTip = "⚠ vorige gegevens (offline)",
        PreviousDataFooter = "vorige gegevens",
        StateNoCredentials = "Niet aangemeld — log in bij Claude Code",
        StateAuthExpired = "Sessie verlopen — open Claude Code",
        StateRateLimited = "Verzoeklimiet — opnieuw proberen",
        StateNetworkError = "Geen verbinding met Anthropic",
        NotifQuotaFormat = "Claude {0}%+ quota gebruikt",
        Theme = "Thema",
        ThemeSystem = "Systeem",
        ThemeDark = "Donker",
        ThemeLight = "Licht",
        ThemeCli = "CLI",
        ThemeImported = "Geïmporteerd",
        ImportTheme = "Importeer .itermcolors…",
        ShowServiceStatus = "Servicestatus tonen",
        HealthOk = "Operationeel",
        HealthDegraded = "Verminderd",
        HealthOutage = "Storing",
        UsageChart = "Gebruiksgrafiek",
        NoData = "Geen gegevens in dit bereik",
        OpenBilling = "Facturering openen…",
        ChartPeak = "max",
        ChartTotal = "totaal",
        ChartTabSpend = "Uitgaven $",
        ChartTabPct = "Quota %",
        Opacity = "Dekking",
        IconMode = "Pictogrammodus",
        PaceAlerts = "Tempo-meldingen",
        WinSession = "sessie (5h)",
        WinWeekly = "wekelijks (7d)",
        PaceAlertTitle = "⚠ Quotatempo",
        PaceAlertBodyFmt = "Met dit tempo is je {0}-quota op rond {1}, vóór de reset",
        MenuAppearance = "Weergave",
        MenuSections = "Secties",
        MenuIcon = "Pictogram",
        MenuAdvanced = "Geavanceerd",
        About = "Over",
        CheckUpdates = "Controleren op updates",
        Checking = "Controleren op updates…",
        UpdateAvailableFmt = "⬇ Update beschikbaar {0}",
        UpToDateFmt = "Je bent up-to-date (v{0}).",
        UpdatePromptFmt = "Huidige versie: v{0}\nNieuwste: {1}\n\nNu downloaden?",
        UpdateDownloadedFmt = "Gedownload naar:\n{0}\n\nSluit ClaudeBar en vervang de actieve .exe door dit bestand.",
        UpdateFailed = "Kan de update niet controleren/downloaden.",
        MenuLiveSessions = "Live sessies",
        MenuShowMascot = "Mascotte tonen",
        MenuSuppressWhenFocused = "Dempen als terminal focus heeft",
        MenuInstallHooks = "Inschakelen (hooks installeren)…",
        MenuUninstallHooks = "Uitschakelen (hooks verwijderen)",
        NoActiveSessions = "Geen actieve sessies",
        SessionPhaseIdle = "inactief",
        SessionPhaseProcessing = "bezig",
        SessionPhaseWaitingApproval = "wacht op jouw OK",
        SessionPhaseWaitingInput = "jouw beurt",
        SessionPhaseCompacting = "comprimeren",
        NotifWaitingApprovalFmt = "Claude wacht op jouw OK in {0}",
        NotifWaitingInputFmt = "Claude is klaar in {0}",
        LiveSessionsTitle = "Claude-sessies",
        HooksInstalledFmt = "Live sessies aan. Back-up: {0}",
        HooksRemoved = "Live sessies uit. Hooks verwijderd.",
        Changelog = "Wat is er nieuw"
    };

    private static readonly Strings French = new()
    {
        Dashboard = "Tableau de bord",
        Refresh = "Actualiser maintenant",
        PanelWindow = "Fenêtre du panneau",
        Position = "Position",
        PosBottomRight = "En bas à droite",
        PosBottomLeft = "En bas à gauche",
        PosTopRight = "En haut à droite",
        PosTopLeft = "En haut à gauche",
        PosCenter = "Centre",
        PosCustom = "Personnalisée (glissez le panneau)",
        Sticky = "Épinglé (ne pas fermer auto)",
        AlwaysOnTop = "Toujours au premier plan",
        UpdateFrequency = "Fréquence d'actualisation",
        Sec30 = "30 secondes",
        Min1 = "1 minute",
        Min5 = "5 minutes",
        Min15 = "15 minutes",
        Notifications = "Notifications",
        Enabled = "Activées",
        NotifyWhenReaching = "Alerter à…",
        ColorThreshold = "Seuil de couleur",
        DefaultTag = "(déf.)",
        Settings = "Paramètres",
        ShowSpend = "Afficher le coût estimé",
        StartWithWindows = "Démarrer avec Windows",
        EditConfig = "Éditer la config (avancé)…",
        OpenDataFolder = "Ouvrir le dossier de données",
        Language = "Langue",
        SystemDefault = "Système (par défaut)",
        Exit = "Quitter",
        SessionWord = "Session",
        WeekWord = "Semaine",
        ResetsIn = "réinit. dans",
        Resetting = "réinitialisation…",
        RollingHint = "fenêtre glissante de 5h depuis ta 1re requête",
        SpendHeaderFormat = "Coût estimé ({0}j, équiv. API)",
        Loading = "Chargement…",
        UpdatedAt = "Mis à jour",
        AgoFormat = "il y a {0}",
        StaleLabel = "obsolète",
        LocalSeal = "Tes identifiants et tes données ne quittent jamais cet appareil · sans télémétrie",
        HintClickToHide = "cliquez sur l'icône pour masquer",
        HintPinnedClose = "épinglé · ✕ pour fermer",
        PreviousDataTip = "⚠ données précédentes (hors ligne)",
        PreviousDataFooter = "données précédentes",
        StateNoCredentials = "Non connecté — connectez-vous à Claude Code",
        StateAuthExpired = "Session expirée — ouvrez Claude Code",
        StateRateLimited = "Limite de requêtes — nouvelle tentative",
        StateNetworkError = "Pas de connexion à Anthropic",
        NotifQuotaFormat = "Claude {0}%+ de quota utilisé",
        Theme = "Thème",
        ThemeSystem = "Système",
        ThemeDark = "Sombre",
        ThemeLight = "Clair",
        ThemeCli = "CLI",
        ThemeImported = "Importé",
        ImportTheme = "Importer .itermcolors…",
        ShowServiceStatus = "Afficher l'état du service",
        HealthOk = "Opérationnel",
        HealthDegraded = "Dégradé",
        HealthOutage = "Panne",
        UsageChart = "Graphique d'utilisation",
        NoData = "Aucune donnée sur cette période",
        OpenBilling = "Ouvrir la facturation…",
        ChartPeak = "max",
        ChartTotal = "total",
        ChartTabSpend = "Coût $",
        ChartTabPct = "Quota %",
        Opacity = "Opacité",
        IconMode = "Mode d'icône",
        PaceAlerts = "Alertes de rythme",
        WinSession = "de session (5h)",
        WinWeekly = "hebdomadaire (7d)",
        PaceAlertTitle = "⚠ Rythme de quota",
        PaceAlertBodyFmt = "À ce rythme, tu épuises le quota {0} vers {1}, avant la réinitialisation",
        MenuAppearance = "Apparence",
        MenuSections = "Sections",
        MenuIcon = "Icône",
        MenuAdvanced = "Avancé",
        About = "À propos",
        CheckUpdates = "Rechercher des mises à jour",
        Checking = "Recherche de mises à jour…",
        UpdateAvailableFmt = "⬇ Mise à jour disponible {0}",
        UpToDateFmt = "Tu es à jour (v{0}).",
        UpdatePromptFmt = "Version actuelle : v{0}\nDernière : {1}\n\nTélécharger maintenant ?",
        UpdateDownloadedFmt = "Téléchargé dans :\n{0}\n\nFerme ClaudeBar et remplace le .exe en cours par ce fichier.",
        UpdateFailed = "Impossible de vérifier/télécharger la mise à jour.",
        MenuLiveSessions = "Sessions en direct",
        MenuShowMascot = "Afficher la mascotte",
        MenuSuppressWhenFocused = "Muet si le terminal a le focus",
        MenuInstallHooks = "Activer (installer les hooks)…",
        MenuUninstallHooks = "Désactiver (retirer les hooks)",
        NoActiveSessions = "Aucune session active",
        SessionPhaseIdle = "au repos",
        SessionPhaseProcessing = "en cours",
        SessionPhaseWaitingApproval = "attend votre OK",
        SessionPhaseWaitingInput = "à vous",
        SessionPhaseCompacting = "compactage",
        NotifWaitingApprovalFmt = "Claude attend votre OK dans {0}",
        NotifWaitingInputFmt = "Claude a terminé dans {0}",
        LiveSessionsTitle = "Sessions Claude",
        HooksInstalledFmt = "Sessions en direct activées. Sauvegarde : {0}",
        HooksRemoved = "Sessions en direct désactivées. Hooks retirés.",
        Changelog = "Nouveautés"
    };

    private static readonly Strings German = new()
    {
        Dashboard = "Dashboard",
        Refresh = "Jetzt aktualisieren",
        PanelWindow = "Panel-Fenster",
        Position = "Position",
        PosBottomRight = "Unten rechts",
        PosBottomLeft = "Unten links",
        PosTopRight = "Oben rechts",
        PosTopLeft = "Oben links",
        PosCenter = "Mitte",
        PosCustom = "Benutzerdefiniert (Panel ziehen)",
        Sticky = "Angeheftet (nicht autom. schließen)",
        AlwaysOnTop = "Immer im Vordergrund",
        UpdateFrequency = "Aktualisierungsintervall",
        Sec30 = "30 Sekunden",
        Min1 = "1 Minute",
        Min5 = "5 Minuten",
        Min15 = "15 Minuten",
        Notifications = "Benachrichtigungen",
        Enabled = "Aktiviert",
        NotifyWhenReaching = "Benachrichtigen bei…",
        ColorThreshold = "Farbschwelle",
        DefaultTag = "(Std.)",
        Settings = "Einstellungen",
        ShowSpend = "Geschätzte Kosten anzeigen",
        StartWithWindows = "Mit Windows starten",
        EditConfig = "Konfig bearbeiten (erweitert)…",
        OpenDataFolder = "Datenordner öffnen",
        Language = "Sprache",
        SystemDefault = "Systemstandard",
        Exit = "Beenden",
        SessionWord = "Sitzung",
        WeekWord = "Woche",
        ResetsIn = "Reset in",
        Resetting = "wird zurückgesetzt…",
        RollingHint = "gleitendes 5-Std-Fenster ab deiner 1. Anfrage",
        SpendHeaderFormat = "Geschätzte Kosten ({0}T, API-Äquiv.)",
        Loading = "Laden…",
        UpdatedAt = "Aktualisiert",
        AgoFormat = "vor {0}",
        StaleLabel = "veraltet",
        LocalSeal = "Deine Anmeldedaten und Daten verlassen nie dieses Gerät · keine Telemetrie",
        HintClickToHide = "Symbol anklicken zum Ausblenden",
        HintPinnedClose = "angeheftet · ✕ zum Schließen",
        PreviousDataTip = "⚠ vorherige Daten (offline)",
        PreviousDataFooter = "vorherige Daten",
        StateNoCredentials = "Nicht angemeldet — bei Claude Code anmelden",
        StateAuthExpired = "Sitzung abgelaufen — Claude Code öffnen",
        StateRateLimited = "Anfragelimit — erneuter Versuch",
        StateNetworkError = "Keine Verbindung zu Anthropic",
        NotifQuotaFormat = "Claude {0}%+ Kontingent genutzt",
        Theme = "Design",
        ThemeSystem = "System",
        ThemeDark = "Dunkel",
        ThemeLight = "Hell",
        ThemeCli = "CLI",
        ThemeImported = "Importiert",
        ImportTheme = ".itermcolors importieren…",
        ShowServiceStatus = "Dienststatus anzeigen",
        HealthOk = "Betriebsbereit",
        HealthDegraded = "Beeinträchtigt",
        HealthOutage = "Störung",
        UsageChart = "Nutzungsdiagramm",
        NoData = "Keine Daten in diesem Bereich",
        OpenBilling = "Abrechnung öffnen…",
        ChartPeak = "Max",
        ChartTotal = "Gesamt",
        ChartTabSpend = "Kosten $",
        ChartTabPct = "Kontingent %",
        Opacity = "Deckkraft",
        IconMode = "Symbolmodus",
        PaceAlerts = "Tempo-Warnungen",
        WinSession = "Sitzung (5h)",
        WinWeekly = "wöchentlich (7d)",
        PaceAlertTitle = "⚠ Kontingent-Tempo",
        PaceAlertBodyFmt = "In diesem Tempo ist dein {0}-Kontingent um {1} aufgebraucht, vor dem Reset",
        MenuAppearance = "Darstellung",
        MenuSections = "Abschnitte",
        MenuIcon = "Symbol",
        MenuAdvanced = "Erweitert",
        About = "Über",
        CheckUpdates = "Nach Updates suchen",
        Checking = "Suche nach Updates…",
        UpdateAvailableFmt = "⬇ Update verfügbar {0}",
        UpToDateFmt = "Du bist auf dem neuesten Stand (v{0}).",
        UpdatePromptFmt = "Aktuelle Version: v{0}\nNeueste: {1}\n\nJetzt herunterladen?",
        UpdateDownloadedFmt = "Heruntergeladen nach:\n{0}\n\nSchließe ClaudeBar und ersetze die laufende .exe durch diese Datei.",
        UpdateFailed = "Update konnte nicht geprüft/geladen werden.",
        MenuLiveSessions = "Live-Sitzungen",
        MenuShowMascot = "Maskottchen anzeigen",
        MenuSuppressWhenFocused = "Stumm, wenn Terminal im Fokus",
        MenuInstallHooks = "Aktivieren (Hooks installieren)…",
        MenuUninstallHooks = "Deaktivieren (Hooks entfernen)",
        NoActiveSessions = "Keine aktiven Sitzungen",
        SessionPhaseIdle = "im Leerlauf",
        SessionPhaseProcessing = "arbeitet",
        SessionPhaseWaitingApproval = "wartet auf dein OK",
        SessionPhaseWaitingInput = "du bist dran",
        SessionPhaseCompacting = "komprimiert",
        NotifWaitingApprovalFmt = "Claude wartet auf dein OK in {0}",
        NotifWaitingInputFmt = "Claude ist fertig in {0}",
        LiveSessionsTitle = "Claude-Sitzungen",
        HooksInstalledFmt = "Live-Sitzungen an. Backup: {0}",
        HooksRemoved = "Live-Sitzungen aus. Hooks entfernt.",
        Changelog = "Neuerungen"
    };

    private static readonly Strings Japanese = new()
    {
        Dashboard = "ダッシュボード",
        Refresh = "今すぐ更新",
        PanelWindow = "パネルウィンドウ",
        Position = "位置",
        PosBottomRight = "右下",
        PosBottomLeft = "左下",
        PosTopRight = "右上",
        PosTopLeft = "左上",
        PosCenter = "中央",
        PosCustom = "カスタム（ドラッグで移動）",
        Sticky = "固定（自動で閉じない）",
        AlwaysOnTop = "常に最前面",
        UpdateFrequency = "更新頻度",
        Sec30 = "30秒",
        Min1 = "1分",
        Min5 = "5分",
        Min15 = "15分",
        Notifications = "通知",
        Enabled = "有効",
        NotifyWhenReaching = "到達時に通知…",
        ColorThreshold = "色のしきい値",
        DefaultTag = "(既定)",
        Settings = "設定",
        ShowSpend = "推定コストを表示",
        StartWithWindows = "Windows 起動時に開始",
        EditConfig = "設定を編集（詳細）…",
        OpenDataFolder = "データフォルダーを開く",
        Language = "言語",
        SystemDefault = "システム既定",
        Exit = "終了",
        SessionWord = "セッション",
        WeekWord = "週間",
        ResetsIn = "リセットまで",
        Resetting = "リセット中…",
        RollingHint = "最初のリクエストからの5時間移動ウィンドウ",
        SpendHeaderFormat = "推定コスト（{0}日, API換算）",
        Loading = "読み込み中…",
        UpdatedAt = "更新",
        AgoFormat = "{0}前",
        StaleLabel = "古い",
        LocalSeal = "認証情報とデータはこの端末から外に出ません · テレメトリなし",
        HintClickToHide = "アイコンをクリックで非表示",
        HintPinnedClose = "固定中 · ✕ で閉じる",
        PreviousDataTip = "⚠ 以前のデータ（オフライン）",
        PreviousDataFooter = "以前のデータ",
        StateNoCredentials = "未ログイン — Claude Code にログイン",
        StateAuthExpired = "セッション期限切れ — Claude Code を開く",
        StateRateLimited = "レート制限 — 再試行中",
        StateNetworkError = "Anthropic に接続できません",
        NotifQuotaFormat = "Claude クォータを{0}%以上使用",
        Theme = "テーマ",
        ThemeSystem = "システム",
        ThemeDark = "ダーク",
        ThemeLight = "ライト",
        ThemeCli = "CLI",
        ThemeImported = "インポート済み",
        ImportTheme = ".itermcolors をインポート…",
        ShowServiceStatus = "サービス状態を表示",
        HealthOk = "正常",
        HealthDegraded = "一部障害",
        HealthOutage = "障害",
        UsageChart = "使用状況グラフ",
        NoData = "この範囲のデータなし",
        OpenBilling = "請求を開く…",
        ChartPeak = "最大",
        ChartTotal = "合計",
        ChartTabSpend = "コスト $",
        ChartTabPct = "クォータ %",
        Opacity = "不透明度",
        IconMode = "アイコン表示",
        PaceAlerts = "ペース通知",
        WinSession = "セッション(5h)",
        WinWeekly = "週間(7d)",
        PaceAlertTitle = "⚠ クォータのペース",
        PaceAlertBodyFmt = "このペースだと{0}のクォータは{1}頃に尽きます（リセット前）",
        MenuAppearance = "外観",
        MenuSections = "セクション",
        MenuIcon = "アイコン",
        MenuAdvanced = "詳細",
        About = "情報",
        CheckUpdates = "更新を確認",
        Checking = "更新を確認中…",
        UpdateAvailableFmt = "⬇ 更新あり {0}",
        UpToDateFmt = "最新です (v{0})。",
        UpdatePromptFmt = "現在のバージョン: v{0}\n最新: {1}\n\n今すぐダウンロードしますか？",
        UpdateDownloadedFmt = "ダウンロード先:\n{0}\n\nClaudeBar を閉じて、実行中の .exe をこのファイルに置き換えてください。",
        UpdateFailed = "更新の確認/ダウンロードに失敗しました。",
        MenuLiveSessions = "ライブセッション",
        MenuShowMascot = "マスコットを表示",
        MenuSuppressWhenFocused = "ターミナルがフォーカス中はミュート",
        MenuInstallHooks = "有効化（フックをインストール）…",
        MenuUninstallHooks = "無効化（フックを削除）",
        NoActiveSessions = "アクティブなセッションなし",
        SessionPhaseIdle = "待機中",
        SessionPhaseProcessing = "処理中",
        SessionPhaseWaitingApproval = "承認待ち",
        SessionPhaseWaitingInput = "あなたの番",
        SessionPhaseCompacting = "圧縮中",
        NotifWaitingApprovalFmt = "Claude が {0} で承認を待っています",
        NotifWaitingInputFmt = "Claude が {0} で完了しました",
        LiveSessionsTitle = "Claude セッション",
        HooksInstalledFmt = "ライブセッション オン。バックアップ: {0}",
        HooksRemoved = "ライブセッション オフ。フックを削除しました。",
        Changelog = "変更点"
    };

    private static readonly Strings Korean = new()
    {
        Dashboard = "대시보드",
        Refresh = "지금 새로고침",
        PanelWindow = "패널 창",
        Position = "위치",
        PosBottomRight = "오른쪽 아래",
        PosBottomLeft = "왼쪽 아래",
        PosTopRight = "오른쪽 위",
        PosTopLeft = "왼쪽 위",
        PosCenter = "가운데",
        PosCustom = "사용자 지정 (드래그)",
        Sticky = "고정 (자동으로 닫지 않음)",
        AlwaysOnTop = "항상 위에",
        UpdateFrequency = "새로고침 주기",
        Sec30 = "30초",
        Min1 = "1분",
        Min5 = "5분",
        Min15 = "15분",
        Notifications = "알림",
        Enabled = "사용",
        NotifyWhenReaching = "도달 시 알림…",
        ColorThreshold = "색상 임계값",
        DefaultTag = "(기본)",
        Settings = "설정",
        ShowSpend = "예상 비용 표시",
        StartWithWindows = "Windows 시작 시 실행",
        EditConfig = "구성 편집 (고급)…",
        OpenDataFolder = "데이터 폴더 열기",
        Language = "언어",
        SystemDefault = "시스템 기본값",
        Exit = "종료",
        SessionWord = "세션",
        WeekWord = "주간",
        ResetsIn = "재설정까지",
        Resetting = "재설정 중…",
        RollingHint = "첫 요청부터 5시간 이동 윈도우",
        SpendHeaderFormat = "예상 비용 ({0}일, API 환산)",
        Loading = "로딩 중…",
        UpdatedAt = "업데이트됨",
        AgoFormat = "{0} 전",
        StaleLabel = "오래됨",
        LocalSeal = "자격 증명과 데이터는 이 기기를 벗어나지 않습니다 · 텔레메트리 없음",
        HintClickToHide = "아이콘을 클릭하여 숨기기",
        HintPinnedClose = "고정됨 · ✕ 닫기",
        PreviousDataTip = "⚠ 이전 데이터 (오프라인)",
        PreviousDataFooter = "이전 데이터",
        StateNoCredentials = "로그인 안 됨 — Claude Code에 로그인",
        StateAuthExpired = "세션 만료 — Claude Code 열기",
        StateRateLimited = "요청 제한 — 재시도 중",
        StateNetworkError = "Anthropic에 연결할 수 없음",
        NotifQuotaFormat = "Claude 할당량 {0}%+ 사용",
        Theme = "테마",
        ThemeSystem = "시스템",
        ThemeDark = "어두움",
        ThemeLight = "밝음",
        ThemeCli = "CLI",
        ThemeImported = "가져옴",
        ImportTheme = ".itermcolors 가져오기…",
        ShowServiceStatus = "서비스 상태 표시",
        HealthOk = "정상",
        HealthDegraded = "일부 장애",
        HealthOutage = "장애",
        UsageChart = "사용량 그래프",
        NoData = "이 범위에 데이터 없음",
        OpenBilling = "청구 열기…",
        ChartPeak = "최대",
        ChartTotal = "합계",
        ChartTabSpend = "비용 $",
        ChartTabPct = "할당량 %",
        Opacity = "불투명도",
        IconMode = "아이콘 모드",
        PaceAlerts = "페이스 알림",
        WinSession = "세션(5h)",
        WinWeekly = "주간(7d)",
        PaceAlertTitle = "⚠ 할당량 페이스",
        PaceAlertBodyFmt = "이 페이스면 {0} 할당량이 {1}경 소진됩니다 (리셋 전)",
        MenuAppearance = "모양",
        MenuSections = "섹션",
        MenuIcon = "아이콘",
        MenuAdvanced = "고급",
        About = "정보",
        CheckUpdates = "업데이트 확인",
        Checking = "업데이트 확인 중…",
        UpdateAvailableFmt = "⬇ 업데이트 있음 {0}",
        UpToDateFmt = "최신 버전입니다 (v{0}).",
        UpdatePromptFmt = "현재 버전: v{0}\n최신: {1}\n\n지금 다운로드할까요?",
        UpdateDownloadedFmt = "다운로드 위치:\n{0}\n\nClaudeBar를 닫고 실행 중인 .exe를 이 파일로 교체하세요.",
        UpdateFailed = "업데이트 확인/다운로드에 실패했습니다.",
        MenuLiveSessions = "실시간 세션",
        MenuShowMascot = "마스코트 표시",
        MenuSuppressWhenFocused = "터미널이 포커스일 때 음소거",
        MenuInstallHooks = "활성화 (훅 설치)…",
        MenuUninstallHooks = "비활성화 (훅 제거)",
        NoActiveSessions = "활성 세션 없음",
        SessionPhaseIdle = "대기 중",
        SessionPhaseProcessing = "작업 중",
        SessionPhaseWaitingApproval = "승인 대기",
        SessionPhaseWaitingInput = "당신 차례",
        SessionPhaseCompacting = "압축 중",
        NotifWaitingApprovalFmt = "Claude가 {0}에서 승인을 기다립니다",
        NotifWaitingInputFmt = "Claude가 {0}에서 완료했습니다",
        LiveSessionsTitle = "Claude 세션",
        HooksInstalledFmt = "실시간 세션 켜짐. 백업: {0}",
        HooksRemoved = "실시간 세션 꺼짐. 훅 제거됨.",
        Changelog = "변경 사항"
    };

    private static readonly Strings TradChinese = new()
    {
        Dashboard = "儀表板",
        Refresh = "立即重新整理",
        PanelWindow = "面板視窗",
        Position = "位置",
        PosBottomRight = "右下",
        PosBottomLeft = "左下",
        PosTopRight = "右上",
        PosTopLeft = "左上",
        PosCenter = "置中",
        PosCustom = "自訂（拖曳面板）",
        Sticky = "釘選（不自動關閉）",
        AlwaysOnTop = "永遠在最上層",
        UpdateFrequency = "更新頻率",
        Sec30 = "30 秒",
        Min1 = "1 分鐘",
        Min5 = "5 分鐘",
        Min15 = "15 分鐘",
        Notifications = "通知",
        Enabled = "已啟用",
        NotifyWhenReaching = "達到時通知…",
        ColorThreshold = "顏色門檻",
        DefaultTag = "(預設)",
        Settings = "設定",
        ShowSpend = "顯示估計花費",
        StartWithWindows = "隨 Windows 啟動",
        EditConfig = "編輯設定（進階）…",
        OpenDataFolder = "開啟資料夾",
        Language = "語言",
        SystemDefault = "系統預設",
        Exit = "結束",
        SessionWord = "工作階段",
        WeekWord = "每週",
        ResetsIn = "重設於",
        Resetting = "重設中…",
        RollingHint = "從你第一次請求起算的 5 小時滾動視窗",
        SpendHeaderFormat = "估計花費（{0}天，API 約當）",
        Loading = "載入中…",
        UpdatedAt = "已更新",
        AgoFormat = "{0}前",
        StaleLabel = "過期",
        LocalSeal = "你的憑證與資料絕不離開本機 · 無遙測",
        HintClickToHide = "點擊圖示以隱藏",
        HintPinnedClose = "已釘選 · ✕ 關閉",
        PreviousDataTip = "⚠ 先前資料（離線）",
        PreviousDataFooter = "先前資料",
        StateNoCredentials = "未登入 — 請登入 Claude Code",
        StateAuthExpired = "工作階段過期 — 開啟 Claude Code",
        StateRateLimited = "請求受限 — 重試中",
        StateNetworkError = "無法連線到 Anthropic",
        NotifQuotaFormat = "Claude 已使用 {0}%+ 配額",
        Theme = "主題",
        ThemeSystem = "系統",
        ThemeDark = "深色",
        ThemeLight = "淺色",
        ThemeCli = "CLI",
        ThemeImported = "已匯入",
        ImportTheme = "匯入 .itermcolors…",
        ShowServiceStatus = "顯示服務狀態",
        HealthOk = "正常運作",
        HealthDegraded = "部分異常",
        HealthOutage = "中斷",
        UsageChart = "使用量圖表",
        NoData = "此範圍沒有資料",
        OpenBilling = "開啟帳單…",
        ChartPeak = "最高",
        ChartTotal = "總計",
        ChartTabSpend = "花費 $",
        ChartTabPct = "配額 %",
        Opacity = "不透明度",
        IconMode = "圖示模式",
        PaceAlerts = "用量速度提醒",
        WinSession = "工作階段(5h)",
        WinWeekly = "每週(7d)",
        PaceAlertTitle = "⚠ 配額速度",
        PaceAlertBodyFmt = "照這個速度，你的{0}配額會在 {1} 用完（重設前）",
        MenuAppearance = "外觀",
        MenuSections = "區段",
        MenuIcon = "圖示",
        MenuAdvanced = "進階",
        About = "關於",
        CheckUpdates = "檢查更新",
        Checking = "正在檢查更新…",
        UpdateAvailableFmt = "⬇ 有更新 {0}",
        UpToDateFmt = "已是最新版本 (v{0})。",
        UpdatePromptFmt = "目前版本：v{0}\n最新：{1}\n\n要立即下載嗎？",
        UpdateDownloadedFmt = "已下載到:\n{0}\n\n關閉 ClaudeBar 並用此檔案取代執行中的 .exe。",
        UpdateFailed = "無法檢查／下載更新。",
        MenuLiveSessions = "即時工作階段",
        MenuShowMascot = "顯示吉祥物",
        MenuSuppressWhenFocused = "終端機聚焦時靜音",
        MenuInstallHooks = "啟用（安裝 hooks）…",
        MenuUninstallHooks = "停用（移除 hooks）",
        NoActiveSessions = "沒有作用中的工作階段",
        SessionPhaseIdle = "閒置",
        SessionPhaseProcessing = "處理中",
        SessionPhaseWaitingApproval = "等待你的同意",
        SessionPhaseWaitingInput = "輪到你了",
        SessionPhaseCompacting = "壓縮中",
        NotifWaitingApprovalFmt = "Claude 正在 {0} 等待你的同意",
        NotifWaitingInputFmt = "Claude 已在 {0} 完成",
        LiveSessionsTitle = "Claude 工作階段",
        HooksInstalledFmt = "即時工作階段已開啟。備份：{0}",
        HooksRemoved = "即時工作階段已關閉。已移除 hooks。",
        Changelog = "更新內容"
    };
}
