# ClaudeBar live-sessions hook. Lee el evento de Claude Code de stdin (JSON) y lo
# reenvia como una linea JSON al Named Pipe \\.\pipe\claudebar. Fire-and-forget:
# si ClaudeBar no esta corriendo (pipe inexistente) o tarda, sale en silencio (exit 0)
# y NUNCA bloquea ni rompe la sesion de Claude. No escribe a stdout/stderr ni devuelve decision.
try {
  $raw = [Console]::In.ReadToEnd()
  if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
  $in = $raw | ConvertFrom-Json

  # Derivar status a partir del nombre del evento del hook.
  $ev = "$($in.hook_event_name)"
  switch ($ev) {
    'PreToolUse'        { $status = 'running_tool' }
    'PostToolUse'       { $status = 'processing' }
    'PermissionRequest' { $status = 'waiting_for_approval' }
    'Notification'      { $status = 'waiting_for_input' }
    'Stop'              { $status = 'waiting_for_input' }
    'SubagentStop'      { $status = 'processing' }
    'UserPromptSubmit'  { $status = 'processing' }
    'PreCompact'        { $status = 'compacting' }
    'SessionStart'      { $status = 'starting' }
    'SessionEnd'        { $status = 'ended' }
    default             { $status = 'processing' }
  }

  $payload = [ordered]@{
    session_id  = "$($in.session_id)"
    cwd         = "$($in.cwd)"
    pid         = $PID
    event       = $ev
    status      = $status
    tool        = "$($in.tool_name)"
    tool_use_id = "$($in.tool_use_id)"
    ts          = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
  }
  $json = ($payload | ConvertTo-Json -Compress)

  $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'claudebar', [System.IO.Pipes.PipeDirection]::Out)
  $pipe.Connect(200)   # timeout corto; si no hay servidor, lanza y caemos al catch
  $sw = New-Object System.IO.StreamWriter($pipe)
  $sw.AutoFlush = $true
  $sw.WriteLine($json)
  $sw.Dispose()
  $pipe.Dispose()
} catch {
  # ClaudeBar cerrado / pipe ocupado / timeout: ignorar por completo.
}
exit 0
