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
        /// <remarks>
        /// On Windows the variables are also named in <c>WSLENV</c>, which is what carries them
        /// into a WSL shell started in that window -- the socket path translated -- and back out
        /// to <c>conch.exe</c> run from it. A Linux process cannot reach a Windows socket across
        /// the WSL VM itself, so from WSL it is conch.exe that makes the call.
        /// </remarks>
        public static IDictionary<string, string>? EnvironmentFor(object window)
        {
            if (SocketPath == null)
            {
                return null;
            }

            var environment = new Dictionary<string, string>
            {
                [ControlEndpoint.SocketVariable] = SocketPath,
                [ControlEndpoint.WindowVariable] = Ids.IdOf(window),
            };

            if (OperatingSystem.IsWindows())
            {
                environment["WSLENV"] = WithWslEnv(Environment.GetEnvironmentVariable("WSLENV"));
            }

            return environment;
        }

        /// <summary>
        /// <paramref name="existing"/> WSLENV with Conch's variables added, keeping whatever was
        /// there: the socket as a path (<c>/p</c>), the window id as it is.
        /// </summary>
        public static string WithWslEnv(string? existing)
        {
            var entries = (existing ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var entry in new[] { ControlEndpoint.SocketVariable + "/p", ControlEndpoint.WindowVariable })
            {
                var name = entry.Split('/')[0];
                if (!entries.Any(e => e.Split('/')[0] == name))
                {
                    entries.Add(entry);
                }
            }

            return string.Join(':', entries);
        }
    }
}
