using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class UsageFormatTests
{
    [Fact]
    public void ResetAbsolute_formats_local_time_as_ddd_HHmm()
    {
        // Un instante UTC concreto; ResetAbsolute lo pasa a hora local en formato "ddd HH:mm".
        var when = new DateTimeOffset(2026, 6, 2, 16, 42, 0, TimeSpan.Zero);
        var expected = when.ToLocalTime().ToString("ddd HH:mm");

        Assert.Equal(expected, UsageFormat.ResetAbsolute(when));
    }

    [Fact]
    public void ResetAbsolute_returns_empty_for_null()
    {
        Assert.Equal("", UsageFormat.ResetAbsolute(null));
    }
}
