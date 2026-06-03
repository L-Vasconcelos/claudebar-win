using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeBarWin.Services;

/// <summary>
/// Instala/desinstala el hook de ClaudeBar en ~/.claude/settings.json de forma idempotente,
/// preservando hooks ajenos y haciendo backup. La lógica de merge es pura y testeable.
/// </summary>
public static class HookInstaller
{
    public const string Marker = "claudebar-hook.ps1";

    private static readonly string[] Events =
    {
        "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest",
        "Notification", "Stop", "SubagentStop", "SessionStart", "SessionEnd", "PreCompact",
    };

    public static string ClaudeDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    public static string SettingsPath => Path.Combine(ClaudeDir, "settings.json");
    public static string HookScriptPath => Path.Combine(ClaudeDir, "hooks", "claudebar-hook.ps1");

    /// <summary>Comando que invoca el hook (PowerShell, sin perfil, leyendo el evento de stdin).</summary>
    public static string HookCommand() =>
        $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{HookScriptPath}\"";

    /// <summary>Devuelve true si nuestro hook ya está en settings.json.</summary>
    public static bool IsInstalled()
    {
        try { return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains(Marker); }
        catch { return false; }
    }

    /// <summary>Merge puro: añade nuestro hook a cada evento sin duplicar ni tocar hooks ajenos.</summary>
    public static string MergeSettings(string json, string command)
    {
        var root = (JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject) ?? new JsonObject();
        var hooks = root["hooks"] as JsonObject;
        if (hooks is null) { hooks = new JsonObject(); root["hooks"] = hooks; }

        foreach (var ev in Events)
        {
            var arr = hooks[ev] as JsonArray;
            if (arr is null) { arr = new JsonArray(); hooks[ev] = arr; }

            if (ContainsMarker(arr)) continue; // idempotente

            arr.Add(new JsonObject
            {
                ["matcher"] = "*",
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                }),
            });
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Quita solo nuestras entradas (por la marca), dejando el resto intacto.</summary>
    public static string RemoveHooks(string json)
    {
        var root = (JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject) ?? new JsonObject();
        if (root["hooks"] is not JsonObject hooks)
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        foreach (var ev in hooks.Select(kv => kv.Key).ToList())
        {
            if (hooks[ev] is not JsonArray arr) continue;
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (EntryHasMarker(arr[i])) arr.RemoveAt(i);
            }
            if (arr.Count == 0) hooks.Remove(ev);
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool ContainsMarker(JsonArray arr) => arr.Any(EntryHasMarker);

    private static bool EntryHasMarker(JsonNode? entry)
    {
        if (entry is not JsonObject obj || obj["hooks"] is not JsonArray inner) return false;
        return inner.Any(h => h is JsonObject ho && (ho["command"]?.GetValue<string>() ?? "").Contains(Marker));
    }

    /// <summary>Escribe el script del hook, hace backup de settings.json y mergea. Devuelve la ruta del backup.</summary>
    public static string Install(string hookScriptContents, string backupStamp)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HookScriptPath)!);
        File.WriteAllText(HookScriptPath, hookScriptContents);

        var current = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";
        var backup = SettingsPath + ".claudebar-bak-" + backupStamp;
        Directory.CreateDirectory(ClaudeDir);
        File.WriteAllText(backup, current);

        File.WriteAllText(SettingsPath, MergeSettings(current, HookCommand()));
        return backup;
    }

    /// <summary>Quita el hook de settings.json (con backup) y borra el script.</summary>
    public static string Uninstall(string backupStamp)
    {
        var current = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";
        var backup = SettingsPath + ".claudebar-bak-" + backupStamp;
        File.WriteAllText(backup, current);
        File.WriteAllText(SettingsPath, RemoveHooks(current));
        try { if (File.Exists(HookScriptPath)) File.Delete(HookScriptPath); } catch { }
        return backup;
    }
}
