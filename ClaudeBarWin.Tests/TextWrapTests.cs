using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Cobertura del envoltorio de texto puro (<see cref="TextWrap.WordWrap"/>) que arregla el
/// truncamiento del sello de privacidad y el hint del footer (F2 → F3, Tarea 8). El algoritmo es
/// puro: la medida del ancho se inyecta como <c>Func&lt;string,double&gt;</c> (en producción es
/// <c>g.MeasureString</c>), así que el test es determinista sin GDI+.
/// </summary>
public class TextWrapTests
{
    // Medidor sintético: 10 px por carácter. Determinista, sin fuentes ni GDI+.
    private static double Measure(string s) => s.Length * 10.0;

    [Fact]
    public void Single_short_line_is_not_wrapped()
    {
        var lines = TextWrap.WordWrap("hola mundo", maxWidth: 500, Measure);
        Assert.Single(lines);
        Assert.Equal("hola mundo", lines[0]);
    }

    [Fact]
    public void Wraps_on_word_boundary_when_it_overflows()
    {
        // "uno dos tres" mide 120 px; con 80 px de ancho cabe "uno dos" (70) pero no "tres".
        var lines = TextWrap.WordWrap("uno dos tres", maxWidth: 80, Measure);
        Assert.Equal(2, lines.Count);
        Assert.Equal("uno dos", lines[0]);
        Assert.Equal("tres", lines[1]);
        // Ninguna línea excede el ancho máximo.
        Assert.All(lines, l => Assert.True(Measure(l) <= 80));
    }

    [Fact]
    public void Reassembling_preserves_words_and_order()
    {
        var lines = TextWrap.WordWrap("alfa beta gamma delta", maxWidth: 100, Measure);
        Assert.Equal("alfa beta gamma delta", string.Join(" ", lines));
    }

    [Fact]
    public void A_single_word_longer_than_max_is_kept_on_its_own_line_not_dropped()
    {
        // Palabra de 120 px con ancho 80: no se puede partir por espacios → se emite igual (no se pierde).
        var lines = TextWrap.WordWrap("supercalifragilis", maxWidth: 80, Measure);
        Assert.Single(lines);
        Assert.Equal("supercalifragilis", lines[0]);
    }

    [Fact]
    public void Empty_or_whitespace_yields_a_single_empty_line()
    {
        Assert.Equal(new[] { "" }, TextWrap.WordWrap("", 100, Measure));
        Assert.Equal(new[] { "" }, TextWrap.WordWrap("   ", 100, Measure));
    }

    [Fact]
    public void Collapses_runs_of_whitespace_between_words()
    {
        // Espacios múltiples no generan palabras vacías ni líneas sueltas.
        var lines = TextWrap.WordWrap("uno   dos", maxWidth: 500, Measure);
        Assert.Single(lines);
        Assert.Equal("uno dos", lines[0]);
    }

    [Fact]
    public void Non_positive_width_keeps_each_word_on_its_own_line()
    {
        // Defensa: con ancho <= 0 no se entra en bucle infinito; cada palabra va a su línea.
        var lines = TextWrap.WordWrap("uno dos tres", maxWidth: 0, Measure);
        Assert.Equal(new[] { "uno", "dos", "tres" }, lines);
    }
}
