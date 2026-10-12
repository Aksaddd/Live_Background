using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LiveBackground
{
    /// <summary>A borderless window hosting the web wallpaper, re-parented behind the desktop icons.</summary>
    internal sealed class WallpaperForm : Form
    {
        private const string Host = "wallpaper.local";
        private readonly WebView2 web;
        private readonly string folder;
        private bool ready;
        private bool paused;
        private Settings pending;

        public event EventHandler Crashed;

        public WallpaperForm(string folder)
        {
            this.folder = folder;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000); // keep the first frame off-screen until attached
            Size = new Size(16, 16);
            BackColor = Color.Black;
            Text = "Live Background";

            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
            Controls.Add(web);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public async Task InitializeAsync()
        {
            string dataDir = Path.Combine(Log.Folder, "WebView2");
            var options = new CoreWebView2EnvironmentOptions("--disable-features=CalculateNativeWinOcclusion");
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
            await web.EnsureCoreWebView2Async(env);

            CoreWebView2 core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.Allow);

            core.ProcessFailed += (s, e) =>
            {
                Log.Write($"WebView2 process failed: {e.ProcessFailedKind} ({e.Reason})");
                Crashed?.Invoke(this, EventArgs.Empty);
            };
            core.NavigationCompleted += (s, e) =>
            {
                Log.Write($"Wallpaper loaded: success={e.IsSuccess} status={e.WebErrorStatus}");
                ready = e.IsSuccess;
                if (pending != null) Apply(pending);
                SetPaused(paused);
            };

            core.Navigate($"https://{Host}/index.html");
        }

        /// <summary>Sends the settings to the page using Lively's property-listener convention.</summary>
        public void Apply(Settings s)
        {
            pending = s;
            if (!ready) return;
            Send("speed", s.Speed);
            Send("thickness", s.Thickness);
            Send("wisps", s.Wisps);
            Send("fps", s.Fps);
        }

        public void SetPaused(bool value)
        {
            paused = value;
            if (ready) Run($"livelyPropertyListener('paused', {(value ? "true" : "false")})");
        }

        private void Send(string name, int value) =>
            Run($"livelyPropertyListener('{name}', {value.ToString(CultureInfo.InvariantCulture)})");

        private async void Run(string script)
        {
            try
            {
                await web.CoreWebView2.ExecuteScriptAsync(
                    $"if (typeof livelyPropertyListener === 'function') {{ {script}; }}");
            }
            catch (Exception e) when (e is InvalidOperationException || e is ObjectDisposedException)
            {
                Log.Write("Script call skipped: " + e.Message);
            }
        }
    }
}
