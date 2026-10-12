using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static LiveBackground.Native;

namespace LiveBackground
{
    /// <summary>
    /// Reports whether the desktop is completely hidden: every monitor's work area covered by a
    /// visible, non-minimised app window (maximised or fullscreen). The wallpaper spans every
    /// monitor, so it only pauses when none of it can be seen.
    /// </summary>
    internal static class CoverageMonitor
    {
        private static readonly HashSet<string> ShellClasses = new HashSet<string>
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
        };

        public static bool DesktopHidden(IntPtr ownWindow)
        {
            Screen[] screens = Screen.AllScreens;
            var covered = new bool[screens.Length];
            int remaining = screens.Length;

            EnumWindows((h, _) =>
            {
                if (h == ownWindow || !IsWindowVisible(h) || IsIconic(h)) return true;
                if ((GetStyle(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                if (ShellClasses.Contains(ClassOf(h))) return true;

                if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, System.Runtime.InteropServices.Marshal.SizeOf(typeof(RECT))) != 0)
                    GetWindowRect(h, out r);
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);

                for (int i = 0; i < screens.Length; i++)
                {
                    if (covered[i]) continue;
                    Rectangle work = screens[i].WorkingArea;
                    work.Inflate(-2, -2); // tolerate frame rounding
                    if (rect.Contains(work))
                    {
                        covered[i] = true;
                        remaining--;
                    }
                }
                return remaining > 0;
            }, IntPtr.Zero);

            return remaining == 0;
        }
    }
}
