using System.Text.Json;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Tests;

public class AppConfigTests
{
    [Fact]
    public void Defaults_are_opt_in_for_live_sessions()
    {
        var c = new AppConfig();
        Assert.False(c.LiveSessionsEnabled);
        Assert.True(c.ShowMascot);
        Assert.True(c.SuppressWhenFocused);
        Assert.Equal("cat", c.MascotKind);
    }

    [Fact]
    public void Roundtrips_through_json()
    {
        var c = new AppConfig { LiveSessionsEnabled = true, ShowMascot = false, SuppressWhenFocused = false, MascotKind = "cat" };
        var json = JsonSerializer.Serialize(c);
        var back = JsonSerializer.Deserialize<AppConfig>(json)!;
        Assert.True(back.LiveSessionsEnabled);
        Assert.False(back.ShowMascot);
        Assert.False(back.SuppressWhenFocused);
    }

    [Fact]
    public void Missing_keys_fall_back_to_defaults()
    {
        var back = JsonSerializer.Deserialize<AppConfig>("{}")!;
        Assert.False(back.LiveSessionsEnabled);
        Assert.True(back.ShowMascot);
    }
}
