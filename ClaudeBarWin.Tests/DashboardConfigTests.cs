using System.Text.Json;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Tests;

public class DashboardConfigTests
{
    [Fact]
    public void Defaults_quota_and_sessions_expanded_spend_and_chart_collapsed()
    {
        var c = new AppConfig();
        Assert.False(c.CollapsedQuota);
        Assert.False(c.CollapsedSessions);
        Assert.True(c.CollapsedSpend);
        Assert.True(c.CollapsedChart);
        Assert.Equal("compact", c.MascotSize);
    }

    [Fact]
    public void Collapsed_and_mascotsize_roundtrip_json()
    {
        var c = new AppConfig { CollapsedQuota = true, CollapsedChart = false, MascotSize = "large" };
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c))!;
        Assert.True(back.CollapsedQuota);
        Assert.False(back.CollapsedChart);
        Assert.Equal("large", back.MascotSize);
    }
}
