using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class ForegroundDetectorTests
{
    [Fact]
    public void Null_pid_is_not_foreground()
        => Assert.False(new ForegroundDetector().IsSessionForeground(null));

    [Fact]
    public void Does_not_throw_for_arbitrary_pid()
    {
        var d = new ForegroundDetector();
        var _ = d.IsSessionForeground(999999); // pid inexistente: false sin excepción
    }
}
