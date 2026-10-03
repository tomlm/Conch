namespace Conch.Utilities
{
    /// <summary>
    /// Keeps Conch's PATH in step with what a fresh login would have.
    /// </summary>
    /// <remarks>
    /// Everything Conch starts -- apps, terminals, detection probes -- inherits its PATH, and
    /// that PATH goes stale in two ways:
    ///
    /// - Where Conch IS the login shell, as on Conchix, nothing reads /etc/profile,
    ///   /etc/profile.d or ~/.profile at all: login execs Conch, and Conch is not bash. So
    ///   ~/.dotnet/tools (added by /etc/profile.d/dotnet.sh), ~/.local/bin (added by
    ///   ~/.profile, where pipx and pip put per-user commands) and anything else a package
    ///   adds that way were never on it. Found when Edit.NET installed cleanly, `dotnet tool
    ///   list -g` showed it, and neither Conch nor a terminal inside Conch could find edit.net.
    /// - An install can add to PATH while Conch runs: the .NET SDK brings dotnet.sh, winget
    ///   adds a folder to the user's PATH in the registry. A running process sees neither.
    ///
    /// So PATH is re-read from the source a login would use -- a login bash on Linux and
    /// macOS, the registry on Windows -- at startup, after every install or uninstall, and on
    /// a recheck. Only PATH: it is what decides whether a command is found, and copying the
    /// whole login environment would bring session-specific variables along with it.
    /// </remarks>
    public static class LoginEnvironment
    {
        private const string LogCategory = "Environment";

        /// <summary>
        /// Set on the login shell this runs, so a Conch that ~/.profile starts can tell it is
        /// only being asked for a PATH, and leave at once instead of starting a second shell.
        /// </summary>
        public const string ProbeVariable = "CONCH_LOGIN_PATH_PROBE";

        // The login shell's own output is not ours to parse: a .profile may print a greeting,
        // a fortune, a motd. The PATH is read from between these markers and nothing else.
        private const string StartMarker = "__CONCH_PATH_BEGIN__";
        private const string EndMarker = "__CONCH_PATH_END__";

        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Re-reads PATH and adopts it. Never throws: a PATH that cannot be read leaves the
        /// current one in place, which is no worse than before.
        /// </summary>
        public static async Task RefreshPathAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var login = Host.Current switch
                {
                    HostOs.Windows => FromRegistry(),
                    HostOs.Linux or HostOs.MacOS => await FromLoginShellAsync(cancellationToken).ConfigureAwait(false),
                    _ => null,
                };

                if (string.IsNullOrWhiteSpace(login))
                {
                    return;
                }

                var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                var merged = Merge(login, current, Path.PathSeparator, Host.Current == HostOs.Windows);
                if (merged != current)
                {
                    Environment.SetEnvironmentVariable("PATH", merged);
                    Log.Info(LogCategory, $"PATH updated from the login environment: {merged}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"Could not refresh PATH: {ex.Message}");
            }
        }

        /// <summary>
        /// The login PATH, followed by whatever the current one has that the login one lacks.
        /// </summary>
        /// <remarks>
        /// Login first, because that is the order a fresh session would search in. The current
        /// entries are kept rather than dropped, because Conch may have been started with a
        /// PATH of someone's choosing -- from a terminal, by a test -- and losing that would
        /// make commands that worked a moment ago disappear. Duplicates go, empty entries go.
        /// </remarks>
        public static string Merge(string login, string current, char separator, bool ignoreCase)
        {
            var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var seen = new HashSet<string>(comparer);
            var entries = new List<string>();

            foreach (var entry in login.Split(separator).Concat(current.Split(separator)))
            {
                var trimmed = entry.Trim();
                if (trimmed.Length > 0 && seen.Add(Key(trimmed)))
                {
                    entries.Add(trimmed);
                }
            }

            return string.Join(separator, entries);
        }

        // "/usr/bin" and "/usr/bin/" are the same directory; "/" is still "/".
        private static string Key(string entry)
        {
            var key = entry.TrimEnd('/', '\\');
            return key.Length > 0 ? key : entry;
        }

        /// <summary>The PATH between the markers in a login shell's output, or null.</summary>
        public static string? ParseProbeOutput(string output)
        {
            var start = output.LastIndexOf(StartMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += StartMarker.Length;
            var end = output.IndexOf(EndMarker, start, StringComparison.Ordinal);
            return end < 0 ? null : output[start..end];
        }

        private static async Task<string?> FromLoginShellAsync(CancellationToken cancellationToken)
        {
            var shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
            var startInfo = BackgroundProcess.StartInfo(shell,
                ["-lc", $"printf '%s%s%s' '{StartMarker}' \"$PATH\" '{EndMarker}'"]);
            startInfo.Environment[ProbeVariable] = "1";

            using var process = BackgroundProcess.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(ProbeTimeout);

            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                Log.Warning(LogCategory, $"The login shell did not report a PATH within {ProbeTimeout.TotalSeconds:0}s.");
                return null;
            }

            return ParseProbeOutput(await output.ConfigureAwait(false));
        }

        /// <summary>
        /// Machine then user PATH from the registry, as a new process would get it.
        /// </summary>
        /// <remarks>
        /// Both are REG_EXPAND_SZ values that may hold %SystemRoot% and the like, so they are
        /// expanded here; the process's own PATH arrived already expanded.
        /// </remarks>
        private static string? FromRegistry()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var machine = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? string.Empty;
            var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
            var combined = string.Join(';', new[] { machine, user }.Where(p => p.Length > 0));
            return combined.Length == 0 ? null : Environment.ExpandEnvironmentVariables(combined);
        }
    }
}
