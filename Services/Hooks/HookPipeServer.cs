using System.IO.Pipes;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Servidor del Named Pipe que recibe eventos del hook de Claude Code.
/// Multi-cliente: acepta una conexión, lee una línea JSON, la parsea y vuelve a escuchar.
/// </summary>
public sealed class HookPipeServer : IDisposable
{
    public const string DefaultPipeName = "claudebar";

    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Se dispara (en un hilo del thread-pool) por cada evento recibido.</summary>
    public event Action<HookEvent>? EventReceived;

    public HookPipeServer(string pipeName = DefaultPipeName) => _pipeName = pipeName;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _loop = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                {
                    var e = HookEvent.Parse(line);
                    if (e is not null) EventReceived?.Invoke(e);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* conexión rota: seguir aceptando */ }
        }
    }

    public void Dispose() => Stop();
}
