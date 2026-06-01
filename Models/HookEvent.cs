using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeBarWin.Models;

/// <summary>Un evento emitido por el hook de Claude Code a través del Named Pipe.</summary>
public sealed class HookEvent
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";
    [JsonPropertyName("pid")] public int? Pid { get; set; }
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("tool")] public string? Tool { get; set; }
    [JsonPropertyName("tool_use_id")] public string? ToolUseId { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("ts")] public long Ts { get; set; }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Parsea una línea JSON. Devuelve null si no es JSON válido o falta session_id.</summary>
    public static HookEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var e = JsonSerializer.Deserialize<HookEvent>(line, Opts);
            if (e is null || string.IsNullOrEmpty(e.SessionId)) return null;
            return e;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Mapea el evento a una fase de sesión.</summary>
    public SessionPhase ToPhase()
    {
        if (Event == "PreCompact") return SessionPhase.Compacting;
        return Status switch
        {
            "waiting_for_approval" => SessionPhase.WaitingForApproval,
            "waiting_for_input" => SessionPhase.WaitingForInput,
            "running_tool" or "processing" or "starting" => SessionPhase.Processing,
            "compacting" => SessionPhase.Compacting,
            "ended" => SessionPhase.Ended,
            _ => SessionPhase.Idle,
        };
    }
}
