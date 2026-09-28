namespace Conch.Utilities
{
    /// <summary>
    /// The operating system a command is being resolved for.
    /// </summary>
    public enum HostOs
    {
        Other,
        Windows,
        Linux,
        MacOS
    }

    /// <summary>
    /// Identifies the machine Conch is running on.
    /// </summary>
    /// <remarks>
    /// Platform choices are threaded through as a <see cref="HostOs"/> rather than read from
    /// <see cref="OperatingSystem"/> at each decision point, so that the install, uninstall and
    /// launch resolution can be tested for every platform from any platform. Conch picks a
    /// different command per OS and those branches are exactly the ones worth covering.
    /// </remarks>
    public static class Host
    {
        public static HostOs Current { get; } =
            OperatingSystem.IsWindows() ? HostOs.Windows
            : OperatingSystem.IsLinux() ? HostOs.Linux
            : OperatingSystem.IsMacOS() ? HostOs.MacOS
            : HostOs.Other;
    }
}
