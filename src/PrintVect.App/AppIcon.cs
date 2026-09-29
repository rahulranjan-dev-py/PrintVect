using System;
using System.Drawing;
using System.IO;
using PrintVect.Core.Logging;

namespace PrintVect.App
{
    /// <summary>The PrintVect icon, embedded in the EXE so the tray never shows a blank square.</summary>
    internal static class AppIcon
    {
        private const string ResourceName = "PrintVect.App.PrintVect.ico";
        private static Icon _window;
        private static Icon _tray;

        /// <summary>Icon for the window title bar and taskbar.</summary>
        public static Icon Window
        {
            get { return _window ?? (_window = Load(null)); }
        }

        /// <summary>16 x 16 icon for the notification area.</summary>
        public static Icon Tray
        {
            get { return _tray ?? (_tray = Load(new Size(16, 16))); }
        }

        private static Icon Load(Size? size)
        {
            try
            {
                using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream != null)
                    {
                        return size.HasValue ? new Icon(stream, size.Value) : new Icon(stream);
                    }
                    Log.Warn("Embedded icon " + ResourceName + " not found; using the default Windows icon.");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load the embedded icon; using the default Windows icon. " + ex.Message);
            }
            return SystemIcons.Application;
        }
    }
}
