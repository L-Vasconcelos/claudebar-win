namespace ClaudeBarWin.Services.Motion;

/// <summary>
/// "Bote" de atención de la mascota: PURO y elapsed-driven (regla de oro F3). Cuando la fase global
/// entra en <c>WaitingForApproval</c>/<c>WaitingForInput</c>, la mascota da unos botes verticales
/// breves que <b>decaen</b>; <see cref="OffsetY"/> devuelve la traslación (px) en cada instante. El
/// "boing" usa <see cref="Easing.OutBack"/> (overshoot) en la subida. Sin reloj/aleatoriedad por
/// dentro → tests deterministas. El llamante lo aplica vía <c>g.TranslateTransform</c> dentro de la
/// celda de la mascota: el <c>y</c> de layout NUNCA cambia (las animaciones no alteran el alto
/// reservado). Con reduce-motion el llamante no lo invoca (offset 0, estado final).
///
/// <para>Estructura: la animación dura <c>repeats</c> periodos de <c>periodMs</c>. En cada periodo la
/// mascota sube (con overshoot) y vuelve a la línea base; la amplitud de cada rebote sucesivo decae
/// linealmente hasta agotarse. El offset es siempre ≥ 0 (el bote es hacia arriba; nunca empuja por
/// debajo de la base).</para>
/// </summary>
public static class Bounce
{
    /// <summary>
    /// Traslación vertical (px, ≥ 0) del bote en <paramref name="elapsedMs"/> ms desde su disparo.
    /// 0 en el inicio, pico positivo a media animación, 0 al final y rebotes que decaen.
    /// <paramref name="periodMs"/> ≤ 0 o <paramref name="repeats"/> ≤ 0 ⇒ plano (sin bote).
    /// </summary>
    public static int OffsetY(double elapsedMs, int amplitudePx, double periodMs, int repeats)
    {
        if (elapsedMs <= 0.0 || periodMs <= 0.0 || repeats <= 0 || amplitudePx == 0)
            return 0;

        double total = periodMs * repeats;
        if (elapsedMs >= total) return 0;   // asentada

        int n = (int)(elapsedMs / periodMs);            // periodo actual [0, repeats)
        double local = (elapsedMs - n * periodMs) / periodMs; // progreso dentro del periodo ∈ [0,1)

        // Hump por periodo: sube con overshoot (OutBack) hasta el centro y baja simétricamente.
        // OutBack(1)==1 con un máximo >1 en el camino → da el "boing" sin pasar de ~1.1.
        double hump = local < 0.5
            ? Easing.OutBack(local / 0.5)
            : Easing.OutBack((1.0 - local) / 0.5);
        if (hump < 0.0) hump = 0.0;

        // Decay lineal de la envolvente: el primer rebote es el más alto, el último el más bajo.
        double decay = (double)(repeats - n) / repeats;

        return (int)Math.Round(amplitudePx * decay * hump);
    }

    /// <summary>¿Sigue el bote en vuelo en <paramref name="elapsedMs"/>? Útil para el scheduler.</summary>
    public static bool IsActive(double elapsedMs, double periodMs, int repeats)
    {
        if (elapsedMs < 0.0 || periodMs <= 0.0 || repeats <= 0) return false;
        return elapsedMs < periodMs * repeats;
    }
}
