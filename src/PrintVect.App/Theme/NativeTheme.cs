using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PrintVect.Core.Logging;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// The few Windows calls the dark look needs: Windows' own dark title bar (Windows 10 1809 and later, Windows 11),
    /// dark scroll bars, and fonts registered for this process only. Each one is optional: on an older Windows the
    /// window simply keeps the light frame or scroll bars.
    /// </summary>
    internal static class NativeTheme
    {
        private const int DwmaUseImmersiveDarkMode = 20;          // Windows 10 20H1 and later, Windows 11
        private const int DwmaUseImmersiveDarkModeOld = 19;       // Windows 10 1809 to 1909
        private const int DwmaBorderColor = 34;                    // Windows 11
        private const int DwmaCaptionColor = 35;                   // Windows 11
        private const uint FrPrivate = 0x10;

        private static bool _dwmMissing;
        private static bool _uxThemeMissing;

        /// <summary>Asks Windows for the dark title bar; on Windows 11 also paints it in the top bar's colour.</summary>
        public static void UseDarkTitleBar(Form form)
        {
            if (_dwmMissing || !IsWindows10OrLater())
            {
                return;
            }
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(form.Handle, DwmaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, DwmaUseImmersiveDarkModeOld, ref on, sizeof(int));
                }
                int caption = ColorRef(Palette.TopBar);
                DwmSetWindowAttribute(form.Handle, DwmaCaptionColor, ref caption, sizeof(int));
                int border = ColorRef(Palette.Border);
                DwmSetWindowAttribute(form.Handle, DwmaBorderColor, ref border, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                _dwmMissing = true;
                Log.Warn("Dark title bar not available on this Windows: " + ex.Message);
            }
        }

        /// <summary>Dark scroll bars for a list, grid or text box (Windows 10 1809 and later).</summary>
        public static void UseDarkScrollBars(Control control)
        {
            if (_uxThemeMissing || !IsWindows10OrLater())
            {
                return;
            }
            if (control.IsHandleCreated)
            {
                Apply(control);
            }
            control.HandleCreated += (s, e) => Apply((Control)s);
        }

        private static void Apply(Control control)
        {
            try
            {
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
                foreach (Control child in control.Controls)
                {
                    if (child is ScrollBar && child.IsHandleCreated)
                    {
                        SetWindowTheme(child.Handle, "DarkMode_Explorer", null);
                    }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                _uxThemeMissing = true;
                Log.Warn("Dark scroll bars not available on this Windows: " + ex.Message);
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
    }
}
