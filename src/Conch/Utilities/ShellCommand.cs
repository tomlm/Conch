namespace Conch.Utilities
{
    /// <summary>
    /// A resolved process invocation: what to execute, and with which arguments.
    /// </summary>
    public sealed record ResolvedCommand(string Process, List<string> Args)
    {
        public override string ToString()
            => Args.Count == 0
                ? Process
                : $"{Process} {string.Join(' ', Args.Select(Quote))}";

        // Arguments reach the process through ArgumentList, so this is only for logs; quote the
        // ones containing spaces so a logged command reads the way it actually ran.
        private static string Quote(string arg)
            => arg.Length == 0 || arg.Any(char.IsWhiteSpace) ? $"\"{arg}\"" : arg;
    }

    /// <summary>
    /// Turns the command lines held in app registrations into concrete process invocations for
    /// the current platform.
    /// </summary>
    public static class ShellCommand
    {
        /// <summary>
        /// Runs <paramref name="command"/> with <paramref name="args"/> directly.
        /// </summary>
        /// <param name="viaWsl">
        /// Run the command inside WSL. Used on Windows for registrations that only describe a
        /// Linux build, matching how a bare command line already falls back to WSL.
        /// </param>
        public static ResolvedCommand ForProcess(string command, IEnumerable<string> args, bool viaWsl = false)
            => ForProcess(command, args, viaWsl, Host.Current);

        /// <inheritdoc cref="ForProcess(string, IEnumerable{string}, bool)"/>
        public static ResolvedCommand ForProcess(string command, IEnumerable<string> args, bool viaWsl, HostOs os)
        {
            var argList = args.ToList();

            if (viaWsl && os == HostOs.Windows)
            {
                var wslArgs = new List<string> { "--", command };
                wslArgs.AddRange(argList);
                return new ResolvedCommand("wsl", wslArgs);
            }

            return new ResolvedCommand(command, argList);
        }

        /// <summary>
        /// Runs <paramref name="script"/> through a shell.
        /// </summary>
        /// <remarks>
        /// Install and uninstall lines routinely chain with <c>&amp;&amp;</c> and rely on shell
        /// quoting, so they cannot be executed as a bare process.
        /// </remarks>
        public static ResolvedCommand ForScript(string script, bool viaWsl = false)
            => ForScript(script, viaWsl, Host.Current);

        /// <inheritdoc cref="ForScript(string, bool)"/>
        public static ResolvedCommand ForScript(string script, bool viaWsl, HostOs os)
        {
            if (viaWsl && os == HostOs.Windows)
            {
                // -l so the login profile is read and the usual package tooling is on PATH.
                return new ResolvedCommand("wsl", new List<string> { "--", "bash", "-lc", script });
            }

            if (os == HostOs.Windows)
            {
                return new ResolvedCommand("cmd.exe", new List<string> { "/c", script });
            }

            return new ResolvedCommand(UnixShell, new List<string> { "-lc", script });
        }

        /// <summary>
        /// Resolves a command line typed by the user.
        /// </summary>
        public static ResolvedCommand ForCommandLine(string commandLine)
        {
            var parts = ArgumentTemplate.Tokenize(commandLine.Trim());
            if (parts.Count == 0)
            {
                return new ResolvedCommand(DefaultShell, new List<string>());
            }

            var rest = parts.GetRange(1, parts.Count - 1);
            var resolvedPath = PathUtils.ResolveOnPath(parts[0]);

            if (!OperatingSystem.IsWindows())
            {
                // Anything not on PATH may still be a shell builtin, alias or pipeline.
                return resolvedPath != null
                    ? new ResolvedCommand(resolvedPath, rest)
                    : ForScript(commandLine.Trim());
            }

            if (resolvedPath == null)
            {
                // Not a Windows executable; assume it names a Linux tool.
                return new ResolvedCommand("wsl", parts);
            }

            var extension = Path.GetExtension(resolvedPath);
            if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedCommand(resolvedPath, rest);
            }

            if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
            {
                var args = new List<string> { "/c", resolvedPath };
                args.AddRange(rest);
                return new ResolvedCommand("cmd.exe", args);
            }

            return new ResolvedCommand("wsl", parts);
        }

        /// <summary>
        /// The interactive shell to open for a plain terminal window.
        /// </summary>
        public static string DefaultShell => DefaultShellFor(Host.Current);

        /// <inheritdoc cref="DefaultShell"/>
        public static string DefaultShellFor(HostOs os) => os == HostOs.Windows ? "cmd.exe" : UnixShell;

        private static string UnixShell =>
            File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
    }
}
