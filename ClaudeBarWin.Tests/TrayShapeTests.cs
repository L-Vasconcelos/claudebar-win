using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

public class TrayShapeTests
{
    [Fact]
    public void ShapeFor_ok_is_circle()
        => Assert.Equal(TrayShape.Circle, Tray.ShapeFor(UsageStatus.Ok));

    [Fact]
    public void ShapeFor_warn_is_triangle()
        => Assert.Equal(TrayShape.Triangle, Tray.ShapeFor(UsageStatus.Warn));

    [Fact]
    public void ShapeFor_critical_is_rhombus()
        => Assert.Equal(TrayShape.Rhombus, Tray.ShapeFor(UsageStatus.Critical));

    [Fact]
    public void Each_status_maps_to_a_distinct_shape()
    {
        var shapes = new[]
        {
            Tray.ShapeFor(UsageStatus.Ok),
            Tray.ShapeFor(UsageStatus.Warn),
            Tray.ShapeFor(UsageStatus.Critical)
        };
        Assert.Equal(3, shapes.Distinct().Count());
    }

    [Fact]
    public void Glyph_returns_a_single_char_for_each_status()
    {
        // El glifo de forma del dashboard es de 1 carácter junto al %.
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Circle).Length);
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Triangle).Length);
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Rhombus).Length);
    }

    [Fact]
    public void TaskbarIsLight_does_not_throw()
    {
        // Lee el registro con fallback; nunca debe lanzar.
        var ex = Record.Exception(() => ThemeResolver.TaskbarIsLight());
        Assert.Null(ex);
    }

    [Fact]
    public void Render_with_status_and_stale_does_not_throw()
    {
        // La nueva firma de Render (status + stale) debe producir un icono sin lanzar.
        var ex = Record.Exception(() =>
        {
            using var ico = TrayIconRenderer.Render(
                68, Theme.Dark, 70, 90, UsageStatus.Warn, stale: true);
        });
        Assert.Null(ex);
    }

    // --- T9d (§3 #15): badge "pend" — forma suprimida, ámbar de paleta, sin tapar el dígito ---

    private const double Warn = 70, Crit = 90;

    private static (Bitmap b0, Bitmap b1) RenderPair(int pct, UsageStatus st)
    {
        using var icoBase = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: false, pending: false);
        using var icoPend = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: false, pending: true);
        return (icoBase.ToBitmap(), icoPend.ToBitmap());
    }

    private static double Dist(Color a, Color b)
        => Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));

    [Fact]
    public void Pending_suppresses_the_a11y_shape_overlay()
    {
        // Con el punto de notificación la forma a11y (rombo/triángulo) se SUPRIME: un solo elemento
        // decorativo por esquina — dígito + forma + punto a 16-32px se solapaban en ruido (§3 #15).
        var (b0, b1) = RenderPair(95, UsageStatus.Critical);
        using (b0)
        using (b1)
        {
            // El ícono ya no colorea el fondo por riesgo (v0.3.9: "sparkle" amarillo fijo, puramente
            // estético) — el overlay de forma sigue contrastando contra ESE amarillo, no contra el
            // viejo RiskColor.
            var fill = TrayIconRenderer.SparkleYellow;
            var ink = ColorMath.Contrast(fill);
            // Centro del rombo (s=14 anclado a la esquina inferior derecha del lienzo de 48).
            var shapePx = b0.GetPixel(40, 40);
            Assert.True(Dist(shapePx, ink) < 60, $"sin pending el rombo debe pintarse; fue {shapePx}");
            var pendPx = b1.GetPixel(40, 40);
            Assert.True(Dist(pendPx, ink) >= 60, $"con pending el rombo debe SUPRIMIRSE; fue {pendPx}");
        }
    }

    [Fact]
    public void Pending_dot_uses_palette_warn_amber_with_knockout_ring()
    {
        // El punto era #F5A623 (fuera de paleta; el .ico antiguo lo aplanaba a oliva) con aro
        // #1A1A1A. Ahora: ámbar Warn del tema + aro KNOCKOUT (borrado a transparente) que lo separa
        // del relleno del badge sobre cualquier barra de tareas. Geometría espejo: d=10 en
        // (48-10-1, 1) → centro (42, 6), aro borrado a +2px.
        var (b0, b1) = RenderPair(10, UsageStatus.Ok);
        using (b0)
        using (b1)
        {
            var dotPx = b1.GetPixel(42, 6);
            var warn = Theme.Dark.Warn;
            Assert.True(Dist(dotPx, warn) < 25,
                $"el punto debe ser el ámbar Warn de la paleta {warn}, fue {dotPx}");
            // Aro knockout: entre el borde del punto (r=5) y el del borrado (r=7) queda TRANSPARENTE.
            var ringPx = b1.GetPixel(42 - 6, 6);
            Assert.True(ringPx.A < 64, $"el aro debe quedar perforado (alpha {ringPx.A})");
            // Nota: con el fondo "sparkle" (v0.3.9) esa esquina cae en un valle de la estrella y ya
            // puede ser transparente incluso SIN pending, así que ya no se afirma que el badge base
            // sea opaco ahí (el knockout sigue siendo correcto e inofensivo si no había nada que tapar).
        }
    }

    [Fact]
    public void Pending_dot_does_not_cover_the_digit()
    {
        // §3 #15: el punto de 18px tapaba el dígito. Ahora todo píxel que el modo pending CAMBIA
        // (punto + aro) debe caer fuera de la tinta del dígito: ningún píxel alterado era tinta
        // (ni su anti-aliasing fuerte) en el badge base. "88" = el par de dígitos más ancho.
        var (b0, b1) = RenderPair(88, UsageStatus.Ok);
        using (b0)
        using (b1)
        {
            var fill = ColorMath.RiskColor(88, Theme.Dark, Warn, Crit);
            var ink = ColorMath.Contrast(fill);
            var violations = new List<string>();
            for (int y = 0; y < b0.Height; y++)
                for (int x = 0; x < b0.Width; x++)
                {
                    var p0 = b0.GetPixel(x, y);
                    var p1 = b1.GetPixel(x, y);
                    if (p0.ToArgb() == p1.ToArgb()) continue;          // sin cambio
                    if (p0.A < 200) continue;                           // base transparente: no es tinta
                    if (Dist(p0, ink) < 100)                            // tinta o su AA fuerte
                        violations.Add($"({x},{y}) base={p0} pend={p1}");
                }
            Assert.True(violations.Count == 0,
                $"el punto pending pisa la tinta del dígito en {violations.Count} px: " +
                string.Join("; ", violations.Take(8)));
        }
    }

    // --- F8 (v039 g4): el punto "pend" recupera la forma daltónica del estado dentro del punto ---

    [Fact]
    public void Pending_critical_draws_a_shape_inside_the_attention_dot()
    {
        // Antes el punto de atención SUPRIMÍA toda forma → un Critical+pend (caso real: status+pending a
        // la vez) era distinguible SOLO por color, rompiendo la redundancia no-cromática que el resto de
        // estados sí tienen. Ahora la silueta del estado (◆ Critical) se dibuja DENTRO del punto, en el
        // color de contraste del ámbar. El centro del punto (42,6) debe pintarse con ese color de tinta,
        // no con el ámbar liso.
        var (b0, b1) = RenderPair(95, UsageStatus.Critical);
        using (b0)
        using (b1)
        {
            var ink = ColorMath.Contrast(Theme.Dark.Warn);
            var center = b1.GetPixel(42, 6);
            Assert.True(Dist(center, ink) < 60,
                $"el centro del punto debe llevar la silueta en color de contraste {ink}, fue {center}");
        }
    }

    [Fact]
    public void Pending_warn_draws_a_shape_inside_the_attention_dot()
    {
        // Warn+pend: triángulo dentro del punto (misma redundancia que el Critical).
        var (b0, b1) = RenderPair(75, UsageStatus.Warn);
        using (b0)
        using (b1)
        {
            var ink = ColorMath.Contrast(Theme.Dark.Warn);
            var center = b1.GetPixel(42, 6);
            Assert.True(Dist(center, ink) < 60,
                $"el centro del punto Warn debe llevar la silueta {ink}, fue {center}");
        }
    }

    [Fact]
    public void Pending_ok_keeps_the_dot_a_plain_amber_circle()
    {
        // Ok: el punto YA es un círculo → la redundancia no-cromática está cubierta y NO se dibuja glifo.
        // El centro del punto debe seguir siendo el ámbar liso (sin tinta de silueta encima).
        var (b0, b1) = RenderPair(10, UsageStatus.Ok);
        using (b0)
        using (b1)
        {
            var warn = Theme.Dark.Warn;
            var center = b1.GetPixel(42, 6);
            Assert.True(Dist(center, warn) < 25,
                $"el punto Ok no debe llevar silueta: centro debe ser ámbar {warn}, fue {center}");
        }
    }

    [Fact]
    public void Pending_shape_does_not_cover_the_digit()
    {
        // La silueta vive DENTRO del punto (esquina superior derecha), nunca sobre el dígito. El dígito
        // ocupa la banda central; las dos esquinas derechas son zonas de overlay legítimas (arriba: el
        // punto + su silueta; abajo: la forma a11y que el modo pending SUPRIME — pasa de tinta a relleno,
        // un cambio esperado que no es "tapar el dígito"). Verificamos que en la BANDA CENTRAL (fuera de
        // ambas esquinas) ningún píxel que pending cambia respecto al base era tinta del dígito.
        // "88" = el par de dígitos más ancho, con Critical para que la silueta ◆ esté presente.
        var (b0, b1) = RenderPair(88, UsageStatus.Critical);
        using (b0)
        using (b1)
        {
            var fill = ColorMath.RiskColor(88, Theme.Dark, Warn, Crit);
            var ink = ColorMath.Contrast(fill);
            var violations = new List<string>();
            for (int y = 0; y < b0.Height; y++)
                for (int x = 0; x < b0.Width; x++)
                {
                    bool topRightDot = x >= 34 && y <= 15;     // punto de atención + su silueta
                    bool bottomRightOverlay = x >= 32 && y >= 32; // forma a11y de esquina (suprimida con pend)
                    if (topRightDot || bottomRightOverlay) continue;
                    var p0 = b0.GetPixel(x, y);
                    var p1 = b1.GetPixel(x, y);
                    if (p0.ToArgb() == p1.ToArgb()) continue;
                    if (p0.A < 200) continue;
                    if (Dist(p0, ink) < 100)
                        violations.Add($"({x},{y}) base={p0} pend={p1}");
                }
            Assert.True(violations.Count == 0,
                $"la silueta pending pisa la tinta del dígito en {violations.Count} px: " +
                string.Join("; ", violations.Take(8)));
        }
    }

    // --- F9 (v039 g4): tamaño de fuente del badge tokenizado + fit-to-box para "99+" ---
    // NOTA (v0.3.9, ícono "sparkle"): el ícono del tray ya NO dibuja el dígito del % (pedido del
    // usuario: puramente decorativo, el dato real sigue en el tooltip/panel) — los tests que medían la
    // tinta del "99+" renderizado (NinetyNinePlus_fills_more_than_the_old_18px_literal,
    // NinetyNinePlus_renders_the_plus_glyph_and_is_not_clipped) se retiraron porque probaban un glifo
    // que ya no se pinta. FitFontPx en sí sigue siendo una función pura sin relación con el ícono y
    // conserva sus propios tests más abajo.

    [Fact]
    public void Badge_measures_and_draws_with_the_same_metrics()
    {
        // La causa raíz del recorte fue medir el fit con un StringFormat distinto del de dibujo. La
        // sobrecarga pública FitFontPx(g, text, box) debe coincidir con la que recibe el sf real del
        // badge: si divergen, el fit miente sobre el ancho pintado. Aquí comprobamos que el tamaño
        // calculado con el sf por defecto (el que usa internamente) es el mismo que el de la firma larga.
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        using var sf = new StringFormat(StringFormat.GenericDefault)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        Assert.Equal(
            TrayIconRenderer.FitFontPx(g, "99+", 40f),
            TrayIconRenderer.FitFontPx(g, "99+", 40f, sf));
    }

    [Fact]
    public void FitFontPx_keeps_full_size_for_one_or_two_digits()
    {
        // Un número de 1-2 dígitos cabe de sobra en la caja → conserva el tamaño completo (no encoge).
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        Assert.Equal(30f, TrayIconRenderer.FitFontPx(g, "8", 40f));
        Assert.Equal(30f, TrayIconRenderer.FitFontPx(g, "88", 40f));
    }

    [Fact]
    public void FitFontPx_shrinks_three_chars_but_stays_above_eighteen()
    {
        // "99+" no cabe a 30px en una caja de 40px → encoge, pero por encima del 18px histórico y por
        // debajo del tamaño de un número normal (queda en la franja legible ~22-24px).
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        float px = TrayIconRenderer.FitFontPx(g, "99+", 40f);
        Assert.True(px > 18f, $"'99+' debe superar el 18px fijo histórico, fue {px:0.0}");
        Assert.True(px < 30f, $"'99+' debe encoger respecto a un número normal, fue {px:0.0}");
    }

    [Fact]
    public void FitFontPx_never_drops_below_the_eighteen_floor()
    {
        // Suelo de seguridad: aunque la caja sea minúscula, nunca por debajo de 18px.
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        Assert.Equal(18f, TrayIconRenderer.FitFontPx(g, "99+", 2f));
    }
}
