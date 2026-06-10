using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class ThemeTokenTests
{
    public static IEnumerable<object[]> Themes =>
        new[] { new object[] { Theme.Dark }, new object[] { Theme.Light }, new object[] { Theme.Cli } };

    [Theory]
    [MemberData(nameof(Themes))]
    public void All_tokens_are_opaque(Theme t)
    {
        var tokens = new[] { t.TextPrimary, t.TextSecondary, t.TextMuted, t.Separator, t.Track,
                             t.BgBase, t.BgElevated, t.Accent, t.Ok, t.Warn, t.Critical, t.Neutral };
        Assert.All(tokens, c => Assert.True(c.A > 0, "token transparente/sin setear"));
    }

    [Fact]
    public void Dark_accent_is_claude_orange()
        => Assert.Equal(Color.FromArgb(0xCC, 0x78, 0x5C), Theme.Dark.Accent);

    [Fact]
    public void Dark_has_two_background_levels()
        => Assert.NotEqual(Theme.Dark.BgBase, Theme.Dark.BgElevated);

    [Fact]
    public void Semantic_aliases_map_to_existing_fields()
    {
        Assert.Equal(Theme.Dark.Foreground, Theme.Dark.TextPrimary);
        Assert.Equal(Theme.Dark.Dim, Theme.Dark.TextSecondary);
        Assert.Equal(Theme.Dark.Background, Theme.Dark.BgBase);
    }

    // --- T9: contraste del tema claro (auditoría visual: gris tenue y verdes ilegibles) ---

    [Fact]
    public void Light_text_muted_meets_aa_small_text_contrast()
    {
        // WCAG AA para texto pequeño exige ≥ 4.5:1. El valor anterior (#8E8E93) caía a ~3.1:1.
        double r = ColorMath.ContrastRatio(Theme.Light.TextMuted, Theme.Light.Background);
        Assert.True(r >= 4.5, $"TextMuted del tema claro contrasta {r:0.00}:1 (< 4.5)");
    }

    [Fact]
    public void Light_success_green_meets_aa_small_text_contrast()
    {
        // El verde de éxito (Ok) se usa también como texto pequeño (línea de salud, badges). El valor
        // anterior (#16A34A) caía a ~3.2:1 sobre el fondo claro.
        double r = ColorMath.ContrastRatio(Theme.Light.Ok, Theme.Light.Background);
        Assert.True(r >= 4.5, $"verde de éxito del tema claro contrasta {r:0.00}:1 (< 4.5)");
    }

    [Fact]
    public void Light_secondary_text_keeps_aa_contrast()
    {
        // No regresar el gris secundario (#71717A) que ya cumplía (~4.6:1).
        double r = ColorMath.ContrastRatio(Theme.Light.TextSecondary, Theme.Light.Background);
        Assert.True(r >= 4.5, $"TextSecondary del tema claro contrasta {r:0.00}:1 (< 4.5)");
    }

    // --- v0.3.5 P1 #3: subir el contraste donde fallaba (texto pequeño AA ≥ 4.5:1) ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_muted_meets_aa_small_text_contrast_in_every_theme(Theme t)
    {
        // El gris/verde tenue (subtítulos, footer, verbo de mascota) debe ser legible en TODOS los temas.
        // El CLI tenía #006E2C (~3.3:1 sobre negro) → ilegible; ahora #00963C (~5.4:1). Un único token
        // TextMuted por tema que cumple AA.
        double r = ColorMath.ContrastRatio(t.TextMuted, t.Background);
        Assert.True(r >= 4.5, $"[{t.Id}] TextMuted contrasta {r:0.00}:1 (< 4.5)");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Accent_as_text_meets_aa_small_text_contrast_in_every_theme(Theme t)
    {
        // El acento usado como TEXTO/borde fino (botón "Importar tema") debe cumplir AA. El naranja de
        // relleno (#CC785C) caía a ~3.1:1 como texto sobre fondo claro → AccentText lo oscurece (#A84B33).
        double r = ColorMath.ContrastRatio(t.AccentText, t.Background);
        Assert.True(r >= 4.5, $"[{t.Id}] AccentText contrasta {r:0.00}:1 (< 4.5)");
    }

    [Fact]
    public void AccentText_falls_back_to_accent_when_not_overridden()
    {
        // En oscuro/CLI el acento ya es legible como texto → AccentText == Accent (sin override).
        Assert.Equal(Theme.Dark.Accent, Theme.Dark.AccentText);
        Assert.Equal(Theme.Cli.Accent, Theme.Cli.AccentText);
        // El tema claro SÍ lo oscurece (override presente).
        Assert.NotEqual(Theme.Light.Accent, Theme.Light.AccentText);
    }

    // --- T3b: ticks de umbral de QuotaBar invisibles (Separator ≈ Track en los 3 temas) ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Tick_on_track_meets_non_text_contrast_in_every_theme(Theme t)
    {
        // WCAG 1.4.11 (contraste no textual) exige ≥3:1 para indicadores gráficos. Los ticks de umbral
        // usaban Separator, ≈ idéntico al Track en los 3 temas (CLI: exacto, ~1:1) → invisibles.
        double r = ColorMath.ContrastRatio(t.TickOnTrack, t.Track);
        Assert.True(r >= 3.0, $"[{t.Id}] TickOnTrack contrasta {r:0.00}:1 (< 3.0) sobre Track");
    }

    [Fact]
    public void TickOnTrack_falls_back_to_text_muted()
    {
        // Sin override, el token cae a TextMuted (que ya cumple ≥3:1 sobre Track en los 3 temas);
        // los temas importados heredan el fallback sin tener que mapear un campo nuevo.
        Assert.Equal(Theme.Dark.TextMuted, Theme.Dark.TickOnTrack);
        Assert.Equal(Theme.Light.TextMuted, Theme.Light.TickOnTrack);
        Assert.Equal(Theme.Cli.TextMuted, Theme.Cli.TickOnTrack);
    }
}
