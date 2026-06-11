using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeBarWin.Config;

/// <summary>
/// User-editable settings, persisted to %APPDATA%\ClaudeBarWin\config.json.
/// Thresholds are in PERCENT (0-100) to match Anthropic's utilization values.
/// </summary>
public sealed class AppConfig
{
    /// <summary>"system" = follow Windows UI language; otherwise a code: en, es, nl, fr, de, ja, ko, zh-Hant.</summary>
    public string Language { get; set; } = "system";

    /// <summary>"system" = follow Windows light/dark; otherwise: dark, light, cli, imported.</summary>
    public string Theme { get; set; } = "system";
    public ImportedThemeColors? ImportedTheme { get; set; }

    /// <summary>Show the Anthropic service-health indicator in the dashboard.</summary>
    public bool ShowHealth { get; set; } = true;

    /// <summary>Show the usage chart inside the dashboard.</summary>
    public bool ShowChart { get; set; } = true;
    /// <summary>Chart metric: "spend" ($ equiv, by model) or "percent" (real utilization).</summary>
    public string ChartMode { get; set; } = "spend";
    /// <summary>In percent mode, which window to plot: "5h" or "7d".</summary>
    public string ChartPctWindow { get; set; } = "7d";

    /// <summary>Tray icon content: "percent" (utilization), "pace" (ritmo), or "both".</summary>
    public string IconDisplayMode { get; set; } = "percent";
    /// <summary>Proactive notification when a window is projected to exhaust before its reset.</summary>
    public bool PaceAlerts { get; set; } = true;

    public int RefreshSeconds { get; set; } = 60;
    public double WarnThresholdPct { get; set; } = 70;
    public double CriticalThresholdPct { get; set; } = 90;
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Utilization milestones (%) that trigger a notification when first crossed.</summary>
    public int[] NotifyMilestones { get; set; } = { 25, 50, 75, 95 };

    /// <summary>Show the local "estimated spend by model" section (ccusage-style).</summary>
    public bool ShowSpendEstimate { get; set; } = true;
    public int SpendWindowDays { get; set; } = 7;

    /// <summary>
    /// T13b: refrescar el catálogo de tarifas desde models.dev (GET anónimo de precios públicos; no
    /// sale NINGÚN dato del usuario — el sello de privacidad del footer sigue siendo verdad). OFF ⇒
    /// solo caché local + snapshot embebido. Default ON.
    /// </summary>
    public bool PricingOnlineUpdate { get; set; } = true;

    // Dashboard window placement & behaviour
    /// <summary>BottomRight | BottomLeft | TopRight | TopLeft | Center | Custom</summary>
    public string DashboardPosition { get; set; } = "BottomRight";
    public int DashboardX { get; set; } = -1;
    public int DashboardY { get; set; } = -1;
    /// <summary>Sticky: keep the dashboard open when it loses focus.</summary>
    public bool DashboardSticky { get; set; } = false;
    /// <summary>Keep the dashboard above other windows.</summary>
    public bool DashboardAlwaysOnTop { get; set; } = true;
    /// <summary>Dashboard window opacity (0.3–1.0).</summary>
    public double DashboardOpacity { get; set; } = 1.0;

    // Live sessions (hook de Claude Code -> Named Pipe)
    /// <summary>Interruptor maestro de la feature de sesiones en vivo (listener del pipe + mascota + lista).</summary>
    public bool LiveSessionsEnabled { get; set; } = false;
    /// <summary>Mostrar la mascota ASCII que reacciona a la fase global de las sesiones.</summary>
    public bool ShowMascot { get; set; } = true;
    /// <summary>No avisar mientras una ventana de Claude Code/terminal sea la del primer plano.</summary>
    public bool SuppressWhenFocused { get; set; } = true;
    /// <summary>Bestiario de la mascota a renderizar (de momento solo "cat").</summary>
    public string MascotKind { get; set; } = "cat";

    // Dashboard layout (v0.3)
    // (MascotSize se retiró en v0.3.7: solo queda la mascota compacta. Valores viejos en
    //  config.json se ignoran al deserializar.)
    /// <summary>Sección Cuota plegada en el dashboard.</summary>
    public bool CollapsedQuota { get; set; } = false;
    /// <summary>Sección Sesiones plegada.</summary>
    public bool CollapsedSessions { get; set; } = false;
    /// <summary>Sección Gasto plegada (por defecto sí, para un panel compacto).</summary>
    public bool CollapsedSpend { get; set; } = true;
    /// <summary>Sección Gráfica plegada (por defecto sí).</summary>
    public bool CollapsedChart { get; set; } = true;

    // Accesibilidad / microinteracciones (v0.3 F3)
    /// <summary>
    /// Reducir movimiento: colapsa TODA animación a su estado final (sin fade/tween/stagger/bounce,
    /// mascota sin spinner/jitter). Default <c>false</c> = animaciones ON (decisión de Yovan). El gate
    /// es ÚNICO; el default NO depende del SO (existe <see cref="ClaudeBarWin.Services.Motion.MotionPrefs"/>
    /// para una futura opción "seguir Windows").
    /// </summary>
    public bool ReduceMotion { get; set; } = false;

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeBarWin", "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                if (cfg is not null) return cfg;
            }
        }
        catch
        {
            // fall through to defaults
        }

        var def = new AppConfig();
        def.Save();
        return def;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // non-fatal
        }
    }
}

/// <summary>Theme colours imported from an .itermcolors file, stored as #RRGGBB hex.</summary>
public sealed class ImportedThemeColors
{
    public string? Bg { get; set; }
    public string? Fg { get; set; }
    public string? Dim { get; set; }
    public string? Track { get; set; }
    public string? Ok { get; set; }
    public string? Warn { get; set; }
    public string? Critical { get; set; }
}
