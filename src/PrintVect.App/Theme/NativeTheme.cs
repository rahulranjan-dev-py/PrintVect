using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PrintVect.Core.Logging;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// The few Windows calls the two looks need: Windows' own title bar in light or dark (Windows 10 1809 and later,
    /// Windows 11 also takes the side menu's colour), matching scroll bars, fonts registered for this process
    /// only, drawing paused while a page is put together, and the window kept off the screen until it is drawn. Each
    /// one is optional: on an older Windows the window simply keeps the standard frame or scroll bars.
    /// </summary>
    internal static class NativeTheme
    {
        private const int DwmaUseImmersiveDarkMode = 20;          // Windows 10 20H1 and later, Windows 11
        private const int DwmaUseImmersiveDarkModeOld = 19;       // Windows 10 1809 to 1909
        private const int DwmaBorderColor = 34;                    // Windows 11
        private const int DwmaCaptionColor = 35;                   // Windows 11
        private const int DwmaTextColor = 36;                      // Windows 11
        private const int DwmaCloak = 13;                          // Windows 8 and later
        private const uint FrPrivate = 0x10;
        private const uint SwpFrameChanged = 0x0020, SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

        private const int WmSetRedraw = 0x000B;
        private const uint RdwInvalidate = 0x0001, RdwErase = 0x0004, RdwAllChildren = 0x0080, RdwUpdateNow = 0x0100, RdwFrame = 0x0400;

        private static bool _dwmMissing;
        private static bool _uxThemeMissing;
        private static bool _user32Missing;

        /// <summary>
        /// Stops Windows drawing a control and everything in it until the result is disposed, then draws it once,
        /// finished. Opening a page shows it, sizes it and lays out its rows; without this every step reaches the
        /// screen and the page visibly jumps (owner, 0.5.0). Elsewhere (the Mono preview) it does nothing.
        /// </summary>
        public static IDisposable FreezeDrawing(Control control)
        {
            if (_user32Missing || Environment.OSVersion.Platform != PlatformID.Win32NT || !control.IsHandleCreated)
            {
                return new Frozen(null);
            }
            try
            {
                SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
                return new Frozen(control);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                _user32Missing = true;
                Log.Warn("Drawing cannot be paused on this Windows: " + ex.Message);
                return new Frozen(null);
            }
        }

        private sealed class Frozen : IDisposable
        {
            private Control _control;

            public Frozen(Control control)
            {
                _control = control;
            }

            public void Dispose()
            {
                Control control = _control;
                _control = null;
                if (control == null || control.IsDisposed || !control.IsHandleCreated) return;
                SendMessage(control.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren | RdwUpdateNow);
            }
        }

        /// <summary>Asks Windows for the title bar of the theme in use; on Windows 11 also paints it in the side menu's colour.</summary>
        public static void ApplyTitleBar(Form form, bool redraw = false)
        {
            if (_dwmMissing || !IsWindows10OrLater() || !form.IsHandleCreated)
            {
                return;
            }
            try
            {
                int dark = ThemeManager.IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, DwmaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, DwmaUseImmersiveDarkModeOld, ref dark, sizeof(int));
                }
                int caption = ColorRef(Palette.Sidebar);
                DwmSetWindowAttribute(form.Handle, DwmaCaptionColor, ref caption, sizeof(int));
                int border = ColorRef(Palette.Border);
                DwmSetWindowAttribute(form.Handle, DwmaBorderColor, ref border, sizeof(int));
                int text = ColorRef(Palette.Text);
                DwmSetWindowAttribute(form.Handle, DwmaTextColor, ref text, sizeof(int));
                if (redraw)
                {
                    // Windows 10 only repaints the frame in the new mode when told that it changed.
                    SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                _dwmMissing = true;
                Log.Warn("Themed title bar not available on this Windows: " + ex.Message);
            }
        }

        /// <summary>
        /// Hides a window from the screen while it keeps working and drawing (DWM cloak, Windows 8 and later), or shows it
        /// again. Returns false when Windows cannot: the window is then simply shown as before.
        /// </summary>
        public static bool Cloak(Form form, bool cloak)
        {
            OperatingSystem os = Environment.OSVersion;
            if (_dwmMissing || os.Platform != PlatformID.Win32NT || os.Version < new Version(6, 2))
            {
                return false;
            }
            try
            {
                int value = cloak ? 1 : 0;
                return DwmSetWindowAttribute(form.Handle, DwmaCloak, ref value, sizeof(int)) == 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                _dwmMissing = true;
                Log.Warn("Windows cannot hide the window while it is drawn: " + ex.Message);
                return false;
            }
        }

        /// <summary>Draws a window and everything in it right now, instead of whenever Windows gets round to it.</summary>
        public static void DrawNow(Control control)
        {
            if (_user32Missing || Environment.OSVersion.Platform != PlatformID.Win32NT || !control.IsHandleCreated)
            {
                return;
            }
            try
            {
                RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, RdwErase | RdwFrame | RdwInvalidate | RdwAllChildren | RdwUpdateNow);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                _user32Missing = true;
                Log.Warn("Drawing cannot be forced on this Windows: " + ex.Message);
            }
        }

        /// <summary>
        /// Scroll bars of a list, grid or text box in the theme in use (dark ones: Windows 10 1809 and later). A list's
        /// own scroll bars (a DataGridView has two) get their window only when they are first needed, so each one is
        /// themed then: the job list's stayed light in the dark colours (owner's photo, 0.5.2).
        /// </summary>
        public static void UseThemedScrollBars(Control control)
        {
            if (_uxThemeMissing || !IsWindows10OrLater())
            {
                return;
            }
            Hook(control);
            foreach (Control child in control.Controls)
            {
                if (child is ScrollBar) Hook(child);
            }
            control.ControlAdded += (s, e) =>
            {
                if (e.Control is ScrollBar) Hook(e.Control);
            };
        }

        private static void Hook(Control control)
        {
            if (control.IsHandleCreated)
            {
                Apply(control);
            }
            control.HandleCreated += (s, e) => Apply((Control)s);
        }

        private static void Apply(Control control)
        {
            string theme = ThemeManager.IsDark ? "DarkMode_Explorer" : "Explorer";
            try
            {
                SetWindowTheme(control.Handle, theme, null);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                _uxThemeMissing = true;
                Log.Warn("Themed scroll bars not available on this Windows: " + ex.Message);
            }
        }

        /// <summary>
        /// Makes a font file visible to GDI (the text of standard controls) in this process only. GDI+ gets the same
        /// file through a PrivateFontCollection; both are needed for one font to look the same everywhere.
        /// </summary>
        public static bool RegisterPrivateFont(string path)
        {
            try
            {
                return AddFontResourceEx(path, FrPrivate, IntPtr.Zero) > 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                return false;   // not Windows (the Mono preview); GDI+ still has the font
            }
        }

        private static bool IsWindows10OrLater()
        {
            OperatingSystem os = Environment.OSVersion;
            return os.Platform == PlatformID.Win32NT && os.Version.Major >= 10;
        }

        private static int ColorRef(System.Drawing.Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
