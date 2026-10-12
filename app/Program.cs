using System;
using System.Threading;
using System.Windows.Forms;

namespace LiveBackground
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            using (var single = new Mutex(true, @"Local\LiveBackground.SingleInstance", out bool first))
            {
                if (!first) return; // already running; its tray icon is the way in

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("Unhandled UI exception: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Unhandled exception: " + e.ExceptionObject);

                Log.Write($"Starting Live Background {Application.ProductVersion} on {Environment.OSVersion}");
                Application.Run(new TrayApp());
                GC.KeepAlive(single);
            }
        }
    }
}
