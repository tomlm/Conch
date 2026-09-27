using System.ComponentModel.DataAnnotations;

namespace Conduit.ViewModel
{
    public class InstallDefinition
    {
        [Required]
        [MinLength(1)]
        public string Install { get; set; } = string.Empty;

        [Required]
        [MinLength(1)]
        public string Uninstall { get; set; } = string.Empty;
    }
}
