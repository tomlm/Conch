using Avalonia.Styling;
using Conch.Utilities;
using Consolonia.Themes;

namespace Conch.Services
{
    /// <summary>
    /// Turns a stored theme name back into the theme it names.
    /// </summary>
    /// <remarks>
    /// Settings hold a name rather than a theme so the file stays readable and the view model
    /// stays free of Avalonia. This is the one place that knows what the names mean.
    /// </remarks>
    public static class ConsoleThemes
    {
        /// <summary>
        /// The theme called <paramref name="name"/>, or null when nothing is called that.
        /// </summary>
        /// <remarks>
        /// ModernDark and TurboVisionDark are missing on purpose. Consolonia declares both as
        /// classes with no XAML behind them, so constructing one throws "No precompiled XAML
        /// found for Consolonia.Themes.ModernDarkTheme". Leaving them out means a settings file
        /// naming one falls back to the default rather than taking the shell down at startup.
        /// </remarks>
        public static Styles? Create(string? name) => name switch
        {
            "Modern" => new ModernTheme(),
            "ModernContrast" => new ModernContrastTheme(),
            "TurboVision" => new TurboVisionTheme(),
            "TurboVisionGray" => new TurboVisionGrayTheme(),
            "TurboVisionElegant" => new TurboVisionElegantTheme(),
            "TurboVisionCompatible" => new TurboVisionCompatibleTheme(),
            _ => null,
        };

        /// <summary>
        /// Applies the saved theme to <paramref name="styles"/> at startup.
        /// </summary>
        /// <remarks>
        /// At startup, and only at startup. Swapping a theme means replacing the application's
        /// first style, and Consolonia's own gallery has to null out the main window's content
        /// across that swap -- "otherwise Avalonia sets some trash template to WindowsPanel" --
        /// and rebuild it afterwards. In Conch that content is the panel holding every open
        /// app, so doing this live would close every running program to change a colour.
        ///
        /// Here there is nothing to tear down yet: App.axaml has declared the default theme as
        /// the first style and no window exists. Replacing it costs nothing.
        ///
        /// A name with no theme behind it -- an older build meeting a newer settings file, or a
        /// hand edit -- leaves the default in place rather than failing to start.
        /// </remarks>
        public static void ApplyAtStartup(Styles styles, string? name)
        {
            if (string.IsNullOrEmpty(name) || styles.Count == 0)
            {
                return;
            }

            var theme = Create(name);
            if (theme == null)
            {
                Log.Warning("Settings", $"No theme called '{name}'; keeping the default.");
                return;
            }

            styles[0] = theme;
            Log.Info("Settings", $"Theme: {name}.");
        }
    }
}
