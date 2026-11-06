using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using SleekMon.Sensors;

namespace SleekMon.App;

/// The app: a tray icon and its menu around the dashboard. Controls are only touched on the UI thread; dashboard
/// events are posted to it. Menu changes are saved to the config file.
internal sealed class TrayApp : ApplicationContext {
    private readonly ConfigFile _configFile = new();
    private readonly SynchronizationContext _ui = new WindowsFormsSynchronizationContext();
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _enabled;
    private readonly ToolStripMenuItem _powerSaving;
    private readonly ToolStripMenuItem _wake;
    private readonly ToolStripMenuItem _brightness = new("Brightness");
    private readonly Dashboard _dashboard;
    private AppConfig _config;
    private bool _exiting;

    public TrayApp() {
        _config = _configFile.Load();

        _enabled = new ToolStripMenuItem("Enabled", null, OnEnabledClick) { Checked = _config.Enabled };
        _powerSaving = new ToolStripMenuItem("Power saving", null, OnPowerSavingClick) { Checked = _config.PowerSaving };
        _wake = new ToolStripMenuItem("Wake screen", null, (_, _) => Wake());
        UpdateWakeItem();
        foreach (var level in _config.BrightnessLevels) {
            _brightness.DropDownItems.Add(new ToolStripMenuItem($"{level}%", null, (_, _) => OnBrightnessClick(level)) {
                Checked = level == _config.Brightness
            });
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_wake);
        menu.Items.Add(_enabled);
        menu.Items.Add(_powerSaving);
        menu.Items.Add(_brightness);
        if (!SensorDump.IsAdministrator()) {
            menu.Items.Add(new ToolStripMenuItem("Restart as administrator", null, OnRestartAsAdministratorClick));
        }
        menu.Items.Add(new ToolStripSeparator());
        var config = new ToolStripMenuItem("Config");
        config.DropDownItems.Add(new ToolStripMenuItem("Open config folder", null, OnOpenConfigFolderClick));
        config.DropDownItems.Add(new ToolStripMenuItem("Open config file", null, OnOpenConfigClick));
        config.DropDownItems.Add(new ToolStripMenuItem("Restart to apply changes", null, (_, _) => Relaunch(verb: null)));
        menu.Items.Add(config);
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Exit()));
        // Bold: the default action, the one of a left click on the icon
        _wake.Font = new Font(menu.Font, FontStyle.Bold);
        _notifyIcon = new NotifyIcon { ContextMenuStrip = menu };
        _notifyIcon.MouseClick += (_, e) => {
            if (e.Button == MouseButtons.Left) Wake();
        };

        _dashboard = new Dashboard(_config);
        _dashboard.StateChanged += _ => _ui.Post(_ => ShowState(), null);
        ShowState();
        _notifyIcon.Visible = true;
        _dashboard.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnded += OnSessionEnded;
    }

    private void ShowState() {
        if (_exiting) return;

        var text = _dashboard.State switch {
            DashboardState.Disabled => "Disabled",
            DashboardState.Disconnected => "Screen not connected",
            DashboardState.Connecting => $"Connecting to {_dashboard.PortName}",
            DashboardState.Connected => $"Connected · {_dashboard.PortName}",
            _ => "Sleeping"
        };
        _status.Text = text;
        _notifyIcon.Text = $"SleekMon · {text}";
        _notifyIcon.Icon = _icons.For(_dashboard.State);
    }

    private void OnEnabledClick(object? sender, EventArgs e) {
        var enabled = !_enabled.Checked;
        _enabled.Checked = enabled;
        Save(_config with { Enabled = enabled });
        UpdateWakeItem();
        // Disabling waits for the screen to turn off (a second or two)
        _dashboard.SetEnabled(enabled);
    }

    private void OnPowerSavingClick(object? sender, EventArgs e) {
        var powerSaving = !_powerSaving.Checked;
        _powerSaving.Checked = powerSaving;
        Save(_config with { PowerSaving = powerSaving });
        UpdateWakeItem();
        _dashboard.SetPowerSaving(powerSaving);
    }

    /// Waking only means something when the screen can sleep.
    private void UpdateWakeItem() => _wake.Enabled = _config.Enabled && _config.PowerSaving;

    private void Wake() {
        if (_wake.Enabled) _dashboard.Wake();
    }

    private void OnBrightnessClick(int level) {
        foreach (ToolStripMenuItem item in _brightness.DropDownItems) item.Checked = item.Text == $"{level}%";
        Save(_config with { Brightness = level });
        _dashboard.SetBrightness(level);
    }

    private void OnRestartAsAdministratorClick(object? sender, EventArgs e) => Relaunch("runas");

    /// Starts a new instance then exits. The new instance waits for this one to exit (single instance lock). Without
    /// a verb, it has the same rights as this one.
    private void Relaunch(string? verb) {
        var path = Environment.ProcessPath ?? Application.ExecutablePath;
        try {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = verb ?? "" });
        } catch (Win32Exception exception) {
            // Elevation refused, or cannot start
            Log.Warn($"Not restarted: {exception.Message}");
            return;
        }
        Exit();
    }

    private static void OnOpenConfigFolderClick(object? sender, EventArgs e) {
        try {
            // Opens the folder with the file selected
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ConfigFile.DefaultPath}\"") { UseShellExecute = true });
        } catch (Win32Exception exception) {
            Log.Warn($"Cannot open the config folder: {exception.Message}");
        }
    }

    private static void OnOpenConfigClick(object? sender, EventArgs e) {
        var path = ConfigFile.DefaultPath;
        try {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        } catch (Win32Exception) {
            // No program associated with .json files
            try {
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            } catch (Win32Exception exception) {
                Log.Warn($"Cannot open the config file: {exception.Message}");
            }
        }
    }

    /// Raised on a system thread.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) {
        if (e.Mode == PowerModes.Suspend) _dashboard.Suspend();
        else if (e.Mode == PowerModes.Resume) _dashboard.Resume();
    }

    /// Raised on a system thread when Windows shuts down or the user logs off: the screen stays powered over USB, so it
    /// is turned off now rather than left showing a frozen image.
    private void OnSessionEnded(object? sender, SessionEndedEventArgs e) {
        Log.Info($"Session ending ({e.Reason})");
        _dashboard.Dispose();
    }

    private void Save(AppConfig config) {
        _config = config;
        _configFile.Save(config);
    }

    private void Exit() {
        if (_exiting) return;
        _exiting = true;
        Log.Info("Exiting");

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnded -= OnSessionEnded;
        _dashboard.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icons.Dispose();
        ExitThread();
    }
}
