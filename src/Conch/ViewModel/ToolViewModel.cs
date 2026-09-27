using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Conduit.ViewModel
{
    /// <summary>
    /// View model for a tool
    /// </summary>
    public partial class ToolViewModel : ObservableValidator
    {
        /// <summary>
        /// Id for the tool
        /// </summary>
        [Required]
        [MinLength(1)]
        [RegularExpression(@"^[a-z0-9.]+$", ErrorMessage = "Id must contain only alphanumeric characters or periods.")]
        [ObservableProperty]
        private string _id = String.Empty;

        /// <summary>
        /// Name of the tool
        /// </summary>
        [Required]
        [MinLength(1)]
        [ObservableProperty]
        private string _name = "Unknown";

        /// <summary>
        /// Description of tool
        /// </summary>
        [ObservableProperty]
        private string _description = "Unknown";

        /// <summary>
        /// Gets or sets the semver version string
        /// </summary>
        [ObservableProperty]
        private string _version = "0.0.0";

        /// <summary>
        /// Link to source code
        /// </summary>
        [ObservableProperty]
        private string _source = "about:blank";

        /// <summary>
        /// Link to documentation
        /// </summary>
        [ObservableProperty]
        private string _documentation = "about:blank";

        [ObservableProperty]
        private string _website = "about:blank";

        public PlatformInstallDefinitions Platforms { get; set; } = new();

        /// <summary>
        /// Keywords for the tool
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(KeywordsText))]
        private ObservableCollection<string> _keywords = new ObservableCollection<string>();

        public string KeywordsText => Keywords == null || Keywords.Count == 0
            ? string.Empty
            : string.Join(", ", Keywords);

        public string Install => GetInstallDefinitionForCurrentPlatform()?.Install ?? string.Empty;

        public string Uninstall => GetInstallDefinitionForCurrentPlatform()?.Uninstall ?? string.Empty;

        public string PlatformSummary
        {
            get
            {
                if (Platforms == null)
                {
                    return string.Empty;
                }

                var platforms = new List<string>();

                if (Platforms.Default != null) platforms.Add("Default");
                if (Platforms.Windows != null) platforms.Add("Windows");
                if (Platforms.Linux != null) platforms.Add("Linux");
                if (Platforms.MacOS != null) platforms.Add("MacOS");

                return string.Join("|", platforms);
            }
        }

        /// <summary>
        /// Command to execute tool
        /// </summary>
        [ObservableProperty]
        [Required]
        [MinLength(1)]
        private string _command = String.Empty;

        /// <summary>
        /// Args to the command
        /// </summary>
        [ObservableProperty]
        private string _args = string.Empty;

        /// <summary>
        /// Desitred initial width of terminal
        /// </summary>
        [ObservableProperty]
        private int _width = 80;

        /// <summary>
        /// Desired initial height of terminal
        /// </summary>
        [ObservableProperty]
        private int _height = 25;

        /// <summary>
        /// tool icon which 4 lines of 8 characters each
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<string> _icon = new ObservableCollection<string>();

        /// <summary>
        /// Validates this model instance using data annotations.
        /// </summary>
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, validateAllProperties: true);

            if (Platforms == null)
            {
                throw new ValidationException("A tool must define platform install entries.");
            }

            var entries = new[] { Platforms.Default, Platforms.Windows, Platforms.Linux, Platforms.MacOS }
                .Where(e => e != null)
                .Cast<InstallDefinition>()
                .ToList();

            if (entries.Count == 0)
            {
                throw new ValidationException("A tool must define at least one platform install entry.");
            }

            foreach (var entry in entries)
            {
                var entryContext = new ValidationContext(entry);
                Validator.ValidateObject(entry, entryContext, validateAllProperties: true);
            }
        }

        private InstallDefinition? GetInstallDefinitionForCurrentPlatform()
        {
            if (Platforms == null)
            {
                return null;
            }

            var specific = OperatingSystem.IsWindows() ? Platforms.Windows ?? Platforms.Linux
                : OperatingSystem.IsLinux() ? Platforms.Linux
                : OperatingSystem.IsMacOS() ? Platforms.MacOS
                : null;
            return specific ?? Platforms.Default;
        }
    }
}
