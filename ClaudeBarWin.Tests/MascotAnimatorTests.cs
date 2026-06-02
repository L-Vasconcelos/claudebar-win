using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="MascotAnimator"/> es PURO y elapsed-driven (regla de oro F3): dado
/// (fase, <c>elapsedMsEnFase</c>, semilla) devuelve <c>{ frameIndex, blinking, spinnerGlyph,
/// verbIndex }</c>. Sin <c>Random</c> ni reloj por dentro — el jitter del parpadeo es un hash de
/// un contador, así el resultado es determinista para la misma semilla. Tono juguetón: blink
/// esporádico (no en cada tick), spinner que cicla en Processing/Compacting, verbo dentro del
/// pool de la fase.
/// </summary>
public class MascotAnimatorTests
{
    private static MascotState Sample(SessionPhase phase, double elapsedMs, int seed = 0)
        => MascotAnimator.Sample(phase, elapsedMs, seed);

    // --- frameIndex válido -------------------------------------------------

    [Fact]
    public void FrameIndex_is_always_within_the_sprite_count()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            int count = MascotSprite.Frames(p, MascotSize.Large).Count;
            for (double t = 0; t <= 6000; t += 37)
            {
                var st = Sample(p, t, seed: 3);
                Assert.InRange(st.FrameIndex, 0, count - 1);
            }
        }
    }

    [Fact]
    public void Static_phases_never_pick_a_second_frame()
    {
        // Idle y Ended tienen un único frame (estáticos en el bestiario): frameIndex siempre 0.
        foreach (var p in new[] { SessionPhase.Idle, SessionPhase.Ended })
            for (double t = 0; t <= 5000; t += 50)
                Assert.Equal(0, Sample(p, t).FrameIndex);
    }

    // --- blink esporádico + determinista -----------------------------------

    [Fact]
    public void Idle_blink_is_sporadic_not_every_tick()
    {
        // En Idle el parpadeo es ocasional: a lo largo de una ventana larga la MAYORÍA de los
        // instantes NO están parpadeando (si parpadeara siempre no sería un parpadeo).
        int blinks = 0, total = 0;
        for (double t = 0; t <= 20000; t += 33)
        {
            total++;
            if (Sample(SessionPhase.Idle, t, seed: 7).Blinking) blinks++;
        }
        Assert.True(blinks > 0, "debería parpadear alguna vez en 20 s");
        Assert.True(blinks < total / 2, $"el parpadeo no debe ser constante: {blinks}/{total}");
    }

    [Fact]
    public void Blink_is_deterministic_for_the_same_seed()
    {
        for (double t = 0; t <= 8000; t += 41)
        {
            var a = Sample(SessionPhase.Idle, t, seed: 12);
            var b = Sample(SessionPhase.Idle, t, seed: 12);
            Assert.Equal(a.Blinking, b.Blinking);
            Assert.Equal(a.FrameIndex, b.FrameIndex);
            Assert.Equal(a.VerbIndex, b.VerbIndex);
            Assert.Equal(a.SpinnerGlyph, b.SpinnerGlyph);
        }
    }

    [Fact]
    public void Different_seeds_can_blink_at_different_times()
    {
        // El jitter depende de la semilla: dos semillas distintas no parpadean idénticamente
        // (al menos en un instante difieren) → confirma que la semilla influye en el jitter.
        bool anyDiff = false;
        for (double t = 0; t <= 20000 && !anyDiff; t += 33)
        {
            if (Sample(SessionPhase.Idle, t, seed: 1).Blinking != Sample(SessionPhase.Idle, t, seed: 99).Blinking)
                anyDiff = true;
        }
        Assert.True(anyDiff, "el jitter debería variar con la semilla");
    }

    // --- spinner de glifos -------------------------------------------------

    [Fact]
    public void Processing_spinner_cycles_through_a_glyph_sequence()
    {
        // En Processing el spinner avanza por su secuencia: a lo largo del tiempo aparece más de
        // un glifo distinto, y todos pertenecen a la secuencia canónica.
        var seen = new HashSet<char>();
        for (double t = 0; t <= 4000; t += 80)
        {
            char g = Sample(SessionPhase.Processing, t).SpinnerGlyph;
            Assert.NotEqual('\0', g);
            Assert.Contains(g, MascotAnimator.SpinnerSequence);
            seen.Add(g);
        }
        Assert.True(seen.Count > 1, "el spinner debe ciclar por varios glifos");
    }

    [Fact]
    public void Compacting_also_has_a_spinner()
    {
        Assert.NotEqual('\0', Sample(SessionPhase.Compacting, 100).SpinnerGlyph);
    }

    [Fact]
    public void Non_working_phases_have_no_spinner()
    {
        // Idle / WaitingFor* / Ended no llevan spinner (es señal de "trabajo vivo").
        foreach (var p in new[] { SessionPhase.Idle, SessionPhase.WaitingForApproval,
                                  SessionPhase.WaitingForInput, SessionPhase.Ended })
            Assert.Equal('\0', Sample(p, 200).SpinnerGlyph);
    }

    [Fact]
    public void Spinner_advances_monotonically_in_phase_order()
    {
        // El índice del spinner crece con el tiempo (módulo la longitud de la secuencia): a t
        // mayor el glifo cambia respecto a t=0 en algún punto del primer ciclo.
        char g0 = Sample(SessionPhase.Processing, 0).SpinnerGlyph;
        char gLater = Sample(SessionPhase.Processing, MascotAnimator.SpinnerStepMs * 1.5).SpinnerGlyph;
        Assert.NotEqual(g0, gLater);
    }

    // --- verbo dentro del pool ---------------------------------------------

    [Fact]
    public void VerbIndex_is_within_the_phase_pool()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            int poolSize = MascotAnimator.VerbPoolSize(p);
            for (double t = 0; t <= 15000; t += 53)
            {
                int vi = Sample(p, t, seed: 4).VerbIndex;
                Assert.InRange(vi, 0, poolSize - 1);
            }
        }
    }

    [Fact]
    public void Every_phase_pool_has_three_to_five_verbs()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
            Assert.InRange(MascotAnimator.VerbPoolSize(p), 3, 5);
    }

    [Fact]
    public void Verb_rotates_over_time_within_a_phase()
    {
        // El verbo no es fijo: a lo largo del tiempo se ve más de uno del pool (rotación juguetona).
        var seen = new HashSet<int>();
        for (double t = 0; t <= 60000; t += 250)
            seen.Add(Sample(SessionPhase.Processing, t, seed: 2).VerbIndex);
        Assert.True(seen.Count > 1, "el verbo debe rotar dentro del pool");
    }

    [Fact]
    public void Localized_verb_is_resolved_for_every_phase_and_language()
    {
        // Puente con i18n: el animador expone un verbo localizado (string) para cada (fase, idioma),
        // no vacío, índice dentro del pool. Comprueba EN y ES como muestra.
        foreach (var code in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(code);
            foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
            {
                var st = Sample(p, 1234, seed: 5);
                string verb = MascotAnimator.Verb(s, p, st.VerbIndex);
                Assert.False(string.IsNullOrWhiteSpace(verb), $"{code}/{p} verbo vacío");
            }
        }
    }
}
