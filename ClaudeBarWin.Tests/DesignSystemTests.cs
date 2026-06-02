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
}
