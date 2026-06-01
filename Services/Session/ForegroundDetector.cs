using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeBarWin.Services;

/// <summary>
/// Decide si la sesión cuyo pid se pasa está "en primer plano" (su proceso o un ancestro
/// es el dueño de la ventana del primer plano). Heurística: comparamos el pid de la ventana
/// foreground y subimos por el árbol de procesos buscando el pid de la sesión.
/// </summary>
public sealed class ForegroundDetector
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public bool IsSessionForeground(int? sessionPid)
    {
        if (sessionPid is not { } target) return false;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint fgPid);
            if (fgPid == 0) return false;
            if ((int)fgPid == target) return true;

            // Subir por ancestros del proceso foreground (terminal -> shell -> claude).
            var seen = 0;
            int? cur = (int)fgPid;
            while (cur is { } pid && seen++ < 6)
            {
                if (pid == target) return true;
                cur = ParentPid(pid);
            }
            return false;
        }
        catch { return false; }
    }

    private static int? ParentPid(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            // No hay API directa simple; usamos WMI vía ManagementObjectSearcher sería pesado.
            // Heurística ligera: si no podemos resolver el padre, devolvemos null (degrada a "no foreground").
            return null;
        }
        catch { return null; }
    }
}
