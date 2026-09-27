using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Conch.ViewModel
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

        /// <summary>
        /// Optional shell command that exits zero when this app is already present.
        /// </summary>
        public string? Detect => GetInstallDefinitionForCurrentPlatform()?.Detect;

        public bool HasInstall => !string.IsNullOrWhiteSpace(Install);

        public bool HasUninstall => !string.IsNullOrWhiteSpace(Uninstall);

        /// <summary>
        /// Which platform entry this registration resolved to on the current machine.
        /// </summary>
        public ToolPlatform ResolvedPlatform
        {
            get
            {
                if (Platforms == null)
                {
                    return ToolPlatform.None;
                }

                if (OperatingSystem.IsWindows())
                {
                    if (Platforms.Windows != null) return ToolPlatform.Windows;
                    if (Platforms.Linux != null) return ToolPlatform.Linux;
                }
                else if (OperatingSystem.IsLinux() && Platforms.Linux != null)
                {
                    return ToolPlatform.Linux;
                }
                else if (OperatingSystem.IsMacOS() && Platforms.MacOS != null)
                {
                    return ToolPlatform.MacOS;
                }

                return Platforms.Default != null ? ToolPlatform.Default : ToolPlatform.None;
            }
        }

        /// <summary>
        /// True when this registration only describes a Linux build but we are on Windows, so its
        /// commands have to run inside WSL.
        /// </summary>
        public bool RunsUnderWsl => OperatingSystem.IsWindows() && ResolvedPlatform == ToolPlatform.Linux;

        /// <summary>
        /// Whether the app was found on this machine. Maintained by the detector, not the file.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallStateText))]
        private bool _isInstalled;

        /// <summary>
        /// True once detection has actually run, so the UI can distinguish "absent" from "unknown".
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallStateText))]
        private bool _isDetected;

        public string InstallStateText => !IsDetected
            ? "Checking..."
            : IsInstalled ? "Installed" : "Not installed";

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

            // On Windows a Linux-only registration still resolves, and its commands are then run
            // through WSL; see RunsUnderWsl.
            var specific = OperatingSystem.IsWindows() ? Platforms.Windows ?? Platforms.Linux
                : OperatingSystem.IsLinux() ? Platforms.Linux
                : OperatingSystem.IsMacOS() ? Platforms.MacOS
                : null;
            return specific ?? Platforms.Default;
        }
    }
}
