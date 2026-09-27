namespace Conduit.ViewModel
{
    public class PlatformInstallDefinitions
    {
        public InstallDefinition? Default { get; set; }
        public InstallDefinition? Windows { get; set; }
        public InstallDefinition? Linux { get; set; }
        public InstallDefinition? MacOS { get; set; }
    }
}
