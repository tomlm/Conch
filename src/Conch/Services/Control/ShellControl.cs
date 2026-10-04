namespace Conch.Services.Control
{
    /// <summary>
    /// The running shell's control endpoint, as everything that opens a window needs to know it.
    /// </summary>
    /// <remarks>
    /// Static because windows are opened from several places -- the bar, Software's installs,
    /// role apps -- each with an AppLauncher of its own, and every one of them has to tell its
    /// terminal how to reach this Conch and which window it is.
    /// </remarks>
    public static class ShellControl
    {
        /// <summary>The ids scripts name windows by.</summary>
        public static WindowIds Ids { get; } = new();

        /// <summary>The socket this Conch listens on, once it does.</summary>
        public static string? SocketPath { get; set; }

        /// <summary>
        /// What to add to the environment of a terminal in <paramref name="window"/>, or null
        /// when there is no socket to point it at.
        /// </summary>
        public static IDictionary<string, string>? EnvironmentFor(object window)
            => SocketPath == null
                ? null
                : new Dictionary<string, string>
                {
                    [ControlEndpoint.SocketVariable] = SocketPath,
                    [ControlEndpoint.WindowVariable] = Ids.IdOf(window),
                };
    }
}
