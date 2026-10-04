using System.Diagnostics;

namespace Conch.Services.Control
{
    /// <summary>
    /// Where a running Conch listens, and how the CLI finds it.
    /// </summary>
    /// <remarks>
    /// A Unix domain socket per running Conch, named after its process id, in a directory only
    /// the user can enter -- so only that user can connect, and nothing listens on the network.
    /// .NET has these on Windows 10 and later too, so both platforms share one transport.
    ///
    /// Inside Conch every terminal is told its socket (<see cref="SocketVariable"/>). Outside
    /// -- an ssh session into Conchix -- the CLI uses the one Conch the user has running, which
    /// is how a script there can still reach the console.
    /// </remarks>
    public static class ControlEndpoint
    {
        /// <summary>Set in every terminal Conch opens: the socket of the Conch it belongs to.</summary>
        public const string SocketVariable = "CONCH_SOCKET";

        /// <summary>Set in every terminal Conch opens: the id of the window it runs in.</summary>
        public const string WindowVariable = "CONCH_WINDOW";

        private const string Extension = ".sock";

        /// <summary>The directory sockets live in for this user.</summary>
        public static string Directory
        {
            get
            {
                if (OperatingSystem.IsWindows())
                {
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Conch", "run");
                }

                // The per-user runtime directory logind makes, where there is one; a private
                // directory under /tmp where there is not (WSL without systemd, for one).
                var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                return !string.IsNullOrEmpty(runtime) && System.IO.Directory.Exists(runtime)
                    ? Path.Combine(runtime, "conch")
                    : Path.Combine(Path.GetTempPath(), $"conch-{Environment.UserName}");
            }
        }

        /// <summary>The socket for the Conch with <paramref name="processId"/>.</summary>
        public static string PathFor(int processId, string? directory = null)
            => Path.Combine(directory ?? Directory, processId + Extension);

        /// <summary>Makes the directory, readable only by this user.</summary>
        public static void EnsureDirectory(string? directory = null)
        {
            directory ??= Directory;
            if (OperatingSystem.IsWindows())
            {
                // Under the user's own profile, which other users cannot read already.
                System.IO.Directory.CreateDirectory(directory);
            }
            else
            {
                System.IO.Directory.CreateDirectory(directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        /// <summary>
        /// The sockets of Conch sessions still running, removing any left by one that is not.
        /// </summary>
        /// <param name="isRunning">Whether a process id is alive; the real check outside tests.</param>
        public static IReadOnlyList<string> Running(string? directory = null, Func<int, bool>? isRunning = null)
        {
            directory ??= Directory;
            isRunning ??= IsProcessRunning;

            if (!System.IO.Directory.Exists(directory))
            {
                return [];
            }

            var alive = new List<string>();
            foreach (var path in System.IO.Directory.GetFiles(directory, "*" + Extension))
            {
                if (int.TryParse(Path.GetFileNameWithoutExtension(path), out var pid) && isRunning(pid))
                {
                    alive.Add(path);
                    continue;
                }

                // Left behind by a Conch that crashed or was killed.
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            return alive;
        }

        /// <summary>
        /// The socket a CLI call should use: the one given, the one this terminal belongs to, or
        /// the only one running. Null with a reason when there is no single answer.
        /// </summary>
        public static (string? Socket, string? Problem, int Code) Choose(
            string? given, string? fromEnvironment, IReadOnlyList<string> running)
        {
            if (!string.IsNullOrEmpty(given))
            {
                return (given, null, ControlExit.Ok);
            }

            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                return (fromEnvironment, null, ControlExit.Ok);
            }

            return running.Count switch
            {
                1 => (running[0], null, ControlExit.Ok),
                0 => (null, "Conch is not running.", ControlExit.NotRunning),
                _ => (null, "Several Conch sessions are running; say which with --socket.", ControlExit.Usage),
            };
        }

        private static bool IsProcessRunning(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
