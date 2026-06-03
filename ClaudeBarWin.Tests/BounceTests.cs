using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="Bounce"/> es PURO y elapsed-driven (regla de oro F3): el "bote" de atención de la
/// mascota es una traslación vertical <c>OffsetY(elapsedMs, amplitude, period, repeats)</c> que
/// arranca en 0, alcanza un pico positivo a media animación, vuelve a 0 al final y cuyos rebotes
/// sucesivos <b>decaen</b>. Usa <see cref="Easing.OutBack"/> (overshoot) para el "boing". Sin
/// reloj/aleatoriedad por dentro → tests deterministas. El llamante lo aplica vía
/// <c>g.TranslateTransform</c> dentro de la celda de la mascota: el <c>y</c> de layout NO cambia.
/// </summary>
public class BounceTests
{
    private const int Amp = 6;          // Motion.BounceAmplitudePx
    private const double Period = 420.0; // Motion.BouncePeriodMs
    private const int Repeats = 3;       // Motion.BounceRepeats

    [Fact]
    public void Starts_at_zero()
        => Assert.Equal(0, Bounce.OffsetY(0.0, Amp, Period, Repeats));

    [Fact]
    public void Returns_to_zero_at_and_after_the_end()
    {
        double end = Period * Repeats;
        Assert.Equal(0, Bounce.OffsetY(end, Amp, Period, Repeats));
        Assert.Equal(0, Bounce.OffsetY(end + 1000.0, Amp, Period, Repeats));
    }

    [Fact]
    public void Has_a_positive_peak_during_the_animation()
    {
        int maxOff = 0;
        for (double t = 0.0; t <= Period * Repeats; t += 5.0)
            maxOff = Math.Max(maxOff, Bounce.OffsetY(t, Amp, Period, Repeats));
        Assert.True(maxOff > 0, $"se esperaba un pico positivo, got {maxOff}");
    }

    [Fact]
    public void Peak_is_within_the_amplitude_envelope()
    {
        // El overshoot de OutBack puede pasar de la amplitud nominal, pero no debe dispararse:
        // se acota a un múltiplo razonable de la amplitud (envolvente del primer rebote).
        for (double t = 0.0; t <= Period * Repeats; t += 5.0)
        {
            int off = Bounce.OffsetY(t, Amp, Period, Repeats);
            Assert.InRange(off, 0, Amp * 2);
        }
    }

    [Fact]
    public void Later_rebounds_are_smaller_than_the_first()
    {
        // Pico del primer periodo vs pico del último periodo: el último decae (rebote más bajo).
        int PeakIn(double from, double to)
        {
            int m = 0;
            for (double t = from; t <= to; t += 2.0)
                m = Math.Max(m, Bounce.OffsetY(t, Amp, Period, Repeats));
            return m;
        }
        int first = PeakIn(0.0, Period);
        int last = PeakIn(Period * (Repeats - 1), Period * Repeats);
        Assert.True(last < first, $"se esperaba que el último rebote ({last}) decayera bajo el primero ({first})");
    }

    [Fact]
    public void Offset_is_never_negative()
    {
        // El bote es "hacia arriba" y vuelve a 0: nunca empuja por debajo de la línea base.
        for (double t = 0.0; t <= Period * Repeats; t += 3.0)
            Assert.True(Bounce.OffsetY(t, Amp, Period, Repeats) >= 0,
                $"offset negativo en t={t}");
    }

    [Fact]
    public void Negative_elapsed_is_clamped_to_zero()
        => Assert.Equal(0, Bounce.OffsetY(-50.0, Amp, Period, Repeats));

    [Fact]
    public void Is_deterministic_for_the_same_inputs()
    {
        // Mismo (elapsed, amplitud, periodo, repeats) ⇒ mismo offset (sin reloj/aleatoriedad).
        for (double t = 0.0; t <= Period * Repeats; t += 17.0)
            Assert.Equal(Bounce.OffsetY(t, Amp, Period, Repeats),
                         Bounce.OffsetY(t, Amp, Period, Repeats));
    }

    [Fact]
    public void Zero_or_negative_period_is_safe_and_flat()
    {
        // Degenerado: sin periodo no hay bote (no debe dividir por cero ni reventar).
        Assert.Equal(0, Bounce.OffsetY(100.0, Amp, 0.0, Repeats));
        Assert.Equal(0, Bounce.OffsetY(100.0, Amp, -10.0, Repeats));
        Assert.Equal(0, Bounce.OffsetY(100.0, Amp, Period, 0));
    }

    [Fact]
    public void Active_predicate_tracks_the_animation_window()
    {
        // Útil para alimentar al scheduler: ¿sigue el bote en vuelo?
        Assert.True(Bounce.IsActive(0.0, Period, Repeats));
        Assert.True(Bounce.IsActive(Period, Period, Repeats));
        Assert.False(Bounce.IsActive(Period * Repeats, Period, Repeats));
        Assert.False(Bounce.IsActive(Period * Repeats + 1.0, Period, Repeats));
        Assert.False(Bounce.IsActive(-1.0, Period, Repeats));
    }
}
