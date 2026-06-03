namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// Entrada escalonada de secciones al abrir el panel: PURO y elapsed-driven (regla de oro F3).
/// Cada sección "se asienta" con una traslación vertical de <c>max</c> px → 0, desfasada por su
/// índice (cabecera=0, secciones de datos=1..n, footer=n+1). No hay alfa por glifo: el efecto es
/// traslación (vía <c>g.TranslateTransform</c>) + el fade global de opacidad del panel. El <c>y</c>
/// de layout NUNCA cambia: el dibujo se desplaza dentro de su celda. Sin reloj/aleatoriedad por
/// dentro → tests deterministas. Con reduce-motion el llamante usa offset 0 (estado final).
/// </summary>
public static class Stagger
{
    private static double Clamp01(double t) => t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);

    /// <summary>
    /// Progreso de entrada∈[0,1] de la sección <paramref name="index"/> en el instante
    /// <paramref name="tSinceOpenMs"/> (ms desde la apertura). 0 mientras <c>t &lt; index*stagger</c>;
    /// sube (ease-out) durante <paramref name="durMs"/>; 1 pasado <c>index*stagger + dur</c>. Con
    /// <paramref name="durMs"/> ≤ 0 es instantáneo (0 antes del inicio, 1 desde el inicio).
    /// </summary>
    public static double Alpha(double tSinceOpenMs, int index, double staggerMs, double durMs)
    {
        double start = index * staggerMs;
        double local = tSinceOpenMs - start;
        if (local < 0.0) return 0.0;          // estrictamente antes de su inicio: aún no entra
        if (durMs <= 0.0) return 1.0;          // sin tramo: instantánea desde el inicio (local>=0)
        return Easing.OutCubic(Clamp01(local / durMs));  // a local==0 ⇒ eased(0) == 0
    }

    /// <summary>
    /// Traslación vertical (px) a aplicar dado el <paramref name="alpha"/> de entrada: parte de
    /// <paramref name="maxPx"/> (alpha 0, sección "abajo") y llega a 0 (alpha 1, asentada). Se
    /// clampa el alfa a [0,1] para no extrapolar.
    /// </summary>
    public static int OffsetY(double alpha, int maxPx)
    {
        double a = Clamp01(alpha);
        return (int)Math.Round(maxPx * (1.0 - a));
    }
}
