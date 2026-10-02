using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Conch.Utilities;

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

        /// <summary>Who makes the app, as the registration says.</summary>
        [ObservableProperty]
        private string _author = string.Empty;

        /// <summary>The app's licence, as an SPDX id where the registration gives one.</summary>
        [ObservableProperty]
        private string _license = string.Empty;

        /// <summary>
        /// Link to a picture of the app running, or empty when there is none.
        /// </summary>
        /// <remarks>
        /// A URL rather than a file in the catalog: the catalog is refreshed from GitHub file by
        /// file, and a picture is worth fetching only when someone opens the app's details.
        /// </remarks>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasScreenshot))]
        private string _screenshot = string.Empty;

        public bool HasScreenshot => Uri.TryCreate(Screenshot, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

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

        /// <summary>
        /// Well-known shell roles this app can fill, such as <c>text-editor</c>.
        /// </summary>
        /// <remarks>
        /// A role is a slot the shell itself opens -- "show me this folder", "edit this
        /// file" -- rather than something the user launches by name. Declaring one here
        /// offers the app as a candidate; which candidate actually serves a role is the
        /// user's choice, kept in settings. See <see cref="Services.Roles.ShellRoles"/>.
        ///
        /// An unrecognised name is kept rather than rejected. The catalog refreshes from
        /// GitHub at startup, so an older build will meet roles that did not exist when it
        /// shipped, and failing validation over one would make publishing a new role break
        /// every copy of Conch already installed.
        /// </remarks>
        [ObservableProperty]
        private ObservableCollection<string> _roles = new ObservableCollection<string>();

        /// <summary>
        /// Ids of other registrations that must be installed before this one can be.
        /// </summary>
        /// <remarks>
        /// A prerequisite is an ordinary registration, not a second kind of thing: it has the
        /// same per-platform install commands and the same detection, so the machinery that
        /// installs an app is the machinery that installs what the app needs.
        ///
        /// This is what keeps a toolchain off the image. A .NET tool needs the SDK -- 610 MB
        /// of it -- and baking that in would spend two thirds of the base system on one class
        /// of app that most machines will never install. Declaring it instead means it arrives
        /// the first time somebody actually wants it.
        ///
        /// An id naming nothing in the catalog is reported when the install is attempted rather
        /// than at load, for the same reason an unknown role is kept: the catalog refreshes from
        /// GitHub, and a registration may name a prerequisite published after this build.
        /// </remarks>
        [ObservableProperty]
        private ObservableCollection<string> _requires = new ObservableCollection<string>();

        public string Install => GetInstallDefinitionFor(Host.Current)?.Install ?? string.Empty;

        public string Uninstall => GetInstallDefinitionFor(Host.Current)?.Uninstall ?? string.Empty;

        /// <summary>
        /// Optional shell command that exits zero when this app is already present.
        /// </summary>
        public string? Detect => GetInstallDefinitionFor(Host.Current)?.Detect;

        public bool HasInstall => !string.IsNullOrWhiteSpace(Install);

        public bool HasUninstall => !string.IsNullOrWhiteSpace(Uninstall);

        /// <summary>
        /// Which platform entry this registration resolved to on the current machine.
        /// </summary>
        public ToolPlatform ResolvedPlatform => ResolvePlatformFor(Host.Current);

        /// <summary>
        /// Which platform entry this registration resolves to on <paramref name="os"/>.
        /// </summary>
        public ToolPlatform ResolvePlatformFor(HostOs os)
        {
            if (Platforms == null)
            {
                return ToolPlatform.None;
            }

            switch (os)
            {
                case HostOs.Windows:
                    if (Platforms.Windows != null) return ToolPlatform.Windows;
                    // A Linux-only registration still resolves on Windows; its commands are then
                    // routed through WSL. See RunsUnderWslOn.
                    if (Platforms.Linux != null) return ToolPlatform.Linux;
                    break;
                case HostOs.Linux:
                    if (Platforms.Linux != null) return ToolPlatform.Linux;
                    break;
                case HostOs.MacOS:
                    if (Platforms.MacOS != null) return ToolPlatform.MacOS;
                    break;
            }

            return Platforms.Default != null ? ToolPlatform.Default : ToolPlatform.None;
        }

        /// <summary>
        /// True when this registration only describes a Linux build but we are on Windows, so its
        /// commands have to run inside WSL.
        /// </summary>
        public bool RunsUnderWsl => RunsUnderWslOn(Host.Current);

        /// <summary>
        /// Whether this registration's commands must be routed through WSL on <paramref name="os"/>.
        /// </summary>
        public bool RunsUnderWslOn(HostOs os) => os == HostOs.Windows && ResolvePlatformFor(os) == ToolPlatform.Linux;

        /// <summary>
        /// Whether this app can exist on this machine at all: there is an entry for it here, and
        /// if that entry is a Linux one on Windows, there is a WSL distribution to run it in.
        /// </summary>
        /// <remarks>
        /// Not whether it is installed -- that is <see cref="IsInstalled"/>. This is what decides
        /// whether it is offered at all: Microsoft's Edit has no business in the app list on
        /// Debian, and nano belongs in it on Windows only when WSL can run it or winget can
        /// install it natively.
        /// </remarks>
        public bool IsAvailableHere => IsAvailableOn(Host.Current, Wsl.IsAvailable);

        /// <summary>
        /// Whether this app can exist on <paramref name="os"/>, given whether WSL is usable there.
        /// </summary>
        public bool IsAvailableOn(HostOs os, bool wslAvailable)
            => ResolvePlatformFor(os) != ToolPlatform.None
               && (!RunsUnderWslOn(os) || wslAvailable);

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
        /// Columns of terminal grid the app wants. 80 unless the registration says otherwise.
        /// </summary>
        /// <remarks>
        /// Grid, not window. The window is sized around this, so an app asking for 80x25
        /// gets 80x25 to write into rather than 80x25 minus the border and title bar --
        /// which is below the floor several TUIs refuse to start under.
        /// </remarks>
        [ObservableProperty]
        private int _cols = 80;

        /// <summary>
        /// Rows of terminal grid the app wants. 25 unless the registration says otherwise.
        /// </summary>
        [ObservableProperty]
        private int _rows = 25;

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

        /// <summary>
        /// The install entry this registration resolves to on <paramref name="os"/>.
        /// </summary>
        public InstallDefinition? GetInstallDefinitionFor(HostOs os)
        {
            if (Platforms == null)
            {
                return null;
            }

            return ResolvePlatformFor(os) switch
            {
                ToolPlatform.Windows => Platforms.Windows,
                ToolPlatform.Linux => Platforms.Linux,
                ToolPlatform.MacOS => Platforms.MacOS,
                ToolPlatform.Default => Platforms.Default,
                _ => null,
            };
        }
    }
}
