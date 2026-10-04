using Conch.Utilities;

namespace Conch.Services.Roles
{
    /// <summary>
    /// The well-known jobs the shell asks something else to do.
    /// </summary>
    /// <remarks>
    /// A role is a slot the shell opens itself -- "show me this folder", "edit this file",
    /// "configure the network" -- as opposed to an app the user picks by name from the
    /// launcher. What fills a slot is the user's choice; the shell only ever names the role.
    ///
    /// Some roles have an implementation built into Conch, which is the default. Any of them
    /// can be filled instead by a registration declaring the role in its <c>roles:</c> list.
    /// The two are interchangeable by construction: see <see cref="IRoleProvider"/>.
    ///
    /// There is no launcher role. Starting things is the top bar's search box, part of the
    /// shell itself rather than a slot something else could fill.
    /// </remarks>
    public static class ShellRoles
    {
        /// <summary>Browse, install and remove apps. Built in: the app manager.</summary>
        public const string AppManager = "app-manager";

        /// <summary>Open a folder. Built in: Files.</summary>
        public const string FileExplorer = "file-explorer";

        /// <summary>Open a file for editing. No built-in; nano, vim, Edit and Edit.NET fill it.</summary>
        public const string TextEditor = "text-editor";

        /// <summary>Show system resource use. No built-in; btop and htop fill it.</summary>
        public const string SystemMonitor = "system-monitor";

        /// <summary>
        /// Configure networking. Built in: Network, which drives nmcli and therefore needs
        /// NetworkManager; nmtui fills the role where that is not wanted.
        /// </summary>
        public const string NetworkConfig = "network-config";

        /// <summary>
        /// Configure audio. No built-in, and nothing to configure on Conchix yet -- its
        /// package list carries no audio stack at all, so alsamixer and pulsemixer would
        /// have no server to reach.
        /// </summary>
        public const string AudioConfig = "audio-config";

        /// <summary>
        /// Configure the display: on a console, the font and its size, which decide how many
        /// columns and rows everything gets. No built-in, because where those live depends
        /// entirely on what is drawing the console -- kmscon's config file on Conchix, a
        /// terminal emulator's own settings anywhere else -- and Conch knows nothing about
        /// either. A distribution fills the role with a tool of its own.
        /// </summary>
        public const string DisplayConfig = "display-config";

        /// <summary>
        /// The app a role should default to on each OS, when nothing has been chosen.
        /// </summary>
        /// <remarks>
        /// The editor each platform's own users already know. On Windows that is Microsoft's
        /// Edit -- mouse-driven, like Conch, and MS-DOS Edit's successor. On Linux it is nano:
        /// Debian installs it by default and points <c>editor</c> at it, so a Conchix user has
        /// it before installing anything. vim-tiny is also there, and is not a default anybody
        /// wants to find themselves in.
        ///
        /// A preference, not a requirement: when the app is not installed the role falls back
        /// to whatever is, exactly as before.
        /// </remarks>
        private static readonly Dictionary<(string Role, HostOs Os), string> PreferredDefaults = new()
        {
            [(TextEditor, HostOs.Windows)] = "edit.exe",
            [(TextEditor, HostOs.Linux)] = "gnu.nano",

            // Conchix's own tool. Conch does not detect Conchix: everywhere else this app is
            // simply not installed, so the preference falls through to whatever is.
            [(DisplayConfig, HostOs.Linux)] = "conchix.display",
        };

        /// <summary>The registration id <paramref name="role"/> should default to on <paramref name="os"/>, if any.</summary>
        public static string? PreferredDefault(string role, HostOs os)
            => PreferredDefaults.TryGetValue((role, os), out var id) ? id : null;

        /// <summary>Every role this build knows, in the order Settings should present them.</summary>
        public static readonly IReadOnlyList<string> All =
        [
            FileExplorer,
            AppManager,
            TextEditor,
            SystemMonitor,
            NetworkConfig,
            AudioConfig,
            DisplayConfig,
        ];

        private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            [AppManager] = "Software",
            [FileExplorer] = "File Explorer",
            [TextEditor] = "Text Editor",
            [SystemMonitor] = "System Monitor",
            [NetworkConfig] = "Network Configuration",
            [AudioConfig] = "Audio Configuration",
            [DisplayConfig] = "Display Configuration",
        };

        /// <summary>True when this build recognises <paramref name="role"/>.</summary>
        public static bool IsKnown(string role) => Names.ContainsKey(role);

        /// <summary>
        /// The name to show for a role. An unknown role is shown as itself rather than
        /// hidden, so a catalog declaring a role newer than this build stays legible.
        /// </summary>
        public static string DisplayName(string role)
            => Names.TryGetValue(role, out var name) ? name : role;
    }
}
