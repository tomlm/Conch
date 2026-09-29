using CommunityToolkit.Mvvm.ComponentModel;
using Conch.Services;
using System.Text.Json.Serialization;

namespace Conch.ViewModel
{
    public partial class AppViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _theme;

        public ToolsViewModel Tools { get; init; }

        /// <summary>
        /// The user's shell preferences. Read once here; written as they are changed.
        /// </summary>
        public ShellSettings Settings { get; init; }

        public AppViewModel()
            : this(ShellSettings.Default().Load())
        {
        }

        /// <param name="settings">
        /// Injected so a test can point at a temporary directory rather than the real profile.
        /// </param>
        public AppViewModel(ShellSettings settings)
        {
            Settings = settings;
            Tools = new ToolsViewModel();
            Theme = settings.Theme ?? DefaultTheme;
        }

        /// <summary>
        /// What Conch looks like before anyone chooses otherwise, matching the theme declared
        /// in App.axaml.
        /// </summary>
        public const string DefaultTheme = "Modern";
    }
}
