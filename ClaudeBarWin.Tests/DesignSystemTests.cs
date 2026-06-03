using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class DesignSystemTests
{
    [Fact]
    public void Spacing_values_are_multiples_of_four()
    {
        foreach (var v in new[] { Spacing.Xs, Spacing.Sm, Spacing.Md, Spacing.Lg, Spacing.Xl, Spacing.Xxl })
            Assert.Equal(0, v % 4);
    }

    [Fact]
    public void Lerp_returns_endpoints_at_0_and_1()
    {
        var a = Color.FromArgb(10, 20, 30);
        var b = Color.FromArgb(200, 200, 200);
        Assert.Equal(a, ColorMath.Lerp(a, b, 0));
        Assert.Equal(b, ColorMath.Lerp(a, b, 1));
    }

    // --- T9: ratio de contraste WCAG (verificación del tema claro) ---

    [Fact]
    public void ContrastRatio_black_on_white_is_21()
    {
        // El máximo teórico del ratio WCAG 2.x es 21:1 (negro sobre blanco).
        Assert.Equal(21.0, ColorMath.ContrastRatio(Color.Black, Color.White), 1);
    }

    [Fact]
    public void ContrastRatio_is_symmetric()
    {
        var a = Color.FromArgb(108, 108, 114);
        var b = Color.FromArgb(250, 250, 250);
        Assert.Equal(ColorMath.ContrastRatio(a, b), ColorMath.ContrastRatio(b, a), 6);
    }

    [Fact]
    public void ContrastRatio_same_color_is_1()
    {
        var c = Color.FromArgb(120, 130, 140);
        Assert.Equal(1.0, ColorMath.ContrastRatio(c, c), 6);
    }
}
