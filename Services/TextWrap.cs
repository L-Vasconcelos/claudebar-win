namespace ClaudeBarWin.Services;

/// <summary>
/// Envoltorio de texto por palabras, <b>puro</b>: el cálculo de ancho se inyecta como
/// <c>Func&lt;string,double&gt;</c> (en producción es <c>g.MeasureString(s, font).Width</c>), así
/// que el algoritmo se testea sin GDI+/fuentes. Lo usa el footer del panel para que el sello de
/// privacidad y el hint dejen de truncarse al ancho fijo (340 px) — F2 dejó esa pega abierta.
/// </summary>
public static class TextWrap
{
    /// <summary>
    /// Parte <paramref name="text"/> en líneas que no excedan <paramref name="maxWidth"/> según
    /// <paramref name="measure"/>, rompiendo solo en límites de palabra. Una palabra más ancha que
    /// el máximo se mantiene en su propia línea (no se trunca ni se pierde). Colapsa secuencias de
    /// espacios. Texto vacío / solo espacios devuelve una única línea vacía. Determinista.
    /// </summary>
    public static List<string> WordWrap(string text, double maxWidth, Func<string, double> measure)
    {
        var words = (text ?? string.Empty).Split(' ', '\t', '\n', '\r')
            .Where(w => w.Length > 0)
            .ToArray();
        var lines = new List<string>();
        if (words.Length == 0) { lines.Add(string.Empty); return lines; }

        string current = string.Empty;
        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current = word; // primera palabra de la línea: entra siempre (aunque desborde sola).
                continue;
            }
            string candidate = current + " " + word;
            if (measure(candidate) <= maxWidth)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        lines.Add(current);
        return lines;
    }

    /// <summary>Elipsis Unicode (un solo glifo, más estrecho que "...").</summary>
    public const string Ellipsis = "…";

    /// <summary>
    /// Recorta <paramref name="text"/> con una elipsis medida para que NO exceda
    /// <paramref name="maxWidth"/> según <paramref name="measure"/>. Si ya cabe, lo devuelve intacto.
    /// Recorta carácter a carácter desde la cola hasta que <c>prefijo + "…"</c> entra; si ni un solo
    /// carácter + elipsis cabe, devuelve solo la elipsis (o cadena vacía si ni la elipsis cabe).
    /// Puro y determinista (misma entrada → misma salida) para garantizar medir==pintar.
    /// </summary>
    public static string Ellipsize(string text, double maxWidth, Func<string, double> measure)
    {
        text ??= string.Empty;
        if (text.Length == 0 || measure(text) <= maxWidth) return text;

        double ellW = measure(Ellipsis);
        if (ellW > maxWidth) return string.Empty; // ni la elipsis cabe
        // Recorta desde la cola hasta que prefijo+elipsis entre.
        for (int len = text.Length - 1; len >= 1; len--)
        {
            string candidate = text[..len] + Ellipsis;
            if (measure(candidate) <= maxWidth) return candidate;
        }
        return Ellipsis; // solo la elipsis cabe
    }

    /// <summary>
    /// Acota una línea con tope derecho (T8): recorta <paramref name="text"/> (vía
    /// <see cref="Ellipsize"/>) para que, pintada desde <paramref name="drawX"/>, NUNCA rebase
    /// <paramref name="rightEdge"/> − <paramref name="margin"/>. Generaliza el <c>FitHeaderLine</c>
    /// de la cabecera para cualquier línea right-bounded (plan del chrome, línea de pace, línea de
    /// reset, nombres de sesión, claves de gasto). Pura y determinista → medir==pintar.
    /// </summary>
    public static string FitLine(string text, int drawX, int rightEdge, int margin, Func<string, double> measure)
    {
        int avail = Math.Max(0, rightEdge - margin - drawX);
        return Ellipsize(text, avail, measure);
    }
}
