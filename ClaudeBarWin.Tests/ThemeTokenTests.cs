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
}
