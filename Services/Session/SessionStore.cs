using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>Fuente única de verdad de las sesiones vivas. Thread-safe vía lock.</summary>
public sealed class SessionStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionState> _sessions = new();

    /// <summary>Se dispara tras cualquier mutación (en el hilo que llamó Apply/Prune).</summary>
    public event Action? Changed;

    /// <summary>Aplica un evento del hook. nowUtc se inyecta para testabilidad.</summary>
    public void Apply(HookEvent e, DateTime nowUtc)
    {
        bool changed;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(e.SessionId, out var s))
            {
                s = new SessionState { SessionId = e.SessionId, Cwd = e.Cwd };
                _sessions[e.SessionId] = s;
            }

            if (!string.IsNullOrEmpty(e.Cwd)) s.Cwd = e.Cwd;
            s.ProjectName = SessionState.ProjectNameFromCwd(s.Cwd);
            if (e.Pid is { } pid) s.Pid = pid;

            var next = e.ToPhase();
            if (s.Phase.CanTransition(next)) s.Phase = next;

            s.PendingTool = s.Phase == SessionPhase.WaitingForApproval ? (e.Tool ?? s.PendingTool) : null;
            s.LastActivityUtc = nowUtc;
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Elimina sesiones Ended o sin actividad desde hace más de ttl. Devuelve cuántas quitó.</summary>
    public int Prune(DateTime nowUtc, TimeSpan ttl)
    {
        int removed;
        lock (_lock)
        {
            var dead = _sessions.Values
                .Where(s => s.Phase == SessionPhase.Ended || nowUtc - s.LastActivityUtc >= ttl)
                .Select(s => s.SessionId)
                .ToList();
            foreach (var id in dead) _sessions.Remove(id);
            removed = dead.Count;
        }
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    /// <summary>Snapshot inmutable de las sesiones actuales.</summary>
    public IReadOnlyList<SessionState> Snapshot()
    {
        lock (_lock) return _sessions.Values.Select(s => s.Clone()).ToList();
    }
}
