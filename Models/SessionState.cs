namespace ClaudeBarWin.Models;

/// <summary>Estado vivo de una sesión de Claude Code. Mutado solo por SessionStore.</summary>
public sealed class SessionState
{
    public required string SessionId { get; init; }
    public required string Cwd { get; set; }
    public string ProjectName { get; set; } = "";
    public int? Pid { get; set; }
    public SessionPhase Phase { get; set; } = SessionPhase.Idle;
    public string? PendingTool { get; set; }
    public DateTime LastActivityUtc { get; set; }

    /// <summary>Copia superficial para exponer snapshots inmutables a la UI.</summary>
    public SessionState Clone() => new()
    {
        SessionId = SessionId,
        Cwd = Cwd,
        ProjectName = ProjectName,
        Pid = Pid,
        Phase = Phase,
        PendingTool = PendingTool,
        LastActivityUtc = LastActivityUtc,
    };

    public static string ProjectNameFromCwd(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return "(sin proyecto)";
        var trimmed = cwd.TrimEnd('\\', '/');
        var idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
    }
}
