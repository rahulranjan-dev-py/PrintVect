using System;
using Microsoft.Win32;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;

namespace PrintVect.App.Theme
{
    internal enum ThemeMode
    {
        /// <summary>Follows Windows: Settings, Personalisation, Colours, "Choose your app mode".</summary>
        System,
        Light,
        Dark
    }

    /// <summary>
    /// Which colours are in use (0.5.0, owner's request: a switch for Dark, Light and System default). System default
    /// reads Windows' app mode and follows it when it changes; Windows 7 and 8 have no dark app mode, so they get Light.
    /// </summary>
    internal static class ThemeManager
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightTheme = "AppsUseLightTheme";

        private static ThemeMode _mode = ThemeMode.System;
        private static bool _listening;

        public static PaletteSet Colors { get; private set; } = PaletteSet.Light;

        public static ThemeMode Mode
        {
            get { return _mode; }
        }

        public static bool IsDark
        {
            get { return Colors.IsDark; }
        }

        /// <summary>Raised on the UI thread when the colours in use changed (a new choice, or Windows switched).</summary>
        public static event EventHandler Changed;

        /// <summary>At start-up, on the UI thread, before any window exists.</summary>
        public static void Initialize(string configured)
        {
            _mode = Parse(configured);
            Colors = Resolve();
            if (!_listening)
            {
                _listening = true;
                try
                {
                    SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                }
                catch (Exception ex)
                {
                    Log.Warn("Colours: cannot follow Windows' light or dark mode while running: " + ex.Message);
                }
            }
            Log.Info("Colours: " + ToConfig(_mode) + " (" + Colors.Name + " in use).");
        }

        public static void SetMode(ThemeMode mode)
        {
            if (mode == _mode)
            {
                return;
            }
            _mode = mode;
            Log.Info("Colours: " + ToConfig(mode) + " chosen in Settings.");
            Update();
        }

        public static void Shutdown()
        {
            if (_listening)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _listening = false;
            }
        }

        public static ThemeMode Parse(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case AppConfig.ThemeLight:
                    return ThemeMode.Light;
                case AppConfig.ThemeDark:
                    return ThemeMode.Dark;
                default:
                    return ThemeMode.System;
            }
        }

        public static string ToConfig(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Light:
                    return AppConfig.ThemeLight;
                case ThemeMode.Dark:
                    return AppConfig.ThemeDark;
                default:
                    return AppConfig.ThemeSystem;
            }
        }

        /// <summary>Windows' app mode: true for light (also when Windows has no such setting), false for dark.</summary>
        public static bool WindowsUsesLightApps()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    object value = key == null ? null : key.GetValue(AppsUseLightTheme);
                    return !(value is int) || (int)value != 0;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Colours: Windows' app mode could not be read, using light: " + ex.Message);
                return true;
            }
        }

        private static PaletteSet Resolve()
        {
            switch (_mode)
            {
                case ThemeMode.Light:
                    return PaletteSet.Light;
                case ThemeMode.Dark:
                    return PaletteSet.Dark;
                default:
                    return WindowsUsesLightApps() ? PaletteSet.Light : PaletteSet.Dark;
            }
        }

        private static void Update()
        {
            PaletteSet next = Resolve();
            if (ReferenceEquals(next, Colors))
            {
                return;
            }
            Colors = next;
            Log.Info("Colours: now " + next.Name + ".");
            EventHandler handler = Changed;
            if (handler == null) return;
            try
            {
                handler(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error("A window could not take the new colours.", ex);
            }
        }

        /// <summary>Windows says a setting changed (its light / dark switch is one of them): follow it in System default.</summary>
        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_mode == ThemeMode.System && (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color
                                              || e.Category == UserPreferenceCategory.VisualStyle))
            {
                Update();
            }
        }
    }
}
