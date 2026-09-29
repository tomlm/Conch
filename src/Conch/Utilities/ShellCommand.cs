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
        //
        // Inner quotes are escaped rather than left as they are. An argument that itself
        // contains a quote -- the 'exec "$0" "$@"' the WSL launcher passes, for one -- rendered
        // as a command nobody could paste back and that read as though the quoting were broken,
        // during the very debugging session where the log is what you have to go on.
        private static string Quote(string arg)
            => arg.Length == 0 || arg.Any(char.IsWhiteSpace) || arg.Contains('"')
                ? $"\"{arg.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
                : arg;
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
                // --exec, not --. They are not two spellings of the same thing: with --,
                // wsl.exe joins the arguments into a command line and hands it to a shell,
                // quoting only the ones that contain whitespace. Everything else is read as
                // shell syntax. Measured against the real wsl.exe, passing argv exactly:
                //
                //   --      /usr/bin/printf FMT hello    bash: %s: No such file or directory
                //   --      /usr/bin/printf FMT 'a;b'    ran 'b' as a separate command
                //   --exec  /usr/bin/printf FMT 'a;b'    <a;b>
                //
                // where FMT is the format string <%s> followed by an escaped newline.
                // The '<' and '>' became redirections and the ';' a command separator,
                // because none of those arguments contained a space to get them quoted. So
                // -- cannot carry an argument through intact, and a registration argument
                // holding shell syntax was being executed as shell syntax. --exec runs the
                // command directly with no shell in the way.
                //
                // Through a login shell, so the command is looked up on the same PATH that
                // detection used. bash -lc reads the login profile, which is where
                // /snap/bin, ~/.local/bin, ~/.cargo/bin and ~/.dotnet/tools are added.
                // Without it an app installed by snap is reported present and then fails
                // to launch, which looks like a broken app rather than a missing path.
                //
                // 'exec "$0" "$@"' takes the command and its arguments as positional
                // parameters rather than interpolating them into the script, so nothing
                // needs shell-quoting and an argument containing spaces or quotes cannot
                // change what runs. Under -- the quotes in it were eaten before bash saw
                // them and every launch died with 'bash: line 1: --: command not found'.
                var wslArgs = new List<string> { "--exec", "bash", "-lc", "exec \"$0\" \"$@\"", command };
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
                // -l so the login profile is read and the usual package tooling is on PATH,
                // and --exec so the script reaches bash as one argument. Under -- it was
                // joined into a command line and reinterpreted by a shell first, which left
                // any token holding shell syntax but no space -- a URL with a '&' in it, a
                // redirect, a ';' -- acting on the outer shell instead of the inner one.
                return new ResolvedCommand("wsl", new List<string> { "--exec", "bash", "-lc", script });
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
                // Not a Windows executable; assume it names a Linux tool. Run it as the shell
                // command it was typed as -- someone entering a command line expects a pipe or
                // a redirect in it to mean what it says, and Tokenize has already taken the
                // quotes off, so re-running the tokens is not the same string any more.
                return ForScript(commandLine.Trim(), viaWsl: true, HostOs.Windows);
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

            return ForScript(commandLine.Trim(), viaWsl: true, HostOs.Windows);
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
