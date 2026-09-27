namespace Conch.ViewModel
{
    /// <summary>
    /// Which platform's entry a registration resolved to.
    /// </summary>
    public enum ToolPlatform
    {
        None,
        Default,
        Windows,
        Linux,
        MacOS
    }

    public class PlatformInstallDefinitions
    {
        public InstallDefinition? Default { get; set; }
        public InstallDefinition? Windows { get; set; }
        public InstallDefinition? Linux { get; set; }
        public InstallDefinition? MacOS { get; set; }
    }
}
