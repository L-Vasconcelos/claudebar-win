using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T0: la mascota Idle debe verse con <c>ShowMascot=on</c> AUNQUE <c>LiveSessionsEnabled=off</c>
/// (desacople de la puerta de visibilidad en <see cref="DashboardHeader"/>). Las puertas de
/// ANIMACIÓN (bote/fast-tick) siguen exigiendo live on, pero el bloque Idle estático ya reserva
/// alto y desplaza la columna de texto. Verificado además el invariante de 2 pasadas (medir==pintar).
/// </summary>
public class DashboardHeaderTests
{
    private const int X = 16, Y = 0, W = 308;

    // Bitmap/Graphics reales para que MeasureString/GetHeight tengan un contexto válido.
    private static Bitmap NewBmp() => new(W + X * 2, 400);

    private static AppConfig Cfg(bool showMascot, bool liveEnabled) => new()
    {
        ShowMascot = showMascot,
        LiveSessionsEnabled = liveEnabled,
        ShowHealth = true,
        MascotSize = "compact",
    };

    // Header con snapshot vacío: la columna derecha apenas aporta alto, así que el bloque de la
    // mascota (Idle) domina el bottom cuando está visible → cambio observable en el y devuelto.
    private static int RunHeader(Graphics g, bool draw, AppConfig cfg, int bounce = 0)
    {
        Rectangle gear = Rectangle.Empty;
        var live = new LiveSessionsView(); // GlobalPhase = Idle por defecto
        var s = Localization.Get("en");
        return DashboardHeader.Draw(
            g, draw, X, Y, W,
            snap: null, live, cfg, s, Theme.Dark,
            MascotAnimator.StaticState, Mood.Neutral,
            Typography.Body, Typography.Caption, Typography.Mono,
            ref gear,
            motion: null, reduceMotion: false,
            mascotBounceOffsetY: bounce, celebration: null);
    }

    [Fact]
    public void Mascot_idle_reserves_height_when_ShowMascot_on_and_live_off()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int withMascot = RunHeader(g, draw: false, Cfg(showMascot: true, liveEnabled: false));
        int withoutMascot = RunHeader(g, draw: false, Cfg(showMascot: false, liveEnabled: false));

        // Con la puerta vieja (live && mascota) ambos eran iguales (mascota nunca se pintaba con
        // live off). Tras T0, el bloque Idle reserva alto > 0 y el header crece. Esta es la regresión
        // que reproduce el bug "activo Mostrar mascota y no pasa nada".
        Assert.True(withMascot > withoutMascot,
            $"con ShowMascot on (live off) el header debe reservar alto para la mascota Idle: " +
            $"mascota={withMascot} sin={withoutMascot}");
    }

    [Fact]
    public void Header_measure_equals_paint_with_mascot_on_and_live_off()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: true, liveEnabled: false);

        int measured = RunHeader(g, draw: false, cfg);
        int painted = RunHeader(g, draw: true, cfg);

        // Invariante de 2 pasadas: medir y pintar devuelven el MISMO y aunque la mascota esté visible.
        Assert.Equal(measured, painted);
    }

    [Fact]
    public void Header_measure_equals_paint_with_mascot_off()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false);

        int measured = RunHeader(g, draw: false, cfg);
        int painted = RunHeader(g, draw: true, cfg);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void Bounce_offset_does_not_change_layout_height()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: true, liveEnabled: false);

        // El bote es puramente visual (transform en la pasada de pintado): NO debe alterar el y de
        // layout. Distintos offsets de bote -> mismo alto devuelto (puerta de animación intacta).
        int noBounce = RunHeader(g, draw: true, cfg, bounce: 0);
        int bounced = RunHeader(g, draw: true, cfg, bounce: 6);

        Assert.Equal(noBounce, bounced);
    }
}
