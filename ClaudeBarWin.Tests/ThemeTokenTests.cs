using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class ThemeTokenTests
{
    public static IEnumerable<object[]> Themes =>
        new[] { new object[] { Theme.Dark }, new object[] { Theme.Light }, new object[] { Theme.Cli } };

    [Theory]
    [MemberData(nameof(Themes))]
    public void All_tokens_are_opaque(Theme t)
    {
        var tokens = new[] { t.TextPrimary, t.TextSecondary, t.TextMuted, t.Separator, t.Track,
                             t.BgBase, t.BgElevated, t.Accent, t.Ok, t.Warn, t.Critical, t.Neutral };
        Assert.All(tokens, c => Assert.True(c.A > 0, "token transparente/sin setear"));
    }

    [Fact]
    public void Dark_accent_is_claude_orange()
        => Assert.Equal(Color.FromArgb(0xCC, 0x78, 0x5C), Theme.Dark.Accent);

    [Fact]
    public void Dark_has_two_background_levels()
        => Assert.NotEqual(Theme.Dark.BgBase, Theme.Dark.BgElevated);

    [Fact]
    public void Semantic_aliases_map_to_existing_fields()
    {
        Assert.Equal(Theme.Dark.Foreground, Theme.Dark.TextPrimary);
        Assert.Equal(Theme.Dark.Dim, Theme.Dark.TextSecondary);
        Assert.Equal(Theme.Dark.Background, Theme.Dark.BgBase);
    }
}
