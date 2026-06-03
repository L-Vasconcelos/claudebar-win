using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Construcción <b>pura</b> del bloque footer del panel (marcador stale + "Actualizado · hace N …"
/// + sello de privacidad). Es la ÚNICA fuente de verdad de qué líneas se pintan abajo, compartida
/// entre la pasada de pintado (<c>LayoutContent</c>) y la guardia de re-layout del tick lento.
///
/// <para><b>Por qué existe (F3, fix Tarea 8):</b> el alto del footer depende del reloj de pared por
/// dos vías — el flag <see cref="UsageFormat.IsStale"/> (añade una línea al envejecer) y el texto de
/// <see cref="UsageFormat.Relative"/> ("2 s" → "5 min" → "1 h"), cuyo nº de líneas tras envolver puede
/// cambiar. El alto de la ventana solo se recalcula en <c>Relayout()</c>, pero el panel se repinta a
/// 1 Hz por el countdown. Sin red de seguridad, una transición fresh→stale (o un cruce de límite de
/// wrap del relativo) entre dos snapshots dejaba la ventana 1 línea CORTA y recortaba el sello.</para>
///
/// <para>El cálculo de ancho se inyecta como <c>Func&lt;string,double&gt;</c> (en producción es
/// <c>g.MeasureString</c>), así que se testea sin GDI+/fuentes ni reloj real: el instante "ahora" y
/// la antigüedad del dato se pasan por parámetro.</para>
/// </summary>
public static class FooterLayout
{
    /// <summary>Una línea del footer con su rol (para colorearla en el paint).</summary>
    public enum LineKind
    {
        /// <summary>Marcador "⚠ desfasado ·" (color Warn).</summary>
        Stale,
        /// <summary>Línea de estado/actualización (color TextSecondary).</summary>
        Footer,
        /// <summary>Sello de privacidad (color TextMuted).</summary>
        Seal,
    }

    /// <summary>Una línea ya envuelta del bloque footer.</summary>
    public readonly record struct Line(string Text, LineKind Kind);

    /// <summary>
    /// Produce las líneas del bloque footer en orden de pintado. <paramref name="stale"/> ya resuelto
    /// por el llamante (vía <see cref="UsageFormat.IsStale"/> con el "ahora" deseado); el texto del
    /// relativo se calcula aquí con <paramref name="nowUtc"/> para que paint y guardia coincidan.
    /// </summary>
    /// <param name="snap">Snapshot vigente (o null = "cargando").</param>
    /// <param name="s">Strings localizados.</param>
    /// <param name="stale">¿Marcar el dato como desfasado? (lo decide el llamante; añade 1 línea).</param>
    /// <param name="sticky">Panel fijado (cambia la pista del footer).</param>
    /// <param name="nowUtc">Instante "ahora" para el texto relativo (inyectable; no lee el reloj).</param>
    /// <param name="maxWidth">Ancho disponible para envolver.</param>
    /// <param name="measure">Medidor de ancho (en producción <c>g.MeasureString(...).Width</c>).</param>
    public static List<Line> Build(
        AppSnapshot? snap, Strings s, bool stale, bool sticky,
        DateTime nowUtc, double maxWidth, Func<string, double> measure)
    {
        var lines = new List<Line>();

        if (stale)
            lines.Add(new Line($"⚠ {s.StaleLabel} ·", LineKind.Stale));

        string footer;
        if (snap is not null && snap.LatestState != UsageFetchState.Ok)
            footer = $"⚠ {UsageFormat.StateMessage(snap.LatestState, s)} · {s.PreviousDataFooter}";
        else if (snap is not null)
        {
            string hint = sticky ? s.HintPinnedClose : s.HintClickToHide;
            footer = $"{s.UpdatedAt} · {UsageFormat.RelativeAt(snap.UsageAtUtc, nowUtc, s)} · {hint}";
        }
        else footer = s.Loading;

        foreach (var line in TextWrap.WordWrap(footer, maxWidth, measure))
            lines.Add(new Line(line, LineKind.Footer));

        foreach (var line in TextWrap.WordWrap(s.LocalSeal, maxWidth, measure))
            lines.Add(new Line(line, LineKind.Seal));

        return lines;
    }

    /// <summary>
    /// Firma compacta del bloque footer en un instante: nº de líneas de cada rol. Si esta firma cambia
    /// entre dos repaints sin nuevo snapshot, el alto reservado quedó obsoleto y hace falta un
    /// <c>Relayout()</c>. Comparar firmas (no listas) evita re-medir en cada tick.
    /// </summary>
    public static (int Stale, int Footer, int Seal) Signature(IReadOnlyList<Line> lines)
    {
        int stale = 0, footer = 0, seal = 0;
        foreach (var l in lines)
        {
            switch (l.Kind)
            {
                case LineKind.Stale: stale++; break;
                case LineKind.Footer: footer++; break;
                case LineKind.Seal: seal++; break;
            }
        }
        return (stale, footer, seal);
    }
}
