using ApexTriggers.App.Resources;
using ApexTriggers.Core;
using ApexTriggers.Core.Config;

namespace ApexTriggers.App;

/// <summary>Tray icon, its menu and notifications; owns the main window's lifetime.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly TriggerController _controller;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new() { Renderer = new Theme.MenuRenderer(), ShowCheckMargin = true, ShowImageMargin = false };
    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _spaceStationTimer = new() { Interval = 30_000 };
    private MainForm? _main;
    private PadStatus _lastStatus = PadStatus.NotConnected;
    private bool _spaceStationWarned;
    private Icon? _trayIcon;

    public TrayApp(TriggerController controller, bool startMinimized)
    {
        _controller = controller;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _tray = new NotifyIcon { Visible = true, ContextMenuStrip = _menu, Text = "Apex Triggers" };
        _tray.DoubleClick += (_, _) => ShowMain();
        _tray.BalloonTipClicked += (_, _) => ShowMain();
        _menu.Opening += (_, _) => BuildMenu();

        _controller.Changed += () => _ui.Post(_ => UpdateTray(), null);
        _controller.ActiveGameChanged += game => _ui.Post(_ => OnActiveGameChanged(game), null);
        _controller.Pad.StateChanged += state => _ui.Post(_ => OnPadState(state), null);

        _spaceStationTimer.Tick += (_, _) => CheckSpaceStation();
        _spaceStationTimer.Start();

        UpdateTray();
        BuildMenu();
        _controller.Start();
        CheckSpaceStation();
        if (!startMinimized) ShowMain();
    }

    public void ShowMain()
    {
        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(_controller);
            _main.FormClosing += OnMainClosing;
            _main.LanguageChanged += RebuildForLanguage;
        }
        _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
    }

    private void OnMainClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.Cancel || e.CloseReason != CloseReason.UserClosing) return;
        if (_controller.Config.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            _main!.Hide();
        }
        else
        {
            BeginExit();
        }
    }

    private void RebuildForLanguage()
    {
        L.Set(_controller.Config.Settings.Language);
        var old = _main;
        var bounds = old?.Bounds;
        _main = null;
        if (old is not null)
        {
            old.FormClosing -= OnMainClosing;
            old.Close();
            old.Dispose();
        }
        ShowMain();
        if (bounds is { } b) _main!.Bounds = b;
        BuildMenu();
        UpdateTray();
    }

    // ---- tray ----

    private void UpdateTray()
    {
        var state = _controller.Pad.State;
        var mode = PadMode();
        var dot = mode is not null ? Theme.Accent : state.Status switch
        {
            PadStatus.HandedToSteam => Theme.Green,
            PadStatus.Connected => Theme.Warn,
            _ => (Color?)null,
        };
        var icon = AppIcon.Tray(dot);
        _tray.Icon = icon;
        _trayIcon?.Dispose();
        _trayIcon = icon;

        var status = _controller.Pad.Yielding ? Strings.Tray_StatusBridge
            : mode is not null ? Strings.Tray_StatusXInput
            : state.Status switch
            {
                PadStatus.HandedToSteam => Strings.Tray_StatusHanded,
                PadStatus.Connected => Strings.Tray_StatusConnected,
                _ => Strings.Tray_StatusNoController,
            };
        var game = _controller.ActiveGame?.Name ?? Strings.Tray_OutsideGames;
        var text = $"Apex Triggers {Program.Version} — {status}\n{game}";
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>The bridge's session or a game's XInput hold, as a status phrase; null when neither applies.</summary>
    private string? PadMode()
    {
        var pad = _controller.Pad;
        if (pad.Yielding) return Strings.Status_Bridge;
        if (pad.HoldXInput && pad.State.Status == PadStatus.Connected) return Strings.Status_XInputHold;
        return null;
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        var state = _controller.Pad.State;
        var pad = state.Info is { } info
            ? $"{Status.Model(info)} · {Status.Connection(info)} · {Status.Battery(info)}"
            : Strings.Common_NoController;
        _menu.Items.Add(Info(pad, bold: true));
        _menu.Items.Add(Info(PadMode() is { } mode ? "● " + mode : state.Status switch
        {
            PadStatus.HandedToSteam => "● " + Strings.Status_Handed,
            PadStatus.Connected => "● " + Strings.Status_NotHanded,
            _ => Strings.Tray_PlugIn,
        }));
        _menu.Items.Add(new ToolStripSeparator());

        var active = _controller.ActiveGame;
        var (left, right) = _controller.EffectsFor(active);
        _menu.Items.Add(Info(L.F(Strings.Tray_Now, active?.Name ?? Strings.Tray_OutsideGames), bold: true));
        _menu.Items.Add(Info(L.Summary(left, right)));
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(Item(Strings.Tray_Open, ShowMain, bold: true));
        var pause = Item(Strings.Tray_Pause, () => _controller.SetPaused(!_controller.Paused));
        pause.Checked = _controller.Paused;
        _menu.Items.Add(pause);
        _menu.Items.Add(Item(Strings.Tray_ResetTriggers, () => _ = _controller.ResetTriggersAsync()));
        _menu.Items.Add(new ToolStripSeparator());

        var autostart = Item(Strings.Common_StartWithWindows, () => Autostart.Set(!Autostart.IsEnabled));
        autostart.Checked = Autostart.IsEnabled;
        _menu.Items.Add(autostart);
        var language = new ToolStripMenuItem(Strings.Tray_Language);
        // Language names stay in their own language whatever the UI language, so they are not resources.
        foreach (var (code, name) in new[] { ("ru", "Русский"), ("en", "English") })
        {
            var item = Item(name, () => SetLanguage(code));
            item.Checked = L.Code == code;
            language.DropDownItems.Add(item);
        }
        ((ToolStripDropDownMenu)language.DropDown).ShowImageMargin = false;
        ((ToolStripDropDownMenu)language.DropDown).ShowCheckMargin = true;
        language.DropDown.Renderer = _menu.Renderer;
        _menu.Items.Add(language);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item(Strings.Tray_Exit, BeginExit));
    }

    private void SetLanguage(string code)
    {
        _controller.Config.Settings.Language = code;
        ConfigStore.Save(_controller.Config);
        var wasVisible = _main is { Visible: true };
        L.Set(code);
        if (wasVisible) RebuildForLanguage();
        else
        {
            _main?.Dispose();
            _main = null;
            BuildMenu();
            UpdateTray();
        }
    }

    private static ToolStripMenuItem Info(string text, bool bold = false) => new(text)
    {
        Enabled = false,
        Font = bold ? Theme.BodyBold : Theme.Small,
    };

    private static ToolStripMenuItem Item(string text, Action action, bool bold = false)
    {
        var item = new ToolStripMenuItem(text) { Font = bold ? Theme.BodyBold : Theme.Body };
        item.Click += (_, _) => action();
        return item;
    }

    // ---- notifications ----

    private void OnActiveGameChanged(GamePreset? game)
    {
        UpdateTray();
        if (!_controller.Config.Settings.NotifyOnPresetChange || _controller.Pad.State.Status == PadStatus.NotConnected) return;
        var (left, right) = _controller.EffectsFor(game);
        Notify(game is null
                ? Strings.Common_DefaultPreset
                : L.F(Strings.Notify_PresetApplied, game.Name),
            L.Summary(left, right));
    }

    private void OnPadState(PadState state)
    {
        UpdateTray();
        var previous = _lastStatus;
        _lastStatus = state.Status;
        if (state.Status == previous || state.Info is not { } info) return;
        if (state.Status == PadStatus.HandedToSteam && previous == PadStatus.NotConnected)
            Notify(Strings.Notify_HandedTitle, L.F(Strings.Notify_HandedText, Status.Model(info), Status.Connection(info)));
        else if (state.Status == PadStatus.Connected && !info.SupportsHandover)
            Notify(Strings.Status_FirmwareCantHandOver,
                L.F(Strings.Notify_FirmwareText, Core.Protocol.PadInfo.MinHandoverFirmware), ToolTipIcon.Warning);
    }

    private void CheckSpaceStation()
    {
        var running = SpaceStation.RunningProcesses().Count > 0;
        if (running && !_spaceStationWarned)
            Notify(Strings.Common_SpaceStationRunning, Strings.Notify_SpaceStationText, ToolTipIcon.Warning);
        _spaceStationWarned = running;
    }

    private void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.None)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(4000);
    }

    // ---- exit ----

    private void BeginExit()
    {
        if (_main is { IsDisposed: false } main)
        {
            if (!main.ConfirmLeave()) return;
            main.FormClosing -= OnMainClosing;
            main.Close();
        }
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _spaceStationTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _controller.Dispose();
        base.ExitThreadCore();
    }
}
