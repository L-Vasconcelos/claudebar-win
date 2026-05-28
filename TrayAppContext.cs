using System.Diagnostics;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin;

public sealed class TrayAppContext : ApplicationContext
{
    public const string ShowSignalName = "ClaudeBarWin_Show";

    private static readonly int[] FreqSeconds = { 30, 60, 300, 900 };
    private static readonly (double warn, double crit)[] ThresholdOptions = { (70, 90), (80, 95), (60, 85) };
    private static readonly int[] MilestoneOptions = { 25, 50, 75, 95 };
    private static readonly string[] PositionKeys = { "BottomRight", "BottomLeft", "TopRight", "TopLeft", "Center" };

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly UsageApiClient _api;
    private readonly TranscriptParser _parser;
    private readonly UsageHistoryStore _history;
    private readonly PlanInfo _plan;
    private DateTime _lastPruneUtc = DateTime.MinValue;
    private readonly DashboardForm _dashboard;
    private readonly EventWaitHandle _showSignal;

    private readonly List<(ToolStripMenuItem item, int seconds)> _freqItems = new();
    private readonly List<(ToolStripMenuItem item, int pct)> _milestoneItems = new();
    private readonly List<(ToolStripMenuItem item, double warn, double crit)> _thresholdItems = new();
    private readonly List<(ToolStripMenuItem item, string pos)> _posItems = new();
    private readonly List<(ToolStripMenuItem item, double value)> _opacityItems = new();
    private readonly List<(ToolStripMenuItem item, string code)> _langItems = new();
    private readonly List<(ToolStripMenuItem item, string id)> _themeItems = new();
    private readonly List<(ToolStripMenuItem item, string mode)> _iconItems = new();
    private ToolStripMenuItem _miPaceAlerts = null!;
    private ToolStripMenuItem _miNotifications = null!;
    private ToolStripMenuItem _miSpend = null!;
    private ToolStripMenuItem _miStartup = null!;
    private ToolStripMenuItem _miSticky = null!;
    private ToolStripMenuItem _miOnTop = null!;
    private ToolStripMenuItem _miHealth = null!;
    private ToolStripMenuItem _miChart = null!;
    private ToolStripMenuItem _miImportTheme = null!;

    private AppConfig _config;
    private Strings _s;
    private Theme _theme;
    private string _menuLangCode;
    private Icon? _currentIcon;

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

        _ = RefreshAsync();
        _timer.Start();
    }

    public static string DescribeMenu()
    {
        var s = Localization.Get("en");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(s.Dashboard);
        sb.AppendLine(s.Refresh);
        sb.AppendLine($"{s.PanelWindow} ▶  {s.Position} (5 presets + drag) · ☑ {s.Sticky} · ☑ {s.AlwaysOnTop} · {s.Opacity} ▶");
        sb.AppendLine($"{s.UpdateFrequency} ▶  {s.Sec30} · {s.Min1} · {s.Min5} · {s.Min15}");
        sb.AppendLine($"{s.Notifications} ▶  ☑ {s.Enabled} · {s.NotifyWhenReaching} ☑25/50/75/95%");
        sb.AppendLine($"{s.ColorThreshold} ▶  70/90 · 80/95 · 60/85");
        sb.AppendLine($"{s.Settings} ▶  ☑ {s.ShowSpend} · ☑ {s.ShowServiceStatus} · ☑ {s.UsageChart}");
        sb.AppendLine($"          {s.IconMode} ▶ % / ▲ / % ▲  ·  ☑ {s.PaceAlerts}  ·  ☑ {s.StartWithWindows}");
        sb.AppendLine($"          {s.Theme} ▶ {s.ThemeSystem}/{s.ThemeDark}/{s.ThemeLight}/{s.ThemeCli} · {s.ImportTheme}");
        sb.AppendLine($"          {s.Language} ▶ (system + 8) · {s.EditConfig} · {s.OpenDataFolder}");
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

    /// <summary>Submenu container that cascades LEFT so it stays on the primary monitor.</summary>
    private static ToolStripMenuItem Sub(string text) =>
        new(text) { DropDownDirection = ToolStripDropDownDirection.Left };

    private string FreqLabel(int seconds) => seconds switch
    {
        30 => _s.Sec30, 60 => _s.Min1, 300 => _s.Min5, 900 => _s.Min15, _ => $"{seconds}s"
    };

    private string PosLabel(string key) => key switch
    {
        "BottomRight" => _s.PosBottomRight,
        "BottomLeft" => _s.PosBottomLeft,
        "TopRight" => _s.PosTopRight,
        "TopLeft" => _s.PosTopLeft,
        "Center" => _s.PosCenter,
        _ => key
    };

    private string ThemeLabel(string id) => id switch
    {
        "system" => _s.ThemeSystem,
        "dark" => _s.ThemeDark,
        "light" => _s.ThemeLight,
        "cli" => _s.ThemeCli,
        "imported" => _s.ThemeImported,
        _ => id
    };

    private ContextMenuStrip BuildMenu()
    {
        _freqItems.Clear();
        _milestoneItems.Clear();
        _thresholdItems.Clear();
        _posItems.Clear();
        _opacityItems.Clear();
        _langItems.Clear();
        _themeItems.Clear();
        _iconItems.Clear();

        var menu = new ContextMenuStrip { ShowImageMargin = true };

        menu.Items.Add(_s.Dashboard, null, (_, _) => ShowDashboard());
        menu.Items.Add(_s.Refresh, null, async (_, _) => await RefreshAsync());
        menu.Items.Add(new ToolStripSeparator());

        // Panel window
        var window = Sub(_s.PanelWindow);
        var posMenu = Sub(_s.Position);
        foreach (var key in PositionKeys)
        {
            var it = new ToolStripMenuItem(PosLabel(key));
            it.Click += (_, _) => MutateConfig(c => c.DashboardPosition = key);
            _posItems.Add((it, key));
            posMenu.DropDownItems.Add(it);
        }
        posMenu.DropDownItems.Add(new ToolStripSeparator());
        var customInfo = new ToolStripMenuItem(_s.PosCustom) { Enabled = false };
        _posItems.Add((customInfo, "Custom"));
        posMenu.DropDownItems.Add(customInfo);
        window.DropDownItems.Add(posMenu);

        _miSticky = new ToolStripMenuItem(_s.Sticky);
        _miSticky.Click += (_, _) => MutateConfig(c => c.DashboardSticky = !c.DashboardSticky);
        window.DropDownItems.Add(_miSticky);

        _miOnTop = new ToolStripMenuItem(_s.AlwaysOnTop);
        _miOnTop.Click += (_, _) => MutateConfig(c => c.DashboardAlwaysOnTop = !c.DashboardAlwaysOnTop);
        window.DropDownItems.Add(_miOnTop);

        var opacity = Sub(_s.Opacity);
        foreach (var pct in new[] { 100, 90, 80, 70, 60 })
        {
            double val = pct / 100.0;
            var it = new ToolStripMenuItem($"{pct}%");
            it.Click += (_, _) => MutateConfig(c => c.DashboardOpacity = val);
            _opacityItems.Add((it, val));
            opacity.DropDownItems.Add(it);
        }
        window.DropDownItems.Add(opacity);
        menu.Items.Add(window);

        // Update frequency
        var freq = Sub(_s.UpdateFrequency);
        foreach (var secs in FreqSeconds)
        {
            var it = new ToolStripMenuItem(FreqLabel(secs));
            it.Click += (_, _) => MutateConfig(c => c.RefreshSeconds = secs);
            _freqItems.Add((it, secs));
            freq.DropDownItems.Add(it);
        }
        menu.Items.Add(freq);

        // Notifications
        var notif = Sub(_s.Notifications);
        _miNotifications = new ToolStripMenuItem(_s.Enabled);
        _miNotifications.Click += (_, _) => MutateConfig(c => c.NotificationsEnabled = !c.NotificationsEnabled);
        notif.DropDownItems.Add(_miNotifications);
        notif.DropDownItems.Add(new ToolStripSeparator());
        notif.DropDownItems.Add(new ToolStripMenuItem(_s.NotifyWhenReaching) { Enabled = false });
        foreach (var pct in MilestoneOptions)
        {
            var it = new ToolStripMenuItem($"{pct}%");
            it.Click += (_, _) => MutateConfig(c =>
            {
                var list = (c.NotifyMilestones ?? Array.Empty<int>()).ToList();
                if (list.Contains(pct)) list.Remove(pct); else list.Add(pct);
                c.NotifyMilestones = list.Distinct().OrderBy(x => x).ToArray();
            });
            _milestoneItems.Add((it, pct));
            notif.DropDownItems.Add(it);
        }
        menu.Items.Add(notif);

        // Color threshold
        var thr = Sub(_s.ColorThreshold);
        foreach (var (warn, crit) in ThresholdOptions)
        {
            string label = $"{warn:0}% / {crit:0}%" + (warn == 70 && crit == 90 ? "  " + _s.DefaultTag : "");
            var it = new ToolStripMenuItem(label);
            it.Click += (_, _) => MutateConfig(c => { c.WarnThresholdPct = warn; c.CriticalThresholdPct = crit; });
            _thresholdItems.Add((it, warn, crit));
            thr.DropDownItems.Add(it);
        }
        menu.Items.Add(thr);

        // Settings
        var settings = Sub(_s.Settings);
        _miSpend = new ToolStripMenuItem(_s.ShowSpend);
        _miSpend.Click += (_, _) => MutateConfig(c => c.ShowSpendEstimate = !c.ShowSpendEstimate);
        settings.DropDownItems.Add(_miSpend);

        _miHealth = new ToolStripMenuItem(_s.ShowServiceStatus);
        _miHealth.Click += (_, _) => MutateConfig(c => c.ShowHealth = !c.ShowHealth);
        settings.DropDownItems.Add(_miHealth);

        _miChart = new ToolStripMenuItem(_s.UsageChart);
        _miChart.Click += (_, _) => MutateConfig(c => c.ShowChart = !c.ShowChart);
        settings.DropDownItems.Add(_miChart);

        var iconMode = Sub(_s.IconMode);
        foreach (var (label, mode) in new[] { ("%", "percent"), ("▲", "pace"), ("% ▲", "both") })
        {
            var it = new ToolStripMenuItem(label);
            it.Click += (_, _) => MutateConfig(c => c.IconDisplayMode = mode);
            _iconItems.Add((it, mode));
            iconMode.DropDownItems.Add(it);
        }
        settings.DropDownItems.Add(iconMode);

        _miPaceAlerts = new ToolStripMenuItem(_s.PaceAlerts);
        _miPaceAlerts.Click += (_, _) => MutateConfig(c => c.PaceAlerts = !c.PaceAlerts);
        settings.DropDownItems.Add(_miPaceAlerts);

        _miStartup = new ToolStripMenuItem(_s.StartWithWindows);
        _miStartup.Click += (_, _) => { StartupManager.Toggle(); };
        settings.DropDownItems.Add(_miStartup);

        // Theme submenu
        var theme = Sub(_s.Theme);
        var themeIds = new List<string> { "system", "dark", "light", "cli" };
        if (_config.ImportedTheme is not null) themeIds.Add("imported");
        foreach (var id in themeIds)
        {
            var it = new ToolStripMenuItem(ThemeLabel(id));
            it.Click += (_, _) => MutateConfig(c => c.Theme = id);
            _themeItems.Add((it, id));
            theme.DropDownItems.Add(it);
        }
        theme.DropDownItems.Add(new ToolStripSeparator());
        _miImportTheme = new ToolStripMenuItem(_s.ImportTheme);
        _miImportTheme.Click += (_, _) => ImportItermColors();
        theme.DropDownItems.Add(_miImportTheme);
        settings.DropDownItems.Add(theme);

        // Language submenu
        var lang = Sub(_s.Language);
        var sys = new ToolStripMenuItem(_s.SystemDefault);
        sys.Click += (_, _) => MutateConfig(c => c.Language = "system");
        _langItems.Add((sys, "system"));
        lang.DropDownItems.Add(sys);
        lang.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (code, native) in Localization.Languages)
        {
            var it = new ToolStripMenuItem(native);
            it.Click += (_, _) => MutateConfig(c => c.Language = code);
            _langItems.Add((it, code));
            lang.DropDownItems.Add(it);
        }
        settings.DropDownItems.Add(lang);

        settings.DropDownItems.Add(new ToolStripSeparator());
        settings.DropDownItems.Add(_s.OpenBilling, null, (_, _) => OpenBilling());
        settings.DropDownItems.Add(_s.EditConfig, null, (_, _) => OpenConfig());
        settings.DropDownItems.Add(_s.OpenDataFolder, null, (_, _) => OpenProjects());
        menu.Items.Add(settings);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_s.Exit, null, (_, _) => ExitApp());

        menu.Opening += (_, _) => UpdateMenuChecks();
        _dashboard?.AttachContextMenu(menu); // right-click on the panel opens the same menu
        return menu;
    }

    private void UpdateMenuChecks()
    {
        var c = AppConfig.Load();
        var milestones = c.NotifyMilestones ?? Array.Empty<int>();

        foreach (var (item, secs) in _freqItems) item.Checked = c.RefreshSeconds == secs;

        _miNotifications.Checked = c.NotificationsEnabled;
        foreach (var (item, pct) in _milestoneItems)
        {
            item.Checked = milestones.Contains(pct);
            item.Enabled = c.NotificationsEnabled;
        }

        foreach (var (item, warn, crit) in _thresholdItems)
            item.Checked = Math.Abs(c.WarnThresholdPct - warn) < 0.01 && Math.Abs(c.CriticalThresholdPct - crit) < 0.01;

        _miSpend.Checked = c.ShowSpendEstimate;
        _miHealth.Checked = c.ShowHealth;
        _miChart.Checked = c.ShowChart;
        _miStartup.Checked = StartupManager.IsEnabled();
        string iconMode = string.IsNullOrEmpty(c.IconDisplayMode) ? "percent" : c.IconDisplayMode;
        foreach (var (item, mode) in _iconItems) item.Checked = mode == iconMode;
        _miPaceAlerts.Checked = c.PaceAlerts;

        foreach (var (item, pos) in _posItems)
            item.Checked = string.Equals(c.DashboardPosition, pos, StringComparison.OrdinalIgnoreCase);
        _miSticky.Checked = c.DashboardSticky;
        _miOnTop.Checked = c.DashboardAlwaysOnTop;
        double op = c.DashboardOpacity <= 0 ? 1.0 : c.DashboardOpacity;
        foreach (var (item, value) in _opacityItems) item.Checked = Math.Abs(op - value) < 0.001;

        string themeId = string.IsNullOrEmpty(c.Theme) ? "system" : c.Theme;
        foreach (var (item, id) in _themeItems) item.Checked = id == themeId;

        string lang = string.IsNullOrEmpty(c.Language) ? "system" : c.Language;
        foreach (var (item, code) in _langItems) item.Checked = code == lang;
    }

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
            if (_dashboard.IsHandleCreated) _dashboard.BeginInvoke((Action)RebuildMenu); // surface "imported" entry
        }
        catch { }
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
            var (icoVal, icoColor) = IconContent(u, snap);
            newIcon = TrayIconRenderer.Render(icoVal, icoColor);

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
            newIcon = TrayIconRenderer.RenderError(_theme.Neutral);
            _tray.Text = "ClaudeBar — " + UsageFormat.StateMessage(snap.LatestState, _s);
        }

        _tray.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;

        if (_dashboard.Visible)
            _dashboard.UpdateData(snap, _config, _plan);
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

    private (int value, Color color) IconContent(RealUsage u, AppSnapshot snap)
    {
        int maxPct = (int)Math.Round(u.MaxUtilization);
        var worst = WorstPace(snap);
        Color paceColor = worst is null
            ? Theme.StatusColor(_theme, StatusFor(u.MaxUtilization))
            : worst.Status switch
            {
                PaceStatus.Critical => _theme.Critical,
                PaceStatus.Over => _theme.Warn,
                _ => _theme.Ok
            };

        return _config.IconDisplayMode switch
        {
            "pace" => (worst is not null ? (int)Math.Round(worst.PaceRatio * 100) : maxPct, paceColor),
            "both" => (maxPct, paceColor),
            _ => (maxPct, Theme.StatusColor(_theme, StatusFor(u.MaxUtilization)))
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
        string eta = p.EtaUtc?.ToLocalTime().ToString("ddd HH:mm") ?? "";
        _tray.BalloonTipIcon = ToolTipIcon.Warning;
        _tray.BalloonTipTitle = _s.PaceAlertTitle;
        _tray.BalloonTipText = string.Format(_s.PaceAlertBodyFmt, windowName, eta);
        _tray.ShowBalloonTip(7000);
    }

    // ---------- Helpers ----------

    private static string Fmt(UsageWindow? w) => w is null ? "—" : $"{w.UtilizationPct:0.#}%";
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
        _dashboard.ShowConfigured(_config);
    }

    private Task<List<HistoryBucket>> BuildHistoryAsync(ChartRange range) => Task.Run(() =>
    {
        var records = _parser.Read(DateTime.UtcNow - UsageHistory.Lookback(range));
        return UsageHistory.Build(records, range, DateTime.UtcNow);
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
        _dashboard.Dispose();
        _showSignal.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _currentIcon?.Dispose();
            _dashboard.Dispose();
            _showSignal.Dispose();
        }
        base.Dispose(disposing);
    }
}
