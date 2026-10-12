using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace LiveBackground
{
    /// <summary>Owns the tray icon and the wallpaper window, and rebuilds the wallpaper whenever
    /// Explorer restarts, displays change or the browser process dies.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly Settings settings = Settings.Load();
        private readonly NotifyIcon tray;
        private readonly Timer watchdog = new Timer { Interval = 1000 };
        private readonly Timer restartDelay = new Timer { Interval = 1500 };
        private readonly Timer saveDelay = new Timer { Interval = 600 };
        private SettingsForm settingsForm;
        private readonly BroadcastWindow broadcasts;
        private readonly ToolStripMenuItem pauseItem;

        private DesktopLayer desktop;
        private WallpaperForm wallpaper;
        private bool autoPaused, sessionLocked;

        public TrayApp()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Live Background") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            var adjust = new ToolStripMenuItem("Adjust mist and trees…", null, (s, e) => OpenSettings());
            adjust.Font = new Font(adjust.Font, FontStyle.Bold); // default action, same as double-click
            menu.Items.Add(adjust);
            pauseItem = new ToolStripMenuItem("Pause", null, (s, e) => TogglePause());
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Toggle("Pause when apps cover the screen", () => settings.PauseWhenCovered,
                v => { settings.PauseWhenCovered = v; settings.Save(); }));
            menu.Items.Add(Toggle("Start with Windows", () => Settings.StartWithWindows, v => Settings.StartWithWindows = v));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Reload wallpaper", null, (s, e) => Restart("reload requested"));
            menu.Items.Add("Open log folder", null, (s, e) =>
            {
                Directory.CreateDirectory(Log.Folder);
                Process.Start("explorer.exe", "\"" + Log.Folder + "\"");
            });
            menu.Items.Add("Exit", null, (s, e) => ExitThread());

            tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
                Text = "Live Background",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => OpenSettings();

            broadcasts = new BroadcastWindow(() => Restart("Explorer restarted"));
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            restartDelay.Tick += (s, e) => { restartDelay.Stop(); Start(); };
            saveDelay.Tick += (s, e) => { saveDelay.Stop(); settings.Save(); };
            watchdog.Tick += (s, e) => Watch();

            UpdatePauseLabel();
            Start();
            watchdog.Start();
        }

        private async void Start()
        {
            Stop();
            string folder = settings.ResolveWallpaperFolder();
            if (!File.Exists(Path.Combine(folder, "index.html")))
            {
                Notify($"No wallpaper found in {folder}.");
                return;
            }

            desktop = new DesktopLayer();
            if (!desktop.Discover())
            {
                Log.Write("Desktop layer not ready; retrying.");
                restartDelay.Start();
                return;
            }

            var form = new WallpaperForm(folder);
            wallpaper = form;
            form.Crashed += (s, e) => Restart("browser process failed");
            form.Show();
            if (!desktop.Attach(form.Handle))
            {
                Notify("Could not place the wallpaper behind the desktop icons. See the log for details.");
                Stop();
                return;
            }

            try
            {
                await form.InitializeAsync();
                form.Apply(settings);
                ApplyPause();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                Notify("Microsoft Edge WebView2 Runtime is missing. Install it from Microsoft, then start Live Background again.");
                Stop();
            }
            catch (Exception e)
            {
                Log.Write("Wallpaper failed to start: " + e);
                Notify("The wallpaper failed to start. See the log for details.");
                Stop();
            }
        }

        private void Stop()
        {
            if (wallpaper == null) return;
            var old = wallpaper;
            wallpaper = null;
            old.Close();
            old.Dispose();
            desktop?.Refresh();
        }

        // Deferred: callers include the wallpaper's own events, so it must not be disposed inline.
        private void Restart(string reason)
        {
            if (restartDelay.Enabled) return;
            Log.Write("Restarting wallpaper: " + reason);
            restartDelay.Start();
        }

        private void Watch()
        {
            if (wallpaper == null || desktop == null || restartDelay.Enabled) return;
            // Reading Handle on a destroyed form would silently create a new top-level window.
            if (!wallpaper.IsHandleCreated || !desktop.StillValid(wallpaper.Handle))
            {
                Restart("desktop window changed");
                return;
            }
            desktop.EnsureZOrder(wallpaper.Handle);

            bool hide = settings.PauseWhenCovered && CoverageMonitor.DesktopHidden(wallpaper.Handle);
            if (hide != autoPaused)
            {
                autoPaused = hide;
                ApplyPause();
            }
        }

        private void ApplyPause() => wallpaper?.SetPaused(settings.Paused || autoPaused || sessionLocked);

        private void TogglePause()
        {
            settings.Paused = !settings.Paused;
            settings.Save();
            UpdatePauseLabel();
            ApplyPause();
        }

        private void UpdatePauseLabel() => pauseItem.Text = settings.Paused ? "Resume" : "Pause";

        private void OnDisplayChanged(object sender, EventArgs e) => Restart("display settings changed");

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) sessionLocked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock) sessionLocked = false;
            else return;
            ApplyPause();
        }

        private void OpenSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(settings, () =>
                {
                    wallpaper?.Apply(settings); // live while dragging
                    saveDelay.Stop();
                    saveDelay.Start();
                });
                settingsForm.FormClosed += (s, e) => { saveDelay.Stop(); settings.Save(); };
            }
            settingsForm.Show();
            if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
        }

        private static ToolStripMenuItem Toggle(string title, Func<bool> get, Action<bool> set)
        {
            var item = new ToolStripMenuItem(title) { Checked = get() };
            item.Click += (s, e) =>
            {
                try { set(!get()); }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    Log.Write($"Could not change '{title}': {ex.Message}");
                }
                item.Checked = get();
            };
            return item;
        }

        private void Notify(string message)
        {
            Log.Write(message);
            tray.ShowBalloonTip(8000, "Live Background", message, ToolTipIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            watchdog.Stop();
            restartDelay.Stop();
            if (saveDelay.Enabled) { saveDelay.Stop(); settings.Save(); }
            settingsForm?.Close();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            Stop();
            broadcasts.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            Log.Write("Exited.");
            base.ExitThreadCore();
        }

        /// <summary>Hidden top-level window: the only kind that receives Explorer's
        /// "TaskbarCreated" broadcast after Explorer restarts.</summary>
        private sealed class BroadcastWindow : NativeWindow
        {
            private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            private readonly Action onExplorerRestart;

            public BroadcastWindow(Action onExplorerRestart)
            {
                this.onExplorerRestart = onExplorerRestart;
                CreateHandle(new CreateParams { Caption = "LiveBackgroundBroadcasts" });
            }

            protected override void WndProc(ref Message m)
            {
                if ((uint)m.Msg == taskbarCreated) onExplorerRestart();
                base.WndProc(ref m);
            }
        }
    }
}
