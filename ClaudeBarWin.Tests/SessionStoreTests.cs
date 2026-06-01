using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class SessionStoreTests
{
    private static HookEvent Ev(string id, string cwd, string ev, string status, string? tool = null)
        => new() { SessionId = id, Cwd = cwd, Event = ev, Status = status, Tool = tool };

    [Fact]
    public void Apply_creates_session_with_project_name_from_cwd()
    {
        var s = new SessionStore();
        s.Apply(Ev("s1", "C:\\Users\\z\\Proyectos\\phoenix", "PreToolUse", "running_tool"), DateTime.UtcNow);
        var sess = Assert.Single(s.Snapshot());
        Assert.Equal("s1", sess.SessionId);
        Assert.Equal("phoenix", sess.ProjectName);
        Assert.Equal(SessionPhase.Processing, sess.Phase);
    }

    [Fact]
    public void Apply_ignores_invalid_transition_but_keeps_session()
    {
        var s = new SessionStore();
        var t0 = DateTime.UtcNow;
        s.Apply(Ev("s1", "c", "SessionEnd", "ended"), t0);
        // Ended es terminal: un nuevo evento processing no debe revivirla a Processing
        s.Apply(Ev("s1", "c", "PreToolUse", "running_tool"), t0.AddSeconds(1));
        Assert.Equal(SessionPhase.Ended, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Apply_records_pending_tool_on_approval()
    {
        var s = new SessionStore();
        s.Apply(Ev("s1", "c", "PermissionRequest", "waiting_for_approval", tool: "Bash"), DateTime.UtcNow);
        Assert.Equal("Bash", s.Snapshot()[0].PendingTool);
    }

    [Fact]
    public void Prune_removes_stale_sessions()
    {
        var s = new SessionStore();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        s.Apply(Ev("old", "c", "PreToolUse", "running_tool"), t0);
        s.Apply(Ev("fresh", "c", "PreToolUse", "running_tool"), t0.AddMinutes(9));
        var removed = s.Prune(t0.AddMinutes(10), TimeSpan.FromMinutes(10));
        Assert.Equal(1, removed);
        Assert.Equal("fresh", Assert.Single(s.Snapshot()).SessionId);
    }

    [Fact]
    public void Apply_raises_Changed()
    {
        var s = new SessionStore();
        var fired = 0;
        s.Changed += () => fired++;
        s.Apply(Ev("s1", "c", "PreToolUse", "running_tool"), DateTime.UtcNow);
        Assert.Equal(1, fired);
    }
}
