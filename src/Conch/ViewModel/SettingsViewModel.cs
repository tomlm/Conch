using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Conch.Services.Roles;
using Conch.Utilities;

namespace Conch.ViewModel
{
    /// <summary>
    /// Backs the Settings window: which app serves each role, how Conch looks, and what it is
    /// running on.
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        /// <summary>
        /// The themes Consolonia ships, in the order Settings offers them.
        /// </summary>
        /// <remarks>
        /// Names rather than instances: the chosen one is written to settings and turned back
        /// into a theme at startup, so this list is data and stays free of Avalonia.
        ///
        /// ModernDarkTheme and TurboVisionDarkTheme are deliberately absent. Consolonia ships
        /// both as classes with no XAML behind them, so constructing either throws
        /// "No precompiled XAML found" -- they are declared but not usable, and offering a
        /// theme that cannot be built would be a setting that persists and then breaks startup.
        /// </remarks>
        public static readonly IReadOnlyList<string> ThemeNames =
        [
            "Modern",
            "ModernContrast",
            "TurboVision",
            "TurboVisionGray",
            "TurboVisionElegant",
            "TurboVisionCompatible",
        ];

        private readonly AppViewModel _app;
        private bool _loading;

        public SettingsViewModel(AppViewModel app, RoleRegistry roles)
        {
            _app = app;

            Roles = new ObservableCollection<RoleSettingViewModel>(
                ShellRoles.All.Select(role => new RoleSettingViewModel(
                    role,
                    roles.CandidatesFor(role),
                    app.Settings.GetRoleChoice(role),
                    (r, id) => app.Settings.SetRoleChoice(r, id))));

            Themes = new ObservableCollection<string>(ThemeNames);

            _loading = true;
            SelectedTheme = app.Settings.Theme ?? AppViewModel.DefaultTheme;
            _loading = false;
        }

        public ObservableCollection<RoleSettingViewModel> Roles { get; }

        public ObservableCollection<string> Themes { get; }

        [ObservableProperty]
        private string? _selectedTheme;

        partial void OnSelectedThemeChanged(string? value)
        {
            if (_loading || value == null)
            {
                return;
            }

            _app.Settings.Theme = value;
            _app.Theme = value;
        }

        /// <summary>
        /// Why the theme does not change as soon as it is picked.
        /// </summary>
        /// <remarks>
        /// Swapping the theme means replacing Application.Current.Styles[0], and Consolonia's
        /// own gallery has to null out the main window's content across that swap -- "otherwise
        /// Avalonia sets some trash template to WindowsPanel" -- and rebuild it afterwards. In
        /// Conch that content IS the WindowsPanel holding every open app, so a live swap would
        /// close every running program to change a colour. Applying at startup instead costs a
        /// restart and keeps the work.
        /// </remarks>
        public string ThemeNote => "Applied at startup. Open apps would be closed by changing it now.";

        public string Version => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0] ?? "unknown";

        public string Runtime => $".NET {Environment.Version}";

        public string OperatingSystem => Environment.OSVersion.ToString();

        public string Session => CommandLineOptions.Current.OwnsSession
            ? "Conch owns this session"
            : "Conch is running as an application";

        public string SettingsPath => Path.Combine(Log.StateDirectory, "settings.json");

        public string LogPath => Log.LogFilePath;

        public string CatalogPath => Path.Combine(Log.StateDirectory, "Tools");
    }
}
