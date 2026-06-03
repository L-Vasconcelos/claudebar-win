using System.Text;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.Services.Motion;
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

        if (args.Contains("--render-gif"))
        {
            ApplicationConfiguration.Initialize();
            RunRenderGif();
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
        sb.AppendLine($"{plan.Display}");
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
        var trayBadges = new (int pct, string dot, ToolTipIcon icon, UsageStatus st)[]
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
            var s = trayBadges[idx];
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
            if (i < trayBadges.Length) Show(i++);
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

        var paceFive = new PaceResult("5h", 62, 1.30, 47.7,
            new DateTimeOffset(now.AddHours(1).AddMinutes(10), TimeSpan.Zero),
            new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero), true, PaceStatus.Critical);
        var paceSeven = new PaceResult("7d", 84, 0.95, 88.4, null,
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
        var trayBadges = new (int pct, UsageStatus st)[]
        { (42, UsageStatus.Ok), (78, UsageStatus.Warn), (95, UsageStatus.Critical), (130, UsageStatus.Critical) };
        const int scale = 3, pad = 10, icon = 32;
        using (var strip = new Bitmap((icon * scale + pad) * trayBadges.Length + pad, icon * scale + pad * 2))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(45, 45, 48));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int x = pad;
            foreach (var s in trayBadges)
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

    // ---- GIF frame sequences for the README microinteraction animations (F3) ----
    // Like --render-demo (synthetic data, no personal usage) but instead of single PNGs it dumps
    // NUMBERED frame sequences by sweeping the deterministic RenderMotionOverride across the animation
    // timeline. ffmpeg then assembles each folder into a looping .gif (see build-gifs in the README).
    //   apertura/  — panel open: fade + staggered section entry + bar/number tween, tSinceOpen 0→440 ms.
    //   mascota/   — mascot life (Processing): braille spinner + blink jitter + playful verb, elapsed 0→2700 ms.
    //   hover/     — hover highlight fading in over the Quota section, intensity 0→1.
    //   celebracion/ — reset celebration flash ("✓ quota renewed") + Happy mood pulsing in.
    // Frames go to %TEMP%\claudebar-gif\<seq>\frame_###.png; only the FINAL .gif belongs in assets/.
    private static void RunRenderGif()
    {
        var root = Path.Combine(Path.GetTempPath(), "claudebar-gif");
        var now = DateTime.UtcNow;
        var snap = DemoSnapshot(now);
        var buckets = DemoBuckets(now);
        var pct = DemoPct(now);
        var plan = new PlanInfo("max", "default_claude_max_5x");

        // Config de marketing: tema oscuro, mascota grande viva, Spend+Chart desplegados (panel lleno).
        AppConfig Cfg() => new()
        {
            Theme = "dark", ChartMode = "spend", ChartPctWindow = "7d", Language = "en",
            ShowSpendEstimate = true, ShowHealth = true, ShowChart = true,
            LiveSessionsEnabled = true, ShowMascot = true, MascotSize = "large",
            CollapsedSpend = false, CollapsedChart = false,
        };

        var processing = new LiveSessionsView { GlobalPhase = SessionPhase.Processing, ActiveCount = 1 };

        // Renderiza UN fotograma a PNG. Si fadeAlpha < 1, mezcla el contenido contra el fondo del tema
        // para que el fade de apertura SE VEA (DrawToBitmap captura el contenido pero no el Opacity de la
        // ventana en capas). devuelve el tamaño del panel para reusarlo entre fotogramas de la misma seq.
        Size Shot(string seqDir, int idx, AppConfig cfg, DashboardForm.RenderMotionOverride mo,
                  LiveSessionsView live, double fadeAlpha)
        {
            using var form = new DashboardForm();
            form.PrepareForRender(snap, cfg, plan, buckets, pct, ChartRange.Hours5, mo, live);
            int fw = form.Width, fh = form.Height;
            using var content = new Bitmap(fw, fh);
            form.DrawToBitmap(content, new Rectangle(0, 0, fw, fh));

            var file = Path.Combine(seqDir, $"frame_{idx:000}.png");
            if (fadeAlpha >= 0.999)
            {
                content.Save(file);
            }
            else
            {
                // Fade real: lienzo con el fondo del tema + el contenido a alfa = fadeAlpha encima.
                using var canvas = new Bitmap(fw, fh);
                using var g = Graphics.FromImage(canvas);
                g.Clear(form.BackColor);
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)Math.Clamp(fadeAlpha, 0, 1) };
                using var ia = new System.Drawing.Imaging.ImageAttributes();
                ia.SetColorMatrix(cm);
                g.DrawImage(content, new Rectangle(0, 0, fw, fh), 0, 0, fw, fh, GraphicsUnit.Pixel, ia);
                canvas.Save(file);
            }
            return new Size(fw, fh);
        }

        string Seq(string name)
        {
            var d = Path.Combine(root, name);
            if (Directory.Exists(d)) Directory.Delete(d, true);
            Directory.CreateDirectory(d);
            return d;
        }

        // 1) APERTURA: fade (OutQuad sobre FadeMs) + stagger de secciones + tween de barras/números.
        //    Barrido tSinceOpen 0→440 ms (cubre fade 120, tween 220 y el stagger de todas las secciones)
        //    + unos fotogramas de "hold" en el estado asentado para que el loop respire antes de reabrir.
        {
            var dir = Seq("apertura");
            int i = 0;
            const double endMs = 440, stepMs = 20;
            for (double t = 0; t <= endMs + 0.001; t += stepMs)
            {
                double fade = Motion.FadeMs <= 0 ? 1.0 : Easing.OutQuad(Math.Clamp(t / Motion.FadeMs, 0, 1));
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(TSinceOpenMs: t, MascotElapsedMs: t), processing, fade);
            }
            for (int h = 0; h < 12; h++) // hold asentado
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(TSinceOpenMs: endMs, MascotElapsedMs: endMs), processing, 1.0);
            Console.WriteLine($"apertura: {i} frames -> {dir}");
        }

        // 2) MASCOTA: vida en Processing. tSinceOpen alto (panel asentado) y barrido de MascotElapsedMs
        //    0→2700 ms = 3 ciclos completos del spinner (90 ms × 10 glifos) ⇒ LOOP perfecto del braille.
        {
            var dir = Seq("mascota");
            int i = 0;
            const double endMs = 2700, stepMs = 30; // 90 fotogramas, 30 ms/frame
            for (double t = 0; t < endMs - 0.001; t += stepMs)
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(
                        TSinceOpenMs: 4000, MascotElapsedMs: t, MascotMood: Mood.Focused), processing, 1.0);
            Console.WriteLine($"mascota: {i} frames -> {dir}");
        }

        // 3) HOVER: realce de la sección de Cuota apareciendo. Barrido manual de la intensidad del
        //    fade-in (OutQuad sobre FadeMs) reusando el override de hover: pasamos por una rampa de
        //    fotogramas con la clave activa y, para el inicio "sin realce", la clave a null.
        {
            var dir = Seq("hover");
            int i = 0;
            // Antes del hover: panel asentado, sin realce.
            for (int h = 0; h < 6; h++)
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(TSinceOpenMs: 4000, MascotElapsedMs: 4000), processing, 1.0);
            // Hover ON (el override fuerza intensidad plena); mantenemos unos fotogramas para que se lea.
            for (int h = 0; h < 18; h++)
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(
                        TSinceOpenMs: 4000, HoveredKey: "sec:quota", MascotElapsedMs: 4000 + h * 30), processing, 1.0);
            Console.WriteLine($"hover: {i} frames -> {dir}");
        }

        // 4) CELEBRACIÓN: destello "✓ cuota renovada" + humor Happy. Barrido corto de MascotElapsed para
        //    que la mascota siga viva durante el destello; loop tras unos fotogramas de hold.
        {
            var dir = Seq("celebracion");
            int i = 0;
            for (int h = 0; h < 30; h++)
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(
                        TSinceOpenMs: 4000, MascotElapsedMs: h * 30, MascotMood: Mood.Happy, Celebrating: true),
                    processing, 1.0);
            Console.WriteLine($"celebracion: {i} frames -> {dir}");
        }

        Console.WriteLine(root);
    }

    private static async Task RunRenderTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "claudebar-render");
        Directory.CreateDirectory(dir);

        var trayBadges = new (int pct, UsageStatus st)[]
        {
            (42, UsageStatus.Ok), (78, UsageStatus.Warn), (95, UsageStatus.Critical), (130, UsageStatus.Critical)
        };
        const int scale = 3, pad = 10, icon = 32;
        using (var strip = new Bitmap((icon * scale + pad) * trayBadges.Length + pad, icon * scale + pad * 2))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(45, 45, 48));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int x = pad;
            foreach (var s in trayBadges)
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

        // ---- Microinteracciones congeladas (Tarea 8): fade+stagger+tween a 0/90/200 ms, hover sobre
        //      una fila, mascota en varias fases/humores, celebración y una pasada reduce-motion. ----
        RenderMotionFrames(dir, snap, plan, buckets, pct);

        // Tira de badges del icono de bandeja para QA visual: matriz barra clara/oscura × 3 formas
        // (Ok/Warn/Critical) × estado fresco/stale, a 48px (nativo) y 16px (tamaño real a 100% DPI).
        // Valida legibilidad y contraste del texto adaptativo + el estado por forma a 16px.
        {
            const double warn = 70, crit = 90;
            // (etiqueta, %, estado, stale). El % elige el color por riesgo; la forma sigue al estado.
            var statuses = new (string label, int pct, UsageStatus st)[]
            {
                ("12%", 12, UsageStatus.Ok),
                ("68%", 68, UsageStatus.Warn),
                ("95%", 95, UsageStatus.Critical),
            };
            // Una columna por (estado × fresco/stale); dos filas por barra (oscura y clara).
            var cols = new List<(string label, int pct, UsageStatus st, bool stale)>();
            foreach (var s in statuses) cols.Add((s.label, s.pct, s.st, false));
            foreach (var s in statuses) cols.Add((s.label + " ·old", s.pct, s.st, true));
            cols.Add(("pend", 45, UsageStatus.Warn, false)); // badge "pending" sigue cubierto
            cols.Add(("99+", 120, UsageStatus.Critical, false));
            cols.Add(("err", 0, UsageStatus.Ok, false));     // marcador de fila de error

            var bars = new (string name, Color bg)[]
            {
                ("dark taskbar",  Color.FromArgb(0x2A, 0x2A, 0x2A)),
                ("light taskbar", Color.FromArgb(0xE8, 0xE8, 0xE8)),
            };

            const int cell = 64;
            const int rowH = 8 + 48 + 4 + 16 + 22; // 48px nativo + 16px real + etiqueta
            using var strip = new Bitmap(cols.Count * cell, bars.Length * rowH);
            using (var g2 = Graphics.FromImage(strip))
            {
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                using var lf = new Font("Segoe UI", 8f);
                for (int r = 0; r < bars.Length; r++)
                {
                    int rowY = r * rowH;
                    g2.SetClip(new Rectangle(0, rowY, strip.Width, rowH));
                    g2.Clear(bars[r].bg);
                    Color txt = ColorMath.Contrast(bars[r].bg);
                    using var labelBrush = new SolidBrush(txt);
                    for (int i = 0; i < cols.Count; i++)
                    {
                        var col = cols[i];
                        Icon ico = col.label == "err"
                            ? TrayIconRenderer.RenderError(Theme.Dark.Neutral)
                            : TrayIconRenderer.Render(col.pct, Theme.Dark, warn, crit, col.st, col.stale,
                                pending: col.label == "pend");
                        using var bm = ico.ToBitmap();
                        int cx = i * cell;
                        g2.DrawImage(bm, cx + (cell - 48) / 2, rowY + 8, 48, 48);          // 48px nativo
                        g2.DrawImage(bm, cx + (cell - 16) / 2, rowY + 8 + 48 + 4, 16, 16);  // 16px = barra real
                        g2.DrawString(col.label, lf, labelBrush, cx + 4, rowY + 8 + 48 + 4 + 16 + 2);
                        ico.Dispose();
                    }
                }
                g2.ResetClip();
            }
            strip.Save(Path.Combine(dir, "tray-badges.png"));
        }

        Console.WriteLine("rendered data.png + settings.png + mascot-large.png + tray-badges.png");
        Console.WriteLine("       + motion-0/90/200.png + hover.png + mascot-*.png + celebration.png + reduce-motion.png");
        Console.WriteLine(dir);
    }

    /// <summary>
    /// Genera los fotogramas FIJOS de las microinteracciones de F3 (Tarea 8) inyectando un
    /// <see cref="DashboardForm.RenderMotionOverride"/> determinista (sin reloj de UI): fade+stagger+tween
    /// a tSinceOpen = 0/90/200 ms, hover sobre una sección, la mascota en varias fases/humores, un
    /// destello de celebración y una pasada con reduce-motion (== estado final).
    /// </summary>
    private static void RenderMotionFrames(string dir, AppSnapshot snap, PlanInfo plan,
        List<HistoryBucket> buckets, List<PctPoint> pct)
    {
        // Config base con la mascota visible para ver también su vida en los fotogramas.
        AppConfig Base() => new()
        {
            Theme = "dark", ChartMode = "spend", ChartPctWindow = "7d", Language = "en",
            ShowSpendEstimate = true, ShowHealth = true, ShowChart = true,
            LiveSessionsEnabled = true, ShowMascot = true, MascotSize = "large"
        };

        void Shot(string file, AppConfig cfg, DashboardForm.RenderMotionOverride? mo, LiveSessionsView? live)
        {
            using var form = new DashboardForm();
            form.PrepareForRender(snap, cfg, plan, buckets, pct, ChartRange.Hours5, mo, live);
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(Path.Combine(dir, file));
        }

        // Mascota procesando (fase animada): spinner + verbo vivos en los tres instantes.
        var processing = new LiveSessionsView { GlobalPhase = SessionPhase.Processing, ActiveCount = 1 };

        // 1) Apertura a 0 / 90 / 200 ms: fade de opacidad + entrada escalonada + tween de barras/números.
        foreach (var (t, file) in new[] { (0.0, "motion-0.png"), (90.0, "motion-90.png"), (200.0, "motion-200.png") })
            Shot(file, Base(), new DashboardForm.RenderMotionOverride(TSinceOpenMs: t, MascotElapsedMs: t), processing);

        // 2) Hover sobre la sección de cuota (realce eased a intensidad plena), ya asentado (t alto).
        Shot("hover.png", Base(),
            new DashboardForm.RenderMotionOverride(TSinceOpenMs: 2000, HoveredKey: "sec:quota", MascotElapsedMs: 2000),
            processing);

        // 3) Mascota en varias fases/humores: animador a un elapsed que muestra spinner/verbo + humor forzado.
        var moods = new (SessionPhase phase, Mood mood, string file)[]
        {
            (SessionPhase.Processing,         Mood.Focused, "mascot-processing.png"),
            (SessionPhase.WaitingForApproval, Mood.Alert,   "mascot-waiting.png"),
            (SessionPhase.Idle,               Mood.Neutral, "mascot-idle.png"),
        };
        foreach (var (phase, mood, file) in moods)
        {
            var live = new LiveSessionsView { GlobalPhase = phase, ActiveCount = phase == SessionPhase.Idle ? 0 : 1 };
            // Bote de atención visible en la fase que pide atención (offset > 0).
            int bounce = phase.NeedsAttention() ? Motion.BounceAmplitudePx : 0;
            Shot(file, Base(),
                new DashboardForm.RenderMotionOverride(
                    TSinceOpenMs: 3000, MascotElapsedMs: 320, MascotMood: mood, MascotBounceOffsetY: bounce),
                live);
        }

        // 4) Celebración de reset (destello "✓ cuota renovada" + humor Happy).
        Shot("celebration.png", Base(),
            new DashboardForm.RenderMotionOverride(
                TSinceOpenMs: 3000, MascotElapsedMs: 200, MascotMood: Mood.Happy, Celebrating: true),
            processing);

        // 5) Reduce-motion ON: el gate único colapsa TODO a su estado final. Con override + ReduceMotion,
        //    fade=1, stagger=0, tween=target, sin spinner/jitter ni hover ⇒ idéntico al estado de hoy.
        var reduced = Base();
        reduced.ReduceMotion = true;
        Shot("reduce-motion.png", reduced,
            new DashboardForm.RenderMotionOverride(TSinceOpenMs: 90, HoveredKey: "sec:quota", MascotElapsedMs: 320, Celebrating: true),
            processing);
    }
}
