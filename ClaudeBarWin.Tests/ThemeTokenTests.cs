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
                             t.BgBase, t.BgElevated, t.Accent, t.Ok, t.Warn, t.Critical, t.Neutral,
                             t.AccentText, t.TickOnTrack, t.WarnText, t.CriticalText, t.HoverBg };
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

    // --- T6b: tokens de TEXTO Warn/Critical (patrón AccentText) + barrido global AA ≥ 4.5:1 ---

    public static IEnumerable<object[]> TextTokens()
    {
        foreach (var t in new[] { Theme.Dark, Theme.Light, Theme.Cli })
            foreach (var (name, c) in new (string, Color)[]
            {
                ("TextPrimary", t.TextPrimary), ("TextSecondary", t.TextSecondary),
                ("TextMuted", t.TextMuted), ("AccentText", t.AccentText),
                ("Ok(texto)", t.Ok),         // el verde Ok se usa directo como texto (pace, %, salud)
                ("WarnText", t.WarnText), ("CriticalText", t.CriticalText),
            })
                yield return new object[] { t, name, c };
    }

    [Theory]
    [MemberData(nameof(TextTokens))]
    public void Every_text_token_meets_aa_small_text_contrast(Theme t, string name, Color c)
    {
        // Barrido global (T6b): TODO color que la UI pinta como texto pequeño debe cumplir AA (≥4.5:1)
        // sobre el fondo del panel, en los 3 temas. Antes fallaban Light.Warn (2.8:1) y Dark.Critical
        // (3.7:1) — el texto más crítico del panel era el de peor contraste.
        double r = ColorMath.ContrastRatio(c, t.Background);
        Assert.True(r >= 4.5, $"[{t.Id}] {name} contrasta {r:0.00}:1 (< 4.5) sobre el fondo");
    }

    [Fact]
    public void WarnText_falls_back_to_warn_and_only_light_overrides()
    {
        // En oscuro (5.6:1) y CLI (12.3:1) el ámbar de relleno ya es legible como texto → sin override.
        Assert.Equal(Theme.Dark.Warn, Theme.Dark.WarnText);
        Assert.Equal(Theme.Cli.Warn, Theme.Cli.WarnText);
        // El claro SÍ lo oscurece (#CA8A04 como texto = 2.8:1).
        Assert.NotEqual(Theme.Light.Warn, Theme.Light.WarnText);
    }

    [Fact]
    public void CriticalText_falls_back_to_critical_and_only_dark_overrides()
    {
        // En claro (4.6:1) y CLI (5.6:1) el rojo de relleno ya es legible como texto → sin override.
        Assert.Equal(Theme.Light.Critical, Theme.Light.CriticalText);
        Assert.Equal(Theme.Cli.Critical, Theme.Cli.CriticalText);
        // El oscuro SÍ lo aclara (#DC2626 como texto = 3.7:1).
        Assert.NotEqual(Theme.Dark.Critical, Theme.Dark.CriticalText);
    }

    [Fact]
    public void Status_fill_tokens_do_not_change_with_t6()
    {
        // T6b toca SOLO las variantes de texto: los rellenos Warn/Critical (barras, dots, badges)
        // conservan sus valores. Pin de regresión del "fills no cambian".
        Assert.Equal(Color.FromArgb(202, 138, 4), Theme.Light.Warn);
        Assert.Equal(Color.FromArgb(220, 38, 38), Theme.Dark.Critical);
    }

    // --- T7b: token HoverBg (el realce con BgElevated puro era invisible en claro y CLI) ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Hover_bg_is_perceptibly_distinct_from_background_in_every_theme(Theme t)
    {
        // El realce de hover es decoración transitoria (no necesita el 3:1 de 1.4.11), pero SÍ debe
        // ser perceptible. BgElevated puro daba claro #FFF/#FAFAFA ≈ 1.03:1 y CLI #0A100A/negro ≈
        // 1.05:1 (invisibles); el token nuevo debe levantar ≥ ~1.1:1 del fondo en los 3 temas.
        Assert.NotEqual(t.Background.ToArgb(), t.HoverBg.ToArgb());
        double r = ColorMath.ContrastRatio(t.HoverBg, t.Background);
        Assert.True(r >= 1.10, $"[{t.Id}] HoverBg contrasta {r:0.000}:1 (< 1.10) sobre el fondo: hover invisible");
    }

    [Fact]
    public void HoverBg_falls_back_to_bg_fg_lerp_and_only_cli_overrides()
    {
        // Sin override el token cae a lerp(Background, Foreground, 0.06) — también para los temas
        // importados, sin mapear un campo nuevo. El CLI sube la mezcla (a 0.06 el verde casi no
        // levanta del negro puro: ~1.06:1).
        Assert.Equal(ColorMath.Lerp(Theme.Dark.Background, Theme.Dark.Foreground, 0.06), Theme.Dark.HoverBg);
        Assert.Equal(ColorMath.Lerp(Theme.Light.Background, Theme.Light.Foreground, 0.06), Theme.Light.HoverBg);
        Assert.NotEqual(ColorMath.Lerp(Theme.Cli.Background, Theme.Cli.Foreground, 0.06), Theme.Cli.HoverBg);
    }

    // --- T9b: knob del TogglePill blanco con borde sutil (§3 #11: "agujero perforado") ---

    [Theory]
    [MemberData(nameof(Themes))]
    public void Knob_fill_is_white_in_every_theme(Theme t)
    {
        // Estándar iOS/Fluent: knob BLANCO en todos los temas. El histórico usaba el color del texto
        // (negro sobre el track claro = "agujero perforado") o ColorMath.Contrast(Accent).
        Assert.Equal(Color.White.ToArgb(), t.KnobFill.ToArgb());
    }

    [Fact]
    public void Knob_border_is_a_translucent_scrim()
    {
        // El borde es un velo negro translúcido: delimita el círculo blanco sobre CUALQUIER track
        // (claro u oscuro) sin introducir un color opaco nuevo por tema.
        var b = Theme.Dark.KnobBorder;
        Assert.True(b.A > 0 && b.A < 255, $"el borde del knob debe ser translúcido (alpha {b.A})");
        Assert.Equal(Theme.Light.KnobBorder, Theme.Dark.KnobBorder);
        Assert.Equal(Theme.Cli.KnobBorder, Theme.Dark.KnobBorder);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Knob_fill_meets_non_text_contrast_on_accent_track_or_border_compensates(Theme t)
    {
        // Sobre el track ON (Accent) el knob blanco da ~3.3:1 en oscuro/claro (≥3:1, WCAG 1.4.11).
        // En CLI (verde #00D959) cae a ~1.9:1: ahí delimita el borde KnobBorder y el estado lo
        // comunican posición + color del track. Documentamos el suelo real (~1.9:1) como pin.
        double r = ColorMath.ContrastRatio(t.KnobFill, t.Accent);
        Assert.True(r >= 1.85, $"[{t.Id}] knob sobre Accent contrasta {r:0.00}:1 (< 1.85)");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Pace_text_color_maps_to_text_variants(Theme t)
    {
        // El selector de color de TEXTO por estado de pace (QuotaBar %, glance del header, línea de
        // pace) usa las variantes AA, no los rellenos.
        Assert.Equal(t.CriticalText, Theme.PaceTextColor(t, PaceStatus.Critical));
        Assert.Equal(t.WarnText, Theme.PaceTextColor(t, PaceStatus.Over));
        Assert.Equal(t.Ok, Theme.PaceTextColor(t, PaceStatus.Ok));
    }
}
