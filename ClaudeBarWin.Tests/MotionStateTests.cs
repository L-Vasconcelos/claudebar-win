using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="MotionState"/> es el contenedor de <see cref="AnimatedValue"/> por clave que el
/// render muestrea. Su núcleo es PURO y elapsed-driven: <c>Set</c> rearma el objetivo, <c>Advance</c>
/// integra el tiempo y <c>Display</c> muestrea la curva. Con <c>reduceMotion</c> devuelve el target
/// al instante (puerta de la Tarea 7). Sin reloj/GDI+ por dentro → tests deterministas.
/// </summary>
public class MotionStateTests
{
    private const double Dur = 220.0;

    [Fact]
    public void First_display_seeds_at_target_without_animating()
    {
        var m = new MotionState();
        // La primera lectura de una clave nueva siembra el valor: no hay tween "desde 0".
        Assert.Equal(80.0, m.Display("bar:5h", 80.0, reduceMotion: false), 9);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Set_then_half_advance_displays_between_prev_and_target()
    {
        var m = new MotionState();
        m.Display("bar:5h", 20.0, reduceMotion: false); // siembra en 20
        m.Set("bar:5h", 80.0, Dur, Easing.OutCubic);    // anima 20→80
        Assert.True(m.IsAnimating);

        m.Advance(Dur / 2);
        double mid = m.Display("bar:5h", 80.0, reduceMotion: false);
        Assert.True(mid > 20.0 && mid < 80.0, $"esperado en (20,80), got {mid}");
    }

    [Fact]
    public void Full_advance_reaches_target_and_stops_animating()
    {
        var m = new MotionState();
        m.Display("bar:7d", 10.0, reduceMotion: false);
        m.Set("bar:7d", 60.0, Dur, Easing.OutCubic);
        m.Advance(Dur);
        Assert.Equal(60.0, m.Display("bar:7d", 60.0, reduceMotion: false), 6);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Reduce_motion_displays_target_immediately()
    {
        var m = new MotionState();
        m.Display("num:crit", 5.0, reduceMotion: true);
        m.Set("num:crit", 95.0, Dur, Easing.OutCubic);
        // Con reduce-motion el muestreo es el target, sin tween intermedio.
        Assert.Equal(95.0, m.Display("num:crit", 95.0, reduceMotion: true), 9);
    }

    [Fact]
    public void Reduce_motion_does_not_request_animation()
    {
        var m = new MotionState();
        m.Display("pace", 0.0, reduceMotion: true);
        m.Set("pace", 100.0, Dur, Easing.OutCubic, reduceMotion: true);
        // El gate de reduce-motion colapsa el valor: nada que animar.
        Assert.False(m.IsAnimating);
        Assert.Equal(100.0, m.Display("pace", 100.0, reduceMotion: true), 9);
    }

    [Fact]
    public void Keys_are_independent()
    {
        var m = new MotionState();
        m.Display("bar:5h", 30.0, reduceMotion: false);
        m.Display("bar:7d", 70.0, reduceMotion: false);
        m.Set("bar:5h", 90.0, Dur, Easing.OutCubic);
        m.Advance(Dur / 2);

        double five = m.Display("bar:5h", 90.0, reduceMotion: false);
        double seven = m.Display("bar:7d", 70.0, reduceMotion: false);
        Assert.True(five > 30.0 && five < 90.0, $"5h en vuelo, got {five}");
        Assert.Equal(70.0, seven, 9); // 7d no se tocó: sigue asentado
    }

    [Fact]
    public void IsAnimating_reflects_any_animating_key()
    {
        var m = new MotionState();
        m.Display("bar:5h", 10.0, reduceMotion: false);
        m.Display("bar:7d", 10.0, reduceMotion: false);
        Assert.False(m.IsAnimating);

        m.Set("bar:7d", 50.0, Dur, Easing.OutCubic);
        Assert.True(m.IsAnimating);
        m.Advance(Dur);
        Assert.False(m.IsAnimating);
    }

    [Fact]
    public void Display_uses_default_tween_duration_when_set_via_helper()
    {
        var m = new MotionState();
        m.Display("bar:5h", 0.0, reduceMotion: false);
        // Set por defecto usa Motion.TweenMs + OutCubic: a media duración debe estar a medio camino.
        m.SetTarget("bar:5h", 100.0, reduceMotion: false);
        m.Advance(Motion.TweenMs / 2);
        double mid = m.Display("bar:5h", 100.0, reduceMotion: false);
        Assert.True(mid > 0.0 && mid < 100.0, $"got {mid}");
        m.Advance(Motion.TweenMs);
        Assert.Equal(100.0, m.Display("bar:5h", 100.0, reduceMotion: false), 6);
    }

    [Fact]
    public void SetTarget_with_reduce_motion_snaps()
    {
        var m = new MotionState();
        m.Display("bar:5h", 0.0, reduceMotion: true);
        m.SetTarget("bar:5h", 100.0, reduceMotion: true);
        Assert.False(m.IsAnimating);
        Assert.Equal(100.0, m.Display("bar:5h", 100.0, reduceMotion: true), 9);
    }
}
