using ClaudeBarWin.Models;

namespace ClaudeBarWin.Tests;

public class HookEventTests
{
    [Fact]
    public void Parses_minimal_json()
    {
        var e = HookEvent.Parse("""{"session_id":"abc","cwd":"C:\\proj\\x","event":"PreToolUse","status":"running_tool","tool":"Bash"}""");
        Assert.NotNull(e);
        Assert.Equal("abc", e!.SessionId);
        Assert.Equal("C:\\proj\\x", e.Cwd);
        Assert.Equal("Bash", e.Tool);
    }

    [Fact]
    public void Returns_null_on_garbage()
        => Assert.Null(HookEvent.Parse("not json"));

    [Fact]
    public void Returns_null_when_session_id_missing()
        => Assert.Null(HookEvent.Parse("""{"event":"Stop"}"""));

    [Theory]
    [InlineData("waiting_for_approval", SessionPhase.WaitingForApproval)]
    [InlineData("waiting_for_input", SessionPhase.WaitingForInput)]
    [InlineData("running_tool", SessionPhase.Processing)]
    [InlineData("processing", SessionPhase.Processing)]
    [InlineData("starting", SessionPhase.Processing)]
    [InlineData("compacting", SessionPhase.Compacting)]
    [InlineData("ended", SessionPhase.Ended)]
    [InlineData("whatever", SessionPhase.Idle)]
    public void Maps_status_to_phase(string status, SessionPhase expected)
    {
        var e = HookEvent.Parse($$"""{"session_id":"s","cwd":"c","event":"X","status":"{{status}}"}""");
        Assert.Equal(expected, e!.ToPhase());
    }

    [Fact]
    public void PreCompact_event_forces_compacting_regardless_of_status()
    {
        var e = HookEvent.Parse("""{"session_id":"s","cwd":"c","event":"PreCompact","status":"running_tool"}""");
        Assert.Equal(SessionPhase.Compacting, e!.ToPhase());
    }
}
