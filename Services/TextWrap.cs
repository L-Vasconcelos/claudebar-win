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
}
