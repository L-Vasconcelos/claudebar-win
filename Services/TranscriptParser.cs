using System.Text.Json;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Reads Claude Code transcript files (~/.claude/projects/**/*.jsonl) and extracts
/// per-turn token usage. Only files modified inside the window are touched, and only
/// recent lines are JSON-parsed, so it stays cheap even with hundreds of MB on disk.
/// </summary>
public sealed class TranscriptParser
{
    private readonly string _projectsRoot;

    public TranscriptParser(string? projectsRoot = null)
    {
        _projectsRoot = projectsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
    }

    public string ProjectsRoot => _projectsRoot;

    public IReadOnlyList<UsageRecord> Read(DateTime sinceUtc)
    {
        var records = new List<UsageRecord>();
        if (!Directory.Exists(_projectsRoot)) return records;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Grace period: a file's mtime is its last line; older writes can still hold in-window lines.
        var fileCutoff = sinceUtc.AddHours(-2);

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(_projectsRoot, "*.jsonl", SearchOption.AllDirectories); }
        catch { return records; }

        foreach (var file in files)
        {
            DateTime mtime;
            try { mtime = File.GetLastWriteTimeUtc(file); }
            catch { continue; }
            if (mtime < fileCutoff) continue;

            try
            {
                foreach (var line in EnumerateLines(file))
                {
                    if (line.Length < 40) continue;
                    if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
                    if (!line.Contains("\"assistant\"", StringComparison.Ordinal)) continue;

                    var rec = TryParseLine(line, sinceUtc, seen);
                    if (rec is not null) records.Add(rec);
                }
            }
            catch
            {
                // locked or unreadable file: skip
            }
        }

        return records;
    }

    private static IEnumerable<string> EnumerateLines(string file)
    {
        // Open with shared read so we don't fight Claude Code writing the file.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return line;
    }

    private static UsageRecord? TryParseLine(string line, DateTime sinceUtc, HashSet<string> seen)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "assistant")
                return null;
            if (!root.TryGetProperty("message", out var msg))
                return null;
            if (!msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("timestamp", out var tsEl)
                || !DateTimeOffset.TryParse(tsEl.GetString(), out var tso))
                return null;

            var tsUtc = tso.UtcDateTime;
            if (tsUtc < sinceUtc) return null;

            // Dedup by message id + requestId (streaming can repeat a turn).
            string msgId = msg.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            string reqId = root.TryGetProperty("requestId", out var rqEl) ? rqEl.GetString() ?? "" : "";
            string key = msgId + "|" + reqId;
            if (key.Length > 1 && !seen.Add(key)) return null;

            string model = msg.TryGetProperty("model", out var mEl) ? mEl.GetString() ?? "" : "";

            long input = GetLong(usage, "input_tokens");
            long output = GetLong(usage, "output_tokens");
            long cacheRead = GetLong(usage, "cache_read_input_tokens");

            long c5, c1 = 0;
            if (usage.TryGetProperty("cache_creation", out var cc) && cc.ValueKind == JsonValueKind.Object)
            {
                c5 = GetLong(cc, "ephemeral_5m_input_tokens");
                c1 = GetLong(cc, "ephemeral_1h_input_tokens");
            }
            else
            {
                c5 = GetLong(usage, "cache_creation_input_tokens");
            }

            return new UsageRecord(tsUtc, model, input, output, c5, c1, cacheRead);
        }
        catch
        {
            return null;
        }
    }

    private static long GetLong(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
