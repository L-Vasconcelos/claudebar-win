namespace ClaudeBarWin.Services;

/// <summary>
/// "Start with Windows" via a shortcut in the user's Startup folder.
/// No registry, no admin — fully reversible (the toggle just deletes the .lnk).
/// </summary>
public static class StartupManager
{
    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ClaudeBarWin.lnk");

    public static bool IsEnabled() => File.Exists(ShortcutPath);

    public static void Toggle()
    {
        if (IsEnabled()) Disable();
        else Enable();
    }

    public static void Enable()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return; // only meaningful for the published exe, not `dotnet exec`

            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;

            dynamic shell = Activator.CreateInstance(type)!;
            var sc = shell.CreateShortcut(ShortcutPath);
            sc.TargetPath = exe;
            sc.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
            sc.Description = "ClaudeBar for Windows";
            sc.Save();
        }
        catch
        {
            // best-effort
        }
    }

    public static void Disable()
    {
        try
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        }
        catch
        {
            // ignore
        }
    }
}
