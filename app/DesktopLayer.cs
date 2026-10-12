using System;
using static LiveBackground.Native;

namespace LiveBackground
{
    /// <summary>
    /// Places a window between the desktop wallpaper and the desktop icons.
    ///
    /// Classic desktop (Windows 10, Windows 11 before 24H2): sending 0x052C to Progman makes
    /// Explorer split the desktop into a WorkerW that holds the icons and an empty WorkerW
    /// behind it. We become a child of that empty WorkerW.
    ///
    /// Raised desktop (Windows 11 24H2+): Progman has WS_EX_NOREDIRECTIONBITMAP, the icon view
    /// (SHELLDLL_DefView) is a layered child of Progman and the wallpaper is drawn by a WorkerW
    /// child at the bottom. Microsoft's guidance is to add our own WS_EX_LAYERED child of Progman
    /// (alpha 255) z-ordered below the icon view and above that WorkerW.
    /// </summary>
    internal sealed class DesktopLayer
    {
        public IntPtr Progman { get; private set; }
        public IntPtr WorkerW { get; private set; }
        public IntPtr DefView { get; private set; }
        public bool Raised { get; private set; }

        public bool Discover()
        {
            Progman = FindWindow("Progman", null);
            if (Progman == IntPtr.Zero)
            {
                Log.Write("Progman not found; is Explorer running?");
                return false;
            }

            Raised = (GetStyle(Progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP) != 0;

            // wParam 0xD / lParam 1 is what current wallpaper apps send; zeros fail on 24H2.
            SendMessageTimeout(Progman, 0x052C, new IntPtr(0xD), new IntPtr(1), SMTO_NORMAL, 1000, out _);

            WorkerW = IntPtr.Zero;
            DefView = IntPtr.Zero;

            if (Raised)
            {
                DefView = FindWindowEx(Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                WorkerW = FindWindowEx(Progman, IntPtr.Zero, "WorkerW", null);
            }
            else
            {
                EnumWindows((top, _) =>
                {
                    IntPtr view = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (view != IntPtr.Zero)
                    {
                        DefView = view;
                        WorkerW = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                    }
                    return true;
                }, IntPtr.Zero);
            }

            Log.Write($"Desktop: raised={Raised} progman={Progman} workerW={WorkerW} defView={DefView}");
            return Raised ? DefView != IntPtr.Zero : WorkerW != IntPtr.Zero;
        }

        /// <summary>Area the wallpaper must cover, in the parent's coordinates (origin 0,0).</summary>
        public RECT Bounds()
        {
            GetWindowRect(Raised ? Progman : WorkerW, out RECT r);
            return r;
        }

        public bool Attach(IntPtr hwnd)
        {
            long style = GetStyle(hwnd, GWL_STYLE);
            style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
            style |= WS_CHILD | WS_CLIPSIBLINGS;
            SetStyle(hwnd, GWL_STYLE, style);

            long ex = GetStyle(hwnd, GWL_EXSTYLE);
            ex &= ~WS_EX_APPWINDOW;
            ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (Raised) ex |= WS_EX_LAYERED;
            SetStyle(hwnd, GWL_EXSTYLE, ex);
            if (Raised && !SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA))
                Log.Win32("SetLayeredWindowAttributes failed");

            IntPtr parent = Raised ? Progman : WorkerW;
            if (SetParent(hwnd, parent) == IntPtr.Zero && GetParent(hwnd) != parent)
            {
                Log.Win32("SetParent failed");
                return false;
            }

            RECT b = Bounds();
            IntPtr after = Raised ? DefView : IntPtr.Zero; // directly below the icons
            uint flags = SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW | (Raised ? 0 : SWP_NOZORDER);
            if (!SetWindowPos(hwnd, after, 0, 0, b.Width, b.Height, flags))
                Log.Win32("SetWindowPos failed");

            EnsureZOrder(hwnd);
            Log.Write($"Attached {hwnd} to {parent}, size {b.Width}x{b.Height}");
            return true;
        }

        /// <summary>On the raised desktop Explorer's WorkerW must stay at the bottom, under us.</summary>
        public void EnsureZOrder(IntPtr hwnd)
        {
            if (!Raised || WorkerW == IntPtr.Zero) return;
            if (GetWindow(GetTopWindow(Progman), GW_HWNDLAST) != WorkerW)
            {
                SetWindowPos(WorkerW, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                Log.Write("Restored WorkerW to the bottom of the desktop.");
            }
        }

        public bool StillValid(IntPtr hwnd)
        {
            IntPtr parent = Raised ? Progman : WorkerW;
            return IsWindow(parent) && GetParent(hwnd) == parent;
        }

        /// <summary>Repaints the static wallpaper after we leave (classic desktop only;
        /// on the raised desktop this would destroy Explorer's WorkerW).</summary>
        public void Refresh()
        {
            if (!Raised) SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_UPDATEINIFILE);
        }
    }
}
