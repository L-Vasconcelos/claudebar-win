using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services;

/// <summary>Colour palette used by the dashboard and tray icon.</summary>
public sealed class Theme
{
    public required string Id { get; init; }
    public Color Background { get; init; }
    public Color Foreground { get; init; }
    public Color Dim { get; init; }
    public Color Track { get; init; }
    public Color Ok { get; init; }
    public Color Warn { get; init; }
    public Color Critical { get; init; }
    public Color Neutral { get; init; }

    // --- Tokens semánticos (Fase 1) ---
    public Color Accent { get; init; }
    public Color BgElevated { get; init; }
    public Color TextMuted { get; init; }
    public Color Separator { get; init; }

    /// <summary>
    /// Variante del acento legible cuando se usa como TEXTO/borde fino (no como relleno). El
    /// <see cref="Accent"/> de relleno lleva texto <see cref="ColorMath.Contrast"/> encima y contrasta
    /// bien; pero pintado como texto sobre el fondo del panel puede caer por debajo de AA (el naranja
    /// Claude sobre fondo claro daba ~3.1:1). Este token oscurece el acento lo justo para texto pequeño
    /// (≥4.5:1) manteniendo el tinte. En oscuro/CLI coincide con <see cref="Accent"/> (ya legible);
    /// solo el tema claro lo oscurece. Si no se setea, cae a <see cref="Accent"/>.
    /// </summary>
    public Color? AccentTextOverride { get; init; }
    public Color AccentText => AccentTextOverride ?? Accent;

    /// <summary>
    /// Color de las muescas de umbral (warn/critical) sobre el <see cref="Track"/> de las barras de
    /// cuota (T3b). El valor histórico (<see cref="Separator"/>) era ≈ idéntico al Track en los 3 temas
    /// (CLI: exactamente el mismo color, ~1:1) → ticks invisibles. Cae a <see cref="TextMuted"/>, que
    /// cumple el contraste no textual WCAG 1.4.11 (≥3:1) sobre el Track en los 3 temas:
    /// oscuro #8E8E93/#3A3A3C ≈ 3.4:1 · claro #6C6C72/#D4D4D8 ≈ 3.5:1 · CLI #00963C/#003214 ≈ 3.7:1.
    /// Los temas importados heredan el fallback (su TextMuted); override disponible si un tema lo necesita.
    /// </summary>
    public Color? TickOnTrackOverride { get; init; }
    public Color TickOnTrack => TickOnTrackOverride ?? TextMuted;

    // Alias semánticos sobre los campos existentes (sin romper consumidores).
    public Color TextPrimary => Foreground;
    public Color TextSecondary => Dim;
    public Color BgBase => Background;

    public static readonly Theme Dark = new()
    {
        Id = "dark",
        Background = Color.FromArgb(24, 24, 27),
        Foreground = Color.FromArgb(244, 244, 245),
        Dim = Color.FromArgb(161, 161, 170),
        Track = Color.FromArgb(58, 58, 60),          // #3A3A3C (antes 63,63,70)
        Ok = Color.FromArgb(22, 163, 74),
        Warn = Color.FromArgb(217, 119, 6),
        Critical = Color.FromArgb(220, 38, 38),
        Neutral = Color.FromArgb(82, 82, 91),
        Accent = Color.FromArgb(0xCC, 0x78, 0x5C),    // naranja Claude
        BgElevated = Color.FromArgb(44, 44, 46),      // #2C2C2E
        TextMuted = Color.FromArgb(142, 142, 147),    // #8E8E93
        Separator = Color.FromArgb(56, 56, 58)        // #38383A
    };

    public static readonly Theme Light = new()
    {
        Id = "light",
        Background = Color.FromArgb(250, 250, 250),
        Foreground = Color.FromArgb(24, 24, 27),
        Dim = Color.FromArgb(113, 113, 122),
        Track = Color.FromArgb(212, 212, 216),
        // Verde de éxito oscurecido (#15803D, green-700): el anterior #16A34A caía a ~3.2:1 sobre el
        // fondo claro (texto pequeño ilegible: línea de salud, badges). Ahora ~4.8:1 (AA, T9).
        Ok = Color.FromArgb(21, 128, 61),
        Warn = Color.FromArgb(202, 138, 4),
        Critical = Color.FromArgb(220, 38, 38),
        Neutral = Color.FromArgb(161, 161, 170),
        Accent = Color.FromArgb(0xCC, 0x78, 0x5C),
        // Acento-como-texto oscurecido (#A84B33): el naranja de relleno (#CC785C) sobre el fondo claro
        // caía a ~3.1:1 como texto/borde (botón "Importar tema" ilegible, P1 #3). Este tono mantiene el
        // tinte Claude y sube a ~5.4:1 (AA texto pequeño). Solo el tema claro lo necesita.
        AccentTextOverride = Color.FromArgb(0xA8, 0x4B, 0x33),
        BgElevated = Color.FromArgb(255, 255, 255),
        // Gris tenue subido (#6C6C72): el anterior #8E8E93 caía a ~3.1:1 sobre el fondo claro. Ahora
        // ~5.0:1 (AA, T9) — antes era el mismo valor que en oscuro pese a fondos opuestos.
        TextMuted = Color.FromArgb(108, 108, 114),
        Separator = Color.FromArgb(209, 209, 214)
    };

    public static readonly Theme Cli = new()
    {
        Id = "cli",
        Background = Color.FromArgb(0, 0, 0),
        Foreground = Color.FromArgb(0, 217, 89),
        Dim = Color.FromArgb(0, 140, 56),
        Track = Color.FromArgb(0, 50, 20),
        Ok = Color.FromArgb(0, 217, 89),
        Warn = Color.FromArgb(242, 191, 51),
        Critical = Color.FromArgb(242, 64, 64),
        Neutral = Color.FromArgb(90, 90, 90),
        Accent = Color.FromArgb(0, 217, 89),
        BgElevated = Color.FromArgb(10, 16, 10),
        // Verde tenue subido (#00963C): el anterior #006E2C caía a ~3.3:1 sobre el negro del CLI
        // (subtítulos/footer/verbo de mascota ilegibles, P1 #3). Ahora ~5.4:1 (AA texto pequeño).
        TextMuted = Color.FromArgb(0, 0x96, 0x3C),
        Separator = Color.FromArgb(0, 50, 20)
    };

    public static Color StatusColor(Theme t, UI.UsageStatus s) => s switch
    {
        UI.UsageStatus.Critical => t.Critical,
        UI.UsageStatus.Warn => t.Warn,
        _ => t.Ok
    };
}

public static class ThemeResolver
{
    public static Theme Resolve(AppConfig cfg)
    {
        string id = string.IsNullOrEmpty(cfg.Theme) ? "system" : cfg.Theme;
        if (id == "system")
            id = OsPrefersDark() ? "dark" : "light";

        return id switch
        {
            "light" => Theme.Light,
            "cli" => Theme.Cli,
            "imported" => cfg.ImportedTheme is { } it ? FromImported(it) : Theme.Dark,
            _ => Theme.Dark
        };
    }

    /// <summary>Reads Windows' app theme preference (Settings → Personalisation → Colours).</summary>
    public static bool OsPrefersDark()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return v is int i && i == 0;
        }
        catch
        {
            return true; // default to dark
        }
    }

    /// <summary>
    /// Reads Windows' <b>taskbar</b> theme preference (<c>SystemUsesLightTheme</c>) — distinta de
    /// <see cref="OsPrefersDark"/>, que lee el tema de las <i>apps</i> (<c>AppsUseLightTheme</c>).
    /// Determina si la barra de tareas es clara, para que el badge del tray adapte su contraste.
    /// Con fallback: si no se puede leer el registro, asume barra oscura (false).
    /// </summary>
    public static bool TaskbarIsLight()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0);
            return v is int i && i == 1;
        }
        catch
        {
            return false; // default to dark taskbar
        }
    }

    private static Theme FromImported(ImportedThemeColors c) => new()
    {
        Id = "imported",
        Background = Hex(c.Bg, Theme.Dark.Background),
        Foreground = Hex(c.Fg, Theme.Dark.Foreground),
        Dim = Hex(c.Dim, Theme.Dark.Dim),
        Track = Hex(c.Track, Theme.Dark.Track),
        Ok = Hex(c.Ok, Theme.Dark.Ok),
        Warn = Hex(c.Warn, Theme.Dark.Warn),
        Critical = Hex(c.Critical, Theme.Dark.Critical),
        Neutral = Theme.Dark.Neutral,
        Accent = Theme.Dark.Accent,
        BgElevated = ColorMath.Lerp(Hex(c.Bg, Theme.Dark.Background), Color.White, 0.06),
        TextMuted = Hex(c.Dim, Theme.Dark.TextMuted),
        Separator = Hex(c.Track, Theme.Dark.Separator)
    };

    private static Color Hex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6)
                return Color.FromArgb(
                    Convert.ToInt32(h.Substring(0, 2), 16),
                    Convert.ToInt32(h.Substring(2, 2), 16),
                    Convert.ToInt32(h.Substring(4, 2), 16));
        }
        catch { }
        return fallback;
    }
}
