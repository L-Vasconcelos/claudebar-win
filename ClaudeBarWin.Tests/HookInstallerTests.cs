using System.Text.Json;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class HookInstallerTests
{
    private const string Cmd = "powershell -NoProfile -File \"C:\\x\\claudebar-hook.ps1\"";

    [Fact]
    public void Merge_into_empty_settings_adds_all_events()
    {
        var merged = HookInstaller.MergeSettings("{}", Cmd);
        using var doc = JsonDocument.Parse(merged);
        var hooks = doc.RootElement.GetProperty("hooks");
        Assert.True(hooks.TryGetProperty("PreToolUse", out _));
        Assert.True(hooks.TryGetProperty("PermissionRequest", out _));
        Assert.Contains("claudebar-hook.ps1", merged);
    }

    [Fact]
    public void Merge_is_idempotent()
    {
        var once = HookInstaller.MergeSettings("{}", Cmd);
        var twice = HookInstaller.MergeSettings(once, Cmd);
        // No debe duplicar nuestra entrada: contar ocurrencias de la marca por evento sigue siendo 1 cada uno.
        var count = twice.Split("claudebar-hook.ps1").Length - 1;
        var onceCount = once.Split("claudebar-hook.ps1").Length - 1;
        Assert.Equal(onceCount, count);
    }

    [Fact]
    public void Merge_preserves_foreign_hooks()
    {
        var existing = """
        {"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"echo cron-setup"}]}]}}
        """;
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        Assert.Contains("echo cron-setup", merged); // el hook del Asistente sobrevive
        Assert.Contains("claudebar-hook.ps1", merged);
    }

    [Fact]
    public void Remove_strips_only_our_hooks()
    {
        var existing = """
        {"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"echo cron-setup"}]}]}}
        """;
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        var removed = HookInstaller.RemoveHooks(merged);
        Assert.Contains("echo cron-setup", removed);
        Assert.DoesNotContain("claudebar-hook.ps1", removed);
    }

    [Fact]
    public void Remove_preserves_non_hook_settings()
    {
        var existing = """{"model":"opus","hooks":{}}""";
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        var removed = HookInstaller.RemoveHooks(merged);
        Assert.Contains("\"model\"", removed);
    }
}
