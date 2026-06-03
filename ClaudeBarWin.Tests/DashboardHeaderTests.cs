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

    // --- T9: anti-corte de las líneas de la cabecera (salud / pace) con la mascota robando ancho ---

    // Medidor sintético: 10 px por carácter (la elipsis "…" mide 10). Determinista, sin GDI+/fuentes.
    private static double M(string s) => s.Length * 10.0;

    [Fact]
    public void FitHeaderLine_leaves_right_margin_and_elides_when_too_long()
    {
        // drawX=100, rightEdge=300, margin=Spacing.Md(12) → ancho útil = 300-12-100 = 188 px (18 chars).
        // Texto de 30 chars (300 px) no cabe → se elide y cabe en el ancho útil reservando el margen.
        var shown = DashboardHeader.FitHeaderLine(new string('a', 30), drawX: 100, rightEdge: 300,
            margin: Spacing.Md, measure: M);
        Assert.EndsWith("…", shown);
        Assert.True(M(shown) <= 300 - Spacing.Md - 100,
            $"la línea elidida ({M(shown)}px) rebasa el ancho útil con margen derecho {Spacing.Md}px");
    }

    [Fact]
    public void FitHeaderLine_keeps_text_unchanged_when_it_fits()
    {
        var shown = DashboardHeader.FitHeaderLine("ok", drawX: 100, rightEdge: 300, margin: Spacing.Md, measure: M);
        Assert.Equal("ok", shown);
    }

    [Fact]
    public void FitHeaderLine_never_reaches_the_right_edge()
    {
        // Para cualquier longitud, el texto dibujado termina al menos `margin` px antes del borde.
        const int drawX = 80, rightEdge = 280, margin = Spacing.Md;
        for (int len = 1; len <= 40; len++)
        {
            var shown = DashboardHeader.FitHeaderLine(new string('x', len), drawX, rightEdge, margin, M);
            Assert.True(drawX + M(shown) <= rightEdge - margin + 1e-6,
                $"len {len}: la línea llega a {drawX + M(shown)} (borde {rightEdge}, margen {margin})");
        }
    }

    // --- v0.3.5 P0 #4: el header NO duplica la "Sesión (5h)" (barra+reset+pace) de la sección "Cuota" ---

    // Snapshot completo (usage 5h/7d + pace con ETA que agota antes del reset) para ejercitar el glance.
    private static AppSnapshot FullSnap(DateTime now)
    {
        var usage = new RealUsage
        {
            FiveHour = new UsageWindow(62, new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero)),
            SevenDay = new UsageWindow(84, new DateTimeOffset(now.AddDays(2).AddHours(6), TimeSpan.Zero)),
        };
        var paceFive = new PaceResult("5h", 62, 1.30, 47.7,
            new DateTimeOffset(now.AddHours(1).AddMinutes(10), TimeSpan.Zero),
            new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero), true, PaceStatus.Critical);
        var paceSeven = new PaceResult("7d", 84, 0.95, 88.4, null,
            new DateTimeOffset(now.AddDays(2).AddHours(6), TimeSpan.Zero), false, PaceStatus.Ok);
        return new AppSnapshot
        {
            Usage = usage, LatestState = UsageFetchState.Ok, UsageAtUtc = now,
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            PaceFive = paceFive, PaceSeven = paceSeven
        };
    }

    private static int RunHeaderSnap(Graphics g, bool draw, AppConfig cfg, AppSnapshot? snap)
    {
        Rectangle gear = Rectangle.Empty;
        var live = new LiveSessionsView();
        var s = Localization.Get("es");
        return DashboardHeader.Draw(g, draw, X, Y, W, snap, live, cfg, s, Theme.Dark,
            MascotAnimator.StaticState, Mood.Neutral, Typography.Body, Typography.Caption, Typography.Mono,
            ref gear, motion: null, reduceMotion: false, mascotBounceOffsetY: 0, celebration: null);
    }

    [Fact]
    public void Header_measure_equals_paint_with_full_usage_and_pace()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false);
        var snap = FullSnap(DateTime.UtcNow);

        Assert.Equal(RunHeaderSnap(g, draw: false, cfg, snap), RunHeaderSnap(g, draw: true, cfg, snap));
    }

    [Fact]
    public void Header_height_does_not_reserve_quota_bar_reset_and_pace()
    {
        // P0 #4: el header ya NO pinta la barra de cuota (etiqueta+%+barra+reset) ni la línea de pace,
        // que duplicaban la primera fila de "Cuota". Con usage presente el header solo añade UN glance de
        // una línea (~16px) respecto a sin usage: jamás el bloque de barra+reset+pace (~60px). Si se hubiera
        // dejado la barra duplicada, la diferencia sería mucho mayor.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false);

        int withUsage = RunHeaderSnap(g, draw: false, cfg, FullSnap(DateTime.UtcNow));
        int noUsage = RunHeaderSnap(g, draw: false, cfg, new AppSnapshot
        {
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            LatestState = UsageFetchState.Ok, UsageAtUtc = DateTime.UtcNow
        });

        int delta = withUsage - noUsage;
        Assert.True(delta > 0, "con usage debe aparecer el glance de una línea");
        Assert.True(delta <= 20, $"el header solo añade UN glance (~16px), no la barra+reset+pace duplicada (delta={delta})");
    }

    [Fact]
    public void Header_height_independent_of_pace_eta()
    {
        // Como el header ya NO pinta el pace, su alto es idéntico tenga o no el pace una ETA de
        // "agotamiento antes del reset" (lo que antes añadía/movía la línea de pace recortada "⚠ vie 1…").
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false);
        var now = DateTime.UtcNow;

        var withEta = FullSnap(now); // PaceFive.ExhaustsBeforeReset = true (ETA presente)
        var noEta = new AppSnapshot
        {
            Usage = withEta.Usage,
            Health = withEta.Health,
            LatestState = UsageFetchState.Ok, UsageAtUtc = now,
            PaceFive = new PaceResult("5h", 62, 0.80, 47.7, null,
                new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero), false, PaceStatus.Ok),
            PaceSeven = withEta.PaceSeven
        };

        Assert.Equal(RunHeaderSnap(g, draw: false, cfg, withEta), RunHeaderSnap(g, draw: false, cfg, noEta));
    }

    // --- v0.3.5 P1 #2: la mascota tiene su PROPIA banda a ancho completo (sin colisión con el estado) ---

    [Fact]
    public void Mascot_band_stacks_above_status_block_not_in_parallel()
    {
        // La mascota ya NO comparte fila con el bloque de estado: ocupa una banda propia ARRIBA y el estado
        // va DEBAJO (apilados, no en paralelo). Por tanto, con mascota + salud el header es más alto que
        // con SOLO salud, en al menos el alto de la banda de la mascota (que ya conocemos de un header con
        // mascota y sin estado). Si fueran en paralelo (diseño viejo) el alto sería el MÁXIMO, no la suma.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        var snapHealth = new AppSnapshot
        {
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            LatestState = UsageFetchState.Ok, UsageAtUtc = DateTime.UtcNow
        };

        int mascotOnlyBand = RunHeader(g, draw: false, Cfg(showMascot: true, liveEnabled: false))
                           - RunHeader(g, draw: false, Cfg(showMascot: false, liveEnabled: false));
        Assert.True(mascotOnlyBand > 0, "la banda de la mascota debe reservar alto");

        int statusOnly = RunHeaderSnap(g, draw: false, Cfg(showMascot: false, liveEnabled: false), snapHealth);
        int mascotPlusStatus = RunHeaderSnap(g, draw: false, Cfg(showMascot: true, liveEnabled: false), snapHealth);

        // Apilados: el alto con mascota+estado supera al de solo estado en (aprox.) la banda de la mascota.
        Assert.True(mascotPlusStatus >= statusOnly + mascotOnlyBand - 1,
            $"la mascota debe APILARSE sobre el estado (su banda), no ir en paralelo: " +
            $"mascota+estado={mascotPlusStatus}, soloEstado={statusOnly}, banda={mascotOnlyBand}");
    }

    [Fact]
    public void Mascot_band_measure_equals_paint_with_full_usage_and_health()
    {
        // medir==pintar con la mascota en su banda + salud + glance de cuota (el caso real del dashboard).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: true, liveEnabled: false);
        var snap = FullSnap(DateTime.UtcNow);

        Assert.Equal(RunHeaderSnap(g, draw: false, cfg, snap), RunHeaderSnap(g, draw: true, cfg, snap));
    }

    [Fact]
    public void Mascot_band_measure_equals_paint_with_bounce()
    {
        // El bote (transform de pintado) no rompe el invariante con la banda apilada: medir(0)==pintar(bounce).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: true, liveEnabled: false);

        int measured = RunHeader(g, draw: false, cfg, bounce: 0);
        int painted = RunHeader(g, draw: true, cfg, bounce: 7);
        Assert.Equal(measured, painted);
    }

    [Fact]
    public void Header_measure_equals_paint_with_mascot_and_health_on_narrow_panel()
    {
        // Cabecera estrecha con mascota + salud: el elidido debe ser idéntico en medir y pintar (mismo y).
        using var bmp = new Bitmap(160, 400);
        using var g = Graphics.FromImage(bmp);
        Rectangle gear = Rectangle.Empty;
        var live = new LiveSessionsView();
        var s = Localization.Get("en");
        var cfg = Cfg(showMascot: true, liveEnabled: false);
        var snap = new AppSnapshot { Health = new HealthStatus(HealthLevel.Operational, "Operational") };

        int Run(bool draw) => DashboardHeader.Draw(g, draw, 8, 0, 120, snap, live, cfg, s, Theme.Dark,
            MascotAnimator.StaticState, Mood.Neutral, Typography.Body, Typography.Caption, Typography.Mono,
            ref gear, motion: null, reduceMotion: false, mascotBounceOffsetY: 0, celebration: null);

        Assert.Equal(Run(false), Run(true));
    }
}
