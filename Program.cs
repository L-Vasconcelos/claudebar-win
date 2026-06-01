using System.Text;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--report"))
        {
            RunReport().GetAwaiter().GetResult();
            return;
        }

        if (args.Contains("--render-test"))
        {
            ApplicationConfiguration.Initialize();
            RunRenderTest().GetAwaiter().GetResult();
            return;
        }

        if (args.Contains("--dump-menu"))
        {
            Console.WriteLine(TrayAppContext.DescribeMenu());
            return;
        }

        if (args.Contains("--db-test"))
        {
            RunDbTest();
            return;
        }

        if (args.Contains("--render-demo"))
        {
            ApplicationConfiguration.Initialize();
            RunRenderDemo();
            return;
        }

        if (args.Contains("--notify-demo"))
        {
            ApplicationConfiguration.Initialize();
            RunNotifyDemo();
            return;
        }

        if (args.Contains("--hook-test"))
        {
            RunHookTest();
            return;
        }

        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(initiallyOwned: true, "ClaudeBarWin_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(TrayAppContext.ShowSignalName);
                ev.Set();
            }
            catch { }
            return;
        }

        using var ctx = new TrayAppContext();
        Application.Run(ctx);
    }

    private static void RunDbTest()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "claudebar-dbtest.db");
        try { File.Delete(tmp); } catch { }
        var store = new UsageHistoryStore(tmp);
        var u = new RealUsage
        {
            FiveHour = new UsageWindow(42, DateTimeOffset.UtcNow.AddHours(2)),
            SevenDay = new UsageWindow(25, DateTimeOffset.UtcNow.AddDays(3))
        };
        store.Append(u, DateTime.UtcNow);
        int rows = store.Count();
        var pts = store.QueryPercent(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5), 100);
        string line = $"DB_TEST ok rows={rows} points={pts.Count} " +
            $"5h={pts.FirstOrDefault()?.FivePct} 7d={pts.FirstOrDefault()?.SevenPct}";
        Console.WriteLine(line);
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "claudebar-dbtest.txt"), line); } catch { }
        try { File.Delete(tmp); } catch { }
    }

    private static void RunHookTest()
    {
        // Requiere una instancia de ClaudeBar corriendo con sesiones en vivo activadas.
        var seq = new (string ev, string status, string tool)[]
        {
            ("SessionStart", "starting", ""),
            ("PreToolUse", "running_tool", "Bash"),
            ("PermissionRequest", "waiting_for_approval", "Write"),
            ("PostToolUse", "processing", "Write"),
            ("Stop", "waiting_for_input", ""),
        };
        foreach (var (ev, status, tool) in seq)
        {
            var json = $"{{\"session_id\":\"hook-test\",\"cwd\":\"C:\\\\Users\\\\zorro\\\\Proyectos\\\\demo\",\"pid\":{Environment.ProcessId},\"event\":\"{ev}\",\"status\":\"{status}\",\"tool\":\"{tool}\",\"ts\":0}}";
            try
            {
                using var c = new System.IO.Pipes.NamedPipeClientStream(".", "claudebar", System.IO.Pipes.PipeDirection.Out);
                c.Connect(500);
                using var w = new StreamWriter(c) { AutoFlush = true };
                w.WriteLine(json);
                Console.WriteLine($"sent {ev}/{status}");
            }
            catch (Exception ex) { Console.WriteLine($"FAIL {ev}: {ex.Message} (¿esta ClaudeBar corriendo con sesiones en vivo ON?)"); }
            System.Threading.Thread.Sleep(1500);
        }
    }

    private static async Task<AppSnapshot> BuildSnapshotAsync(AppConfig cfg)
    {
        var api = new UsageApiClient();
        var parser = new TranscriptParser();
        var now = DateTime.UtcNow;

        var result = await api.FetchAsync();
        WindowStats? spend = null;
        if (cfg.ShowSpendEstimate)
        {
            var window = TimeSpan.FromDays(cfg.SpendWindowDays);
            spend = UsageAggregator.Build(parser.Read(now - window), now, window, window).Week;
        }
        HealthStatus? health = cfg.ShowHealth ? await StatusClient.FetchAsync() : null;

        PaceResult? paceFive = null, paceSeven = null;
        if (result.Usage is not null)
        {
            var recent = new UsageHistoryStore().RecentForRate(now.AddHours(-6));
            (paceFive, paceSeven) = PaceCalculator.Compute(result.Usage, recent, now);
        }

        return new AppSnapshot
        {
            Usage = result.Usage,
            LatestState = result.State,
            UsageAtUtc = now,
            Spend = spend,
            SpendDays = cfg.SpendWindowDays,
            Health = health,
            PaceFive = paceFive,
            PaceSeven = paceSeven
        };
    }

    private static async Task RunReport()
    {
        var cfg = AppConfig.Load();
        var plan = CredentialsReader.Read();
        var snap = await BuildSnapshotAsync(cfg);

        var sb = new StringBuilder();
        sb.AppendLine("=== ClaudeBar report ===");
        sb.AppendLine($"Plan   : {plan.Display}");
        sb.AppendLine($"State  : {snap.LatestState}");
        if (snap.Usage is { } u)
        {
            sb.AppendLine();
            sb.AppendLine($"5h  : {Bar(u.FiveHour)}");
            sb.AppendLine($"7d  : {Bar(u.SevenDay)}");
            if (u.SevenDayOpus is not null) sb.AppendLine($"7d Opus  : {Bar(u.SevenDayOpus)}");
            if (u.SevenDaySonnet is not null) sb.AppendLine($"7d Sonnet: {Bar(u.SevenDaySonnet)}");
            sb.AppendLine($"extra usage enabled: {u.ExtraUsageEnabled}");
        }
        sb.AppendLine();
        sb.AppendLine($"-- pace --");
        AppendPace(sb, "5h", snap.PaceFive);
        AppendPace(sb, "7d", snap.PaceSeven);
        if (snap.Spend is { } s)
        {
            sb.AppendLine();
            sb.AppendLine($"-- estimated spend ({snap.SpendDays}d, API-equiv) --");
            sb.AppendLine($"  total: ${s.CostUsd:0.00}  ({s.Messages} turns)");
            foreach (var kv in s.CostByModel.OrderByDescending(k => k.Value))
                sb.AppendLine($"     {kv.Key,-7}: ${kv.Value:0.00}");
        }

        var text = sb.ToString();
        Console.WriteLine(text);
        try
        {
            var outPath = Path.Combine(Path.GetTempPath(), "claudebar-report.txt");
            File.WriteAllText(outPath, text);
            Console.WriteLine($"(written to {outPath})");
        }
        catch { }
    }

    private static void AppendPace(StringBuilder sb, string win, PaceResult? p)
    {
        if (p is null) { sb.AppendLine($"  {win}: —"); return; }
        string eta = p.EtaUtc is { } e ? e.ToLocalTime().ToString("ddd HH:mm") : "—";
        sb.AppendLine($"  {win}: ritmo {p.PaceRatio * 100:0}% · eta {eta} · " +
            $"exhausts_before_reset={p.ExhaustsBeforeReset} · {p.Status}");
    }

    private static string Bar(UsageWindow? w)
    {
        if (w is null) return "—";
        string cd = UsageFormat.Countdown(w.ResetsAt, "resetting…");
        return cd.Length > 0 ? $"{w.UtilizationPct:0.#}%  (resets in {cd})" : $"{w.UtilizationPct:0.#}%";
    }

    /// <summary>Fires the four milestone notifications in sequence (🟢→🔴) so the
    /// user can see how they escalate, then exits. Diagnostic / demo only.</summary>
    private static void RunNotifyDemo()
    {
        var samples = new (int pct, string dot, ToolTipIcon icon, UsageStatus st)[]
        {
            (25, "🟢", ToolTipIcon.Info, UsageStatus.Ok),
            (50, "🟡", ToolTipIcon.Warning, UsageStatus.Ok),
            (75, "🟠", ToolTipIcon.Warning, UsageStatus.Warn),
            (95, "🔴", ToolTipIcon.Error, UsageStatus.Critical)
        };

        var tray = new NotifyIcon { Visible = true, Text = "ClaudeBar demo" };
        Icon? cur = null;
        var ctx = new ApplicationContext();

        void Show(int idx)
        {
            var s = samples[idx];
            var ic = TrayIconRenderer.Render(s.pct, Theme.StatusColor(Theme.Dark, s.st));
            tray.Icon = ic;
            cur?.Dispose();
            cur = ic;
            tray.BalloonTipIcon = s.icon;
            tray.BalloonTipTitle = $"{s.dot} Claude {s.pct}%+ de cuota usada";
            tray.BalloonTipText = "Demo · 5h 24% · 7d 24%  (Max · Max 5x)";
            tray.ShowBalloonTip(4000);
        }

        int i = 0;
        Show(i++);
        var t = new System.Windows.Forms.Timer { Interval = 4500 };
        t.Tick += (_, _) =>
        {
            if (i < samples.Length) Show(i++);
            else { t.Stop(); tray.Visible = false; tray.Dispose(); cur?.Dispose(); ctx.ExitThread(); }
        };
        t.Start();
        Application.Run(ctx);
    }

    // ---- README screenshots from synthetic demo data (no personal usage) ----

    private static AppSnapshot DemoSnapshot(DateTime now)
    {
        var usage = new RealUsage
        {
            FiveHour = new UsageWindow(62, new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero)),
            SevenDay = new UsageWindow(84, new DateTimeOffset(now.AddDays(2).AddHours(6), TimeSpan.Zero)),
            SevenDaySonnet = new UsageWindow(12, new DateTimeOffset(now.AddDays(2), TimeSpan.Zero)),
            ExtraUsageEnabled = false
        };
        var spend = new WindowStats { CostUsd = 456.8, Messages = 1234 };
        spend.CostByModel["Opus"] = 420.50;
        spend.CostByModel["Sonnet"] = 35.20;
        spend.CostByModel["Haiku"] = 1.10;

        var paceFive = new PaceResult("5h", 62, 1.30,
            new DateTimeOffset(now.AddHours(1).AddMinutes(10), TimeSpan.Zero),
            new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero), true, PaceStatus.Critical);
        var paceSeven = new PaceResult("7d", 84, 0.95, null,
            new DateTimeOffset(now.AddDays(2).AddHours(6), TimeSpan.Zero), false, PaceStatus.Ok);

        return new AppSnapshot
        {
            Usage = usage, LatestState = UsageFetchState.Ok, UsageAtUtc = now,
            Spend = spend, SpendDays = 7,
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            PaceFive = paceFive, PaceSeven = paceSeven
        };
    }

    private static List<HistoryBucket> DemoBuckets(DateTime now)
    {
        var list = new List<HistoryBucket>();
        var rnd = new Random(7);
        for (int i = 0; i < 12; i++)
        {
            double op = 8 + i * 2.6 + 6 * Math.Abs(Math.Sin(i * 0.9)) + rnd.NextDouble() * 4; // rising, peak on the right
            double so = rnd.NextDouble() * 7;
            double ha = rnd.NextDouble() * 1.5;
            var t = now.AddHours(-5 * (12 - i)).ToLocalTime();
            list.Add(new HistoryBucket(t, t.ToString("ddd HH'h'"), op, so, ha, 0));
        }
        return list;
    }

    private static List<PctPoint> DemoPct(DateTime now)
    {
        var list = new List<PctPoint>();
        const int n = 80;
        double stepMin = 7.0 * 24 * 60 / n;
        for (int i = 0; i < n; i++)
        {
            var t = now.AddMinutes(-(n - i) * stepMin);
            double seven = 84.0 * i / (n - 1);          // weekly ramp to 84%
            double fiveSaw = (i % 16) / 16.0 * 72;       // 5h sawtooth
            list.Add(new PctPoint(t, fiveSaw, seven));
        }
        return list;
    }

    private static void RunRenderDemo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "claudebar-demo");
        Directory.CreateDirectory(dir);
        var now = DateTime.UtcNow;
        var snap = DemoSnapshot(now);
        var buckets = DemoBuckets(now);
        var pct = DemoPct(now);
        var plan = new PlanInfo("max", "default_claude_max_5x");

        var shots = new (string theme, string mode, ChartRange range, string file)[]
        {
            ("dark", "spend", ChartRange.Hours5, "dashboard-dark.png"),
            ("light", "spend", ChartRange.Hours5, "dashboard-light.png"),
            ("cli", "percent", ChartRange.Week1, "dashboard-cli.png")
        };
        foreach (var (theme, mode, range, file) in shots)
        {
            var cfg = new AppConfig
            {
                Theme = theme, ChartMode = mode, ChartPctWindow = "7d", Language = "en",
                ShowSpendEstimate = true, ShowHealth = true, ShowChart = true
            };
            using var form = new DashboardForm();
            form.PrepareForRender(snap, cfg, plan, buckets, pct, range);
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(Path.Combine(dir, file));
        }

        // Tray icon badges by status.
        var samples = new (int pct, UsageStatus st)[]
        { (42, UsageStatus.Ok), (78, UsageStatus.Warn), (95, UsageStatus.Critical), (130, UsageStatus.Critical) };
        const int scale = 3, pad = 10, icon = 32;
        using (var strip = new Bitmap((icon * scale + pad) * samples.Length + pad, icon * scale + pad * 2))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(45, 45, 48));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int x = pad;
            foreach (var s in samples)
            {
                using var ic = TrayIconRenderer.Render(s.pct, Theme.StatusColor(Theme.Dark, s.st));
                using var b = ic.ToBitmap();
                g.DrawImage(b, new Rectangle(x, pad, icon * scale, icon * scale));
                x += icon * scale + pad;
            }
            strip.Save(Path.Combine(dir, "tray-icons.png"));
        }

        Console.WriteLine(dir);
    }

    private static async Task RunRenderTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "claudebar-render");
        Directory.CreateDirectory(dir);

        var samples = new (int pct, UsageStatus st)[]
        {
            (42, UsageStatus.Ok), (78, UsageStatus.Warn), (95, UsageStatus.Critical), (130, UsageStatus.Critical)
        };
        const int scale = 3, pad = 10, icon = 32;
        using (var strip = new Bitmap((icon * scale + pad) * samples.Length + pad, icon * scale + pad * 2))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(45, 45, 48));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int x = pad;
            foreach (var s in samples)
            {
                using var ic = TrayIconRenderer.Render(s.pct, Theme.StatusColor(Theme.Dark, s.st));
                using var bmp = ic.ToBitmap();
                g.DrawImage(bmp, new Rectangle(x, pad, icon * scale, icon * scale));
                x += icon * scale + pad;
            }
            strip.Save(Path.Combine(dir, "icons.png"));
        }

        var cfg = AppConfig.Load();
        var plan = CredentialsReader.Read();
        var snap = await BuildSnapshotAsync(cfg);

        var now = DateTime.UtcNow;
        var records = new TranscriptParser().Read(now - UsageHistory.Lookback(ChartRange.Hours5));
        var buckets = UsageHistory.Build(records, ChartRange.Hours5, now);
        var pct = new UsageHistoryStore().QueryPercent(now - UsageHistory.Lookback(ChartRange.Hours5), now, 120);
        using (var form = new DashboardForm())
        {
            form.PrepareForRender(snap, cfg, plan, buckets, pct, ChartRange.Hours5);
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(Path.Combine(dir, "data.png"));

            // Vista de ajustes (mismo form): cambia el modo y reajusta el alto.
            form.ShowSettings();
            using var bmpS = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmpS, new Rectangle(0, 0, form.Width, form.Height));
            bmpS.Save(Path.Combine(dir, "settings.png"));
        }

        // Mascota grande: sesiones en vivo ON + tamaño "large" para verla en la cabecera.
        cfg.LiveSessionsEnabled = true;
        cfg.ShowMascot = true;
        cfg.MascotSize = "large";
        using (var formL = new DashboardForm())
        {
            formL.PrepareForRender(snap, cfg, plan, buckets, pct, ChartRange.Hours5);
            using var bmpL = new Bitmap(formL.Width, formL.Height);
            formL.DrawToBitmap(bmpL, new Rectangle(0, 0, formL.Width, formL.Height));
            bmpL.Save(Path.Combine(dir, "mascot-large.png"));
        }

        Console.WriteLine("rendered data.png + settings.png + mascot-large.png");
        Console.WriteLine(dir);
    }
}
