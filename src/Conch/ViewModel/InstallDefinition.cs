using System.ComponentModel.DataAnnotations;

namespace Conch.ViewModel
{
    public class InstallDefinition
    {
        [Required]
        [MinLength(1)]
        public string Install { get; set; } = string.Empty;

        [Required]
        [MinLength(1)]
        public string Uninstall { get; set; } = string.Empty;

        /// <summary>
        /// Optional shell command that exits zero when the app is already installed.
        /// </summary>
        /// <remarks>
        /// When absent, presence is inferred from whether the registration's command resolves on
        /// PATH. That covers a plain binary but not an app reached through a launcher, a runtime
        /// or a wrapper script, which is what this is for.
        /// </remarks>
        public string? Detect { get; set; }
    }
}
