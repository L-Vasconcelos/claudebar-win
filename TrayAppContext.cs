using System.Diagnostics;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin;

public sealed class TrayAppContext : ApplicationContext
{
    public const string ShowSignalName = "ClaudeBarWin_Show";

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly UsageApiClient _api;
    /// <summary>Respaldo local (app de escritorio de Claude) cuando la API OAuth no responde.</summary>
    private readonly LocalPlanUsageReader _localUsage = new();
    private readonly TranscriptParser _parser;
    private readonly UsageHistoryStore _history;
    private readonly PlanInfo _plan;
    private DateTime _lastPruneUtc = DateTime.MinValue;
    private readonly DashboardForm _dashboard;
    private readonly EventWaitHandle _showSignal;

    private readonly SessionStore _sessions;
    private readonly SessionAggregator _sessionAgg;
    private readonly ForegroundDetector _foreground;
    private HookPipeServer? _pipe;

    private ToolStripMenuItem _miUpdate = null!;
    private SparkleUpdateService _updates = null!;
    private ToolStripMenuItem _miInstallHooks = null!;

    private AppConfig _config;
    private Strings _s;
    private Theme _theme;
    private string _menuLangCode;
    private Icon? _currentIcon;
    private Icon? _updateIcon; // icono PROPIO y estable para la UI de NetSparkle; se dispone solo al cerrar, nunca en el ciclo del tray

    private RealUsage? _lastUsage;
    private DateTime _lastUsageAtUtc;
    private AppSnapshot? _lastSnapshot;
    private bool _busy;

    private readonly Dictionary<int, bool> _fired = new();
    private bool _milestonesInitialised;
    private bool _paceFiredFive, _paceFiredSeven;

    public TrayAppContext()
    {
        _config = AppConfig.Load();
        _s = Localization.ForConfig(_config);
        _theme = ThemeResolver.Resolve(_config);
        _menuLangCode = CurrentLangCode();
        _api = new UsageApiClient();
        _parser = new TranscriptParser();
        _history = new UsageHistoryStore();
        _history.Prune();
        _plan = CredentialsReader.Read();

        // Seed from the persisted last-good reading so we show something immediately
        // on startup / while rate-limited, before the first successful fetch.
        if (UsageCache.Load() is { } cached)
        {
            _lastUsage = cached.ToRealUsage();
            _lastUsageAtUtc = cached.SavedAtUtc;
        }

        _dashboard = new DashboardForm();
        _ = _dashboard.Handle;
        _dashboard.SetHistoryProvider(BuildHistoryAsync);
        _dashboard.SetPercentProvider(BuildPercentAsync);
        _dashboard.ChartModeChanged += m => { var c = AppConfig.Load(); c.ChartMode = m; c.Save(); _config = c; };
        _dashboard.ChartWindowChanged += win => { var c = AppConfig.Load(); c.ChartPctWindow = win; c.Save(); _config = c; };
        _dashboard.Moved += p =>
        {
            var c = AppConfig.Load();
            c.DashboardPosition = "Custom";
            c.DashboardX = p.X;
            c.DashboardY = p.Y;
            c.Save();
            _config = c;
        };

        _sessions = new SessionStore();
        _sessionAgg = new SessionAggregator();
        _foreground = new ForegroundDetector();
        _dashboard.SetLiveSessionsProvider(() => _sessionAgg.BuildView(_sessions.Snapshot()));
        _dashboard.OverviewRangeChanged += _ => RefreshOverviewStats();
        _dashboard.SettingsChanged += a => MutateConfig(a);
        _dashboard.SpecialActionRequested += key =>
        {
            if (key == "special:importtheme") ImportItermColors();
            else if (key == "special:hooktoggle") ToggleHooks();
        };

        _sessions.Changed += OnSessionsChanged;

        if (_config.LiveSessionsEnabled) StartPipe();

        _currentIcon = TrayIconRenderer.RenderError(_theme.Neutral);
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "ClaudeBar…",
            Icon = _currentIcon,
            ContextMenuStrip = BuildMenu()
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleDashboard();
        };

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(15, _config.RefreshSeconds) * 1000 };
        _timer.Tick += async (_, _) => await RefreshAsync();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        new Thread(ShowSignalLoop) { IsBackground = true, Name = "ShowSignalListener" }.Start();

        // Icono PROPIO para las ventanas de NetSparkle: NO reutilizar _currentIcon, que UpdateUi
        // dispone/reemplaza en cada refresco (dejaría a NetSparkle con un Icon disposed → crash al abrir su ventana).
        _updateIcon = TrayIconRenderer.RenderError(_theme.Neutral);
        _updates = new SparkleUpdateService(_updateIcon);
        _updates.AvailabilityChanged += () =>
        {
            try { _dashboard.BeginInvoke(new Action(UpdateMenuChecks)); } catch { }
        };

        _ = RefreshAsync();
        _timer.Start();
        _updates.StartLoop();   // comprobación silenciosa al arrancar + cada 6 h
    }

    public static string DescribeMenu()
    {
        var s = Localization.Get("en");
        var sb = new System.Text.StringBuilder();
        // Menú minimal: toda la configuración vive en el panel de ajustes ⚙ del dashboard.
        sb.AppendLine(s.Dashboard);
        sb.AppendLine(s.Settings);
        sb.AppendLine(s.MenuLiveSessions);
        sb.AppendLine(s.CheckUpdates);
        sb.AppendLine(s.Exit);
        return sb.ToString();
    }

    private void ShowSignalLoop()
    {
        while (true)
        {
            try
            {
                _showSignal.WaitOne();
                if (_dashboard.IsHandleCreated)
                    _dashboard.BeginInvoke((Action)ShowDashboard);
            }
            catch
            {
                break;
            }
        }
    }

    // ---------- Menu ----------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };

        var miDash = new ToolStripMenuItem(_s.Dashboard);
        miDash.Click += (_, _) => ToggleDashboard();

        var miSettings = new ToolStripMenuItem(_s.Settings);
        miSettings.Click += (_, _) => { ShowDashboard(); _dashboard.ShowSettings(); };

        // Activación de sesiones en vivo (instala/desinstala los hooks): única acción de "config"
        // que sobrevive en el menú porque necesita el instalador. El resto vive en el panel ⚙.
        _miInstallHooks = new ToolStripMenuItem(_s.MenuLiveSessions);
        _miInstallHooks.Click += (_, _) => ToggleHooks();

        _miUpdate = new ToolStripMenuItem(_s.CheckUpdates);
        _miUpdate.Click += async (_, _) => await _updates.CheckInteractive();

        var miExit = new ToolStripMenuItem(_s.Exit);
        miExit.Click += (_, _) => ExitApp();

        menu.Items.Add(miDash);
        menu.Items.Add(miSettings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miInstallHooks);
        menu.Items.Add(_miUpdate);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miExit);

        menu.Opening += (_, _) => UpdateMenuChecks();
        _dashboard?.AttachContextMenu(menu); // right-click on the panel opens the same menu
        return menu;
    }

    private void UpdateMenuChecks()
    {
        // Live sessions: el texto refleja si los hooks están instalados (activar / desactivar).
        _miInstallHooks.Checked = HookInstaller.IsInstalled();
        _miInstallHooks.Text = HookInstaller.IsInstalled() ? _s.MenuUninstallHooks : _s.MenuInstallHooks;

        // Disponibilidad de actualización.
        _miUpdate.Text = _updates?.AvailableTag is { } tag
            ? string.Format(_s.UpdateAvailableFmt, tag)
            : _s.CheckUpdates;
    }

    // ---------- Live sessions (hook -> Named Pipe) ----------

    private void StartPipe()
    {
        if (_pipe is not null) return;
        _pipe = new HookPipeServer();
        _pipe.EventReceived += e => _sessions.Apply(e, DateTime.UtcNow);
        _pipe.Start();
    }

    private void StopPipe()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    private void OnSessionsChanged()
    {
        // Prune perezoso (cada cambio comprobamos TTL de 10 min).
        _sessions.Prune(DateTime.UtcNow, TimeSpan.FromMinutes(10));

        // Avisos: diff + supresión por foco.
        var snap = _sessions.Snapshot();
        foreach (var s in _sessionAgg.DiffNotifications(snap, DateTime.UtcNow))
        {
            if (_config.SuppressWhenFocused && _foreground.IsSessionForeground(s.Pid)) continue;
            NotifySession(s);
        }

        // Refrescar mascota/lista + icono en el hilo de UI.
        try { _dashboard.BeginInvoke(new Action(() =>
        {
            _dashboard.OnLiveSessionsChanged();
            RefreshTrayIcon();
        })); } catch { }
    }

    private void NotifySession(Models.SessionState s)
    {
        if (!_config.NotificationsEnabled) return;
        var fmt = s.Phase == Models.SessionPhase.WaitingForApproval
            ? _s.NotifWaitingApprovalFmt : _s.NotifWaitingInputFmt;
        var icon = s.Phase == Models.SessionPhase.WaitingForApproval
            ? ToolTipIcon.Warning : ToolTipIcon.Info;
        try { _tray.ShowBalloonTip(5000, _s.LiveSessionsTitle, string.Format(fmt, s.ProjectName), icon); } catch { }
    }

    private bool LiveAttentionPending()
        => _config.LiveSessionsEnabled
           && _sessionAgg.BuildView(_sessions.Snapshot()).GlobalPhase.NeedsAttention();

    private void RefreshTrayIcon()
    {
        if (_lastSnapshot is null || _lastUsage is null) return;
        UpdateUi(_lastSnapshot); // recalcula icono; UpdateUi ya pinta con _lastSnapshot
    }

    /// <summary>Acción especial del panel de ajustes (fila "Importar .itermcolors"): abre el diálogo,
    /// importa la paleta y la aplica como tema "imported". Necesita UI (OpenFileDialog), por eso vive aquí.</summary>
    private void ImportItermColors()
    {
        try
        {
            using var dlg = new OpenFileDialog
            {
                Title = _s.ImportTheme,
                Filter = "iTerm colors (*.itermcolors)|*.itermcolors|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            var colors = ItermColorsImporter.TryImport(dlg.FileName);
            if (colors is null) return;
            MutateConfig(c => { c.ImportedTheme = colors; c.Theme = "imported"; });
        }
        catch { }
    }

    private void ToggleHooks()
    {
        // Instalar/quitar hooks toca la configuración GLOBAL de Claude Code (~/.claude/settings.json),
        // que puede estar usándose en sesiones 24/7. Por eso ambos sentidos piden confirmación explícita
        // (botón por defecto "No" → no se dispara con un Enter/clic accidental).
        if (HookInstaller.IsInstalled())
        {
            var confirm = MessageBox.Show(
                "Vas a DESACTIVAR las sesiones en vivo de ClaudeBar.\n\n" +
                "Se quitarán los hooks de tu configuración global de Claude Code (~/.claude/settings.json) " +
                "y la mascota y los avisos de estado dejarán de actualizarse.\n\n" +
                "Se hará una copia de seguridad de settings.json antes de tocarlo.\n\n" +
                "¿Seguro que quieres desactivarlas?",
                _s.MenuLiveSessions, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            HookInstaller.Uninstall(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            MutateConfig(c => c.LiveSessionsEnabled = false);
            StopPipe();
            _tray.ShowBalloonTip(4000, _s.LiveSessionsTitle, _s.HooksRemoved, ToolTipIcon.Info);
            return;
        }

        var ok = MessageBox.Show(
            "Vas a ACTIVAR las sesiones en vivo de ClaudeBar.\n\n" +
            "Esto añadirá hooks a tu configuración global de Claude Code (~/.claude/settings.json) para que " +
            "ClaudeBar reciba el estado de tus sesiones (mascota + avisos de bandeja).\n\n" +
            "Se hará una copia de seguridad de settings.json antes de modificarlo.\n\n" +
            "¿Continuar?",
            _s.MenuLiveSessions, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (ok != DialogResult.Yes) return;

        var backup = HookInstaller.Install(HookScript.Contents(), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        MutateConfig(c => c.LiveSessionsEnabled = true);
        StartPipe();
        _tray.ShowBalloonTip(5000, _s.LiveSessionsTitle, string.Format(_s.HooksInstalledFmt, backup), ToolTipIcon.Info);
    }

    private string CurrentLangCode() =>
        string.IsNullOrEmpty(_config.Language) || _config.Language == "system"
            ? Localization.ResolveSystemCode()
            : _config.Language;

    private void RebuildMenu()
    {
        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = BuildMenu();
        old?.Dispose();
        _menuLangCode = CurrentLangCode();
    }

    private void MutateConfig(Action<AppConfig> change)
    {
        var c = AppConfig.Load();
        change(c);
        c.Save();
        _config = c;
        _s = Localization.ForConfig(c);
        _theme = ThemeResolver.Resolve(c);

        int desired = Math.Max(15, c.RefreshSeconds) * 1000;
        if (_timer.Interval != desired) _timer.Interval = desired;

        if (CurrentLangCode() != _menuLangCode && _dashboard.IsHandleCreated)
            _dashboard.BeginInvoke((Action)RebuildMenu);

        if (_config.LiveSessionsEnabled && _pipe is null) StartPipe();
        else if (!_config.LiveSessionsEnabled && _pipe is not null) StopPipe();

        _ = RefreshAsync();
    }

    // ---------- Data ----------

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _config = AppConfig.Load();
            _s = Localization.ForConfig(_config);
            _theme = ThemeResolver.Resolve(_config);
            int desiredInterval = Math.Max(15, _config.RefreshSeconds) * 1000;
            if (_timer.Interval != desiredInterval) _timer.Interval = desiredInterval;

            // T13b: refresh del catálogo de tarifas en background — fire-and-forget, JAMÁS bloquea
            // este refresh de UI. No-op si la caché tiene <7 días o el toggle de privacidad está OFF.
            PricingCatalog.Default.RefreshInBackground(_config.PricingOnlineUpdate);

            var cfg = _config;
            var now = DateTime.UtcNow;

            var (result, spend, health) = await Task.Run(async () =>
            {
                var r = await _api.FetchAsync();
                WindowStats? s = null;
                if (cfg.ShowSpendEstimate)
                {
                    var window = TimeSpan.FromDays(cfg.SpendWindowDays);
                    s = UsageAggregator.Build(_parser.Read(now - window), now, window, window).Week;
                }
                HealthStatus? h = cfg.ShowHealth ? await StatusClient.FetchAsync() : null;
                return (r, s, h);
            });

            if (result.State == UsageFetchState.Ok && result.Usage is not null)
            {
                _lastUsage = result.Usage;
                _lastUsageAtUtc = now;
                UsageCache.Save(result.Usage, now);
                _history.Append(result.Usage, now);   // sample real % into SQLite
                if (now - _lastPruneUtc > TimeSpan.FromHours(1)) { _history.Prune(); _lastPruneUtc = now; }
            }
            else
            {
                // FUENTE LOCAL DE RESPALDO (2026-08-10): con la API OAuth devolviendo 429 de forma
                // permanente, el panel se quedaba congelado en el último dato bueno (16 días viejo). La
                // app de escritorio de Claude escribe la cota real en disco cada ~15 min, sin token y sin
                // red: si ese dato es MÁS NUEVO que el que tenemos, se usa. Es dato real y fresco, así
                // que el estado pasa a Ok (el aviso de "datos anteriores" solo debe salir cuando de
                // verdad estamos mostrando algo viejo).
                var localAt = _localUsage.LastSampleAt();
                if (localAt is { } at && at.UtcDateTime > _lastUsageAtUtc
                    && _localUsage.Read(now) is { } localUsage)
                {
                    _lastUsage = localUsage;
                    _lastUsageAtUtc = at.UtcDateTime;
                    result = new UsageResult { State = UsageFetchState.Ok, Usage = localUsage };
                    UsageCache.Save(localUsage, _lastUsageAtUtc);
                    _history.Append(localUsage, _lastUsageAtUtc);
                    if (now - _lastPruneUtc > TimeSpan.FromHours(1)) { _history.Prune(); _lastPruneUtc = now; }
                }
            }

            PaceResult? paceFive = null, paceSeven = null;
            if (_lastUsage is not null)
            {
                var recent = _history.RecentForRate(now.AddHours(-6));
                (paceFive, paceSeven) = PaceCalculator.Compute(_lastUsage, recent, now);
            }

            var snap = new AppSnapshot
            {
                Usage = _lastUsage,
                LatestState = result.State,
                UsageAtUtc = _lastUsageAtUtc,
                Spend = spend,
                SpendDays = cfg.SpendWindowDays,
                Health = health,
                PaceFive = paceFive,
                PaceSeven = paceSeven
            };
            _lastSnapshot = snap;
            UpdateUi(snap);
        }
        catch (Exception ex)
        {
            _tray.Text = "ClaudeBar — error";
            Debug.WriteLine(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private void UpdateUi(AppSnapshot snap)
    {
        Icon newIcon;
        if (snap.Usage is { } u)
        {
            var (icoVal, _, status) = IconContent(u, snap);
            // "stale": el dato envejeció (supera 3× el refresco) o la última lectura no fue Ok.
            bool stale = UsageFormat.IsStale(snap.UsageAtUtc, _config.RefreshSeconds)
                         || snap.LatestState != UsageFetchState.Ok;
            newIcon = TrayIconRenderer.Render(icoVal, _theme, _config.WarnThresholdPct, _config.CriticalThresholdPct,
                status, stale, pending: LiveAttentionPending(), taskbarLight: ThemeResolver.TaskbarIsLight());

            string five = UsageFormat.Countdown(u.FiveHour?.ResetsAt, _s.Resetting);
            string week = UsageFormat.Countdown(u.SevenDay?.ResetsAt, _s.Resetting);
            string tip = $"5h {Fmt(u.FiveHour)}{Suffix(five)}\n7d {Fmt(u.SevenDay)}{Suffix(week)}";
            if (snap.LatestState != UsageFetchState.Ok)
                tip += "\n" + _s.PreviousDataTip;
            _tray.Text = Truncate(tip, 127);

            CheckMilestones(u.MaxUtilization, u);
            CheckPace(snap);
        }
        else
        {
            newIcon = TrayIconRenderer.RenderError(_theme.Neutral, pending: LiveAttentionPending());
            _tray.Text = "ClaudeBar — " + UsageFormat.StateMessage(snap.LatestState, _s);
        }

        _tray.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;

        if (_dashboard.Visible)
        {
            _dashboard.UpdateData(snap, _config, _plan);
            RefreshOverviewStats();
        }
    }

    private void RefreshOverviewStats()
    {
        Task.Run(() =>
        {
            // Read all transcripts (alltime lookback) — TranscriptParser is cheap for local files.
            var records = _parser.Read(DateTime.UtcNow.AddDays(-365));
            _dashboard.UpdateOverviewStats(records);
        });
    }

    // ---------- Milestone notifications ----------

    private void CheckMilestones(double pct, RealUsage usage)
    {
        var set = (_config.NotifyMilestones ?? Array.Empty<int>())
            .Where(m => m is > 0 and <= 100).Distinct().OrderBy(m => m).ToList();

        foreach (var m in set)
            if (!_fired.ContainsKey(m)) _fired[m] = pct >= m;
        foreach (var stale in _fired.Keys.Where(k => !set.Contains(k)).ToList())
            _fired.Remove(stale);

        if (!_milestonesInitialised)
        {
            foreach (var m in set) _fired[m] = pct >= m;
            _milestonesInitialised = true;
            return;
        }

        int highestCrossed = -1;
        foreach (var m in set)
        {
            if (pct >= m)
            {
                if (!_fired[m]) { _fired[m] = true; if (m > highestCrossed) highestCrossed = m; }
            }
            else
            {
                _fired[m] = false;
            }
        }

        if (highestCrossed >= 0 && _config.NotificationsEnabled)
            NotifyMilestone(highestCrossed, usage);
    }

    private void NotifyMilestone(int milestone, RealUsage u)
    {
        (string dot, ToolTipIcon icon) = milestone switch
        {
            >= 95 => ("🔴", ToolTipIcon.Error),
            >= 75 => ("🟠", ToolTipIcon.Warning),
            >= 50 => ("🟡", ToolTipIcon.Warning),
            _ => ("🟢", ToolTipIcon.Info)
        };

        _tray.BalloonTipIcon = icon;
        _tray.BalloonTipTitle = $"{dot} {string.Format(_s.NotifQuotaFormat, milestone)}";
        _tray.BalloonTipText = $"5h {Fmt(u.FiveHour)} · 7d {Fmt(u.SevenDay)}  ({_plan.Display})";
        _tray.ShowBalloonTip(6000);
    }

    // ---------- Pace ----------

    private (int value, Color color, UsageStatus status) IconContent(RealUsage u, AppSnapshot snap)
    {
        int maxPct = (int)Math.Round(u.MaxUtilization);
        var worst = WorstPace(snap);
        // Estado por color/forma derivado del pace (peor ventana) o, sin pace, del % de uso.
        UsageStatus paceStatus = worst is null
            ? StatusFor(u.MaxUtilization)
            : worst.Status switch
            {
                PaceStatus.Critical => UsageStatus.Critical,
                PaceStatus.Over => UsageStatus.Warn,
                _ => UsageStatus.Ok
            };
        Color paceColor = Theme.StatusColor(_theme, paceStatus);
        UsageStatus usageStatus = StatusFor(u.MaxUtilization);

        return _config.IconDisplayMode switch
        {
            "pace" => (worst is not null ? (int)Math.Round(worst.PaceRatio * 100) : maxPct, paceColor, paceStatus),
            "both" => (maxPct, paceColor, paceStatus),
            _ => (maxPct, Theme.StatusColor(_theme, usageStatus), usageStatus)
        };
    }

    private static PaceResult? WorstPace(AppSnapshot snap)
    {
        var list = new[] { snap.PaceFive, snap.PaceSeven }.Where(p => p is not null).Cast<PaceResult>().ToList();
        return list.Count == 0
            ? null
            : list.OrderByDescending(p => (int)p.Status).ThenByDescending(p => p.PaceRatio).First();
    }

    private void CheckPace(AppSnapshot snap)
    {
        HandlePaceWindow(snap.PaceFive, ref _paceFiredFive, _s.WinSession);
        HandlePaceWindow(snap.PaceSeven, ref _paceFiredSeven, _s.WinWeekly);
    }

    private void HandlePaceWindow(PaceResult? p, ref bool fired, string windowName)
    {
        if (p is null) { fired = false; return; }
        if (p.ExhaustsBeforeReset)
        {
            if (!fired)
            {
                fired = true;
                if (_config.NotificationsEnabled && _config.PaceAlerts) NotifyPace(p, windowName);
            }
        }
        else fired = false;
    }

    private void NotifyPace(PaceResult p, string windowName)
    {
        // ETA con el día abreviado del idioma elegido (T2), no el de la CurrentCulture del SO.
        string eta = UsageFormat.ResetAbsolute(p.EtaUtc, _s.Culture);
        _tray.BalloonTipIcon = ToolTipIcon.Warning;
        _tray.BalloonTipTitle = _s.PaceAlertTitle;
        _tray.BalloonTipText = string.Format(_s.PaceAlertBodyFmt, windowName, eta);
        _tray.ShowBalloonTip(7000);
    }

    // ---------- Helpers ----------

    // % del tooltip/notificaciones con la cultura del idioma elegido (T2): instancia porque lee _s.
    private string Fmt(UsageWindow? w) => w is null ? "—" : UsageFormat.Percent(w.UtilizationPct, _s.Culture);
    private static string Suffix(string countdown) => countdown.Length > 0 ? $" (↻{countdown})" : "";

    private UsageStatus StatusFor(double pct) =>
        pct >= _config.CriticalThresholdPct ? UsageStatus.Critical :
        pct >= _config.WarnThresholdPct ? UsageStatus.Warn :
        UsageStatus.Ok;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private void ToggleDashboard()
    {
        if (_dashboard.Visible) _dashboard.Hide();
        else ShowDashboard();
    }

    private void ShowDashboard()
    {
        if (_lastSnapshot is not null)
            _dashboard.UpdateData(_lastSnapshot, _config, _plan);
        RefreshOverviewStats();
        _dashboard.ShowConfigured(_config);
    }

    private Task<List<HistoryBucket>> BuildHistoryAsync(ChartRange range) => Task.Run(() =>
    {
        var records = _parser.Read(DateTime.UtcNow - UsageHistory.Lookback(range));
        return UsageHistory.Build(records, range, DateTime.UtcNow, _s.Culture);
    });

    private Task<List<PctPoint>> BuildPercentAsync(ChartRange range) => Task.Run(() =>
    {
        var now = DateTime.UtcNow;
        return _history.QueryPercent(now - UsageHistory.Lookback(range), now, 120);
    });

    private void OpenBilling()
    {
        try { Process.Start(new ProcessStartInfo("https://console.anthropic.com/settings/billing") { UseShellExecute = true }); }
        catch { }
    }

    private static string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
    private const string RepoUrl = "https://github.com/Yovancas/claudebar-win";

    private void ShowAbout()
    {
        string msg = $"ClaudeBar for Windows\nv{AppVersion}\n\n{RepoUrl}";
        MessageBox.Show(msg, $"{_s.About} — ClaudeBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenConfig()
    {
        _config.Save();
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppConfig.ConfigPath}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void OpenProjects()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_parser.ProjectsRoot}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void ExitApp()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _currentIcon?.Dispose();
        _updateIcon?.Dispose();
        _dashboard.Dispose();
        _showSignal.Dispose();
        _pipe?.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _currentIcon?.Dispose();
            _updateIcon?.Dispose();
            _dashboard.Dispose();
            _showSignal.Dispose();
            _pipe?.Dispose();
        }
        base.Dispose(disposing);
    }
}
