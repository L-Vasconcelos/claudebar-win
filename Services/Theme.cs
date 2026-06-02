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
        Ok = Color.FromArgb(22, 163, 74),
        Warn = Color.FromArgb(202, 138, 4),
        Critical = Color.FromArgb(220, 38, 38),
        Neutral = Color.FromArgb(161, 161, 170),
        Accent = Color.FromArgb(0xCC, 0x78, 0x5C),
        BgElevated = Color.FromArgb(255, 255, 255),
        TextMuted = Color.FromArgb(142, 142, 147),
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
        TextMuted = Color.FromArgb(0, 110, 44),
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
