using System.Diagnostics;
using System.Text;
using Conch.Utilities;

namespace Conch.Services
{
    /// <summary>What nmcli printed, and whether it worked.</summary>
    /// <param name="ExitCode">nmcli's own exit code; 0 is success.</param>
    /// <param name="Output">Standard output, which for a terse listing is the data.</param>
    /// <param name="Error">Standard error, which is where nmcli explains a refusal.</param>
    public sealed record NmcliResult(int ExitCode, string Output, string Error)
    {
        public bool Ok => ExitCode == 0;

        /// <summary>
        /// The one line worth showing when something failed.
        /// </summary>
        /// <remarks>
        /// nmcli's first line of stderr is already a sentence written for a person -- "Error:
        /// NetworkManager is not running.", "Error: Connection activation failed: Secrets were
        /// required, but not provided." -- so it is shown as it is rather than replaced with a
        /// message of our own that would say less. The exit code is the last resort, for the
        /// case where nmcli failed and said nothing at all.
        /// </remarks>
        public string Message
        {
            get
            {
                var line = FirstLine(Error) ?? FirstLine(Output);
                return line ?? $"nmcli exited with code {ExitCode}.";
            }
        }

        private static string? FirstLine(string text)
            => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);
    }

    /// <summary>A network interface as NetworkManager sees it.</summary>
    public sealed record NetworkDevice(string Name, string Type, string State, string Connection)
    {
        /// <summary>True for the wireless interfaces, which is what makes a Wi-Fi list worth showing.</summary>
        public bool IsWifi => string.Equals(Type, "wifi", StringComparison.OrdinalIgnoreCase);

        /// <summary>Which connection is on this device, or what it is doing instead.</summary>
        public string Detail => Connection.Length > 0 ? Connection : State;
    }

    /// <summary>A saved connection profile.</summary>
    /// <param name="Type">
    /// The raw setting name, as terse output gives it: <c>802-11-wireless</c>, not <c>wifi</c>.
    /// </param>
    /// <param name="Device">The interface it is currently on, empty when it is not active.</param>
    public sealed record SavedConnection(string Name, string Uuid, string Type, string Device)
    {
        /// <summary>
        /// Active precisely when NetworkManager has it on a device.
        /// </summary>
        /// <remarks>
        /// Which is what the DEVICE column means in <em>terse</em> output, and the reason that
        /// column is asked for. Non-terse output writes <c>--</c> there instead of leaving it
        /// empty, so this test quietly reverses if anyone drops the <c>-t</c>.
        /// </remarks>
        public bool IsActive => Device.Length > 0;

        /// <summary>
        /// True for a wireless profile, which is what lets an access point in range be matched
        /// to a profile that already exists for it.
        /// </summary>
        public bool IsWifi => Type == WifiType;

        public string DisplayName => IsActive ? Name + " *" : Name;

        public string Detail => IsActive ? $"{FriendlyType} on {Device}" : FriendlyType;

        /// <summary>
        /// The word for <see cref="Type"/> that a person would use.
        /// </summary>
        /// <remarks>
        /// Terse mode prints the D-Bus setting name and the ordinary listing prints a friendly
        /// one, for the same profiles in the same nmcli:
        ///
        ///   nmcli -t -f NAME,TYPE connection show    Cafe WiFi:802-11-wireless
        ///   nmcli    -f NAME,TYPE connection show    Cafe WiFi     wifi
        ///
        /// Measured on nmcli 1.46. Device listings are a third vocabulary again -- <c>device
        /// status</c> says <c>ethernet</c> in both modes -- so a type from one command must
        /// never be compared with a type from another.
        /// </remarks>
        public string FriendlyType => Type switch
        {
            WifiType => "wifi",
            "802-3-ethernet" => "ethernet",
            _ => Type,
        };

        /// <summary>How terse output spells a wireless profile.</summary>
        public const string WifiType = "802-11-wireless";
    }

    /// <summary>An access point in range.</summary>
    /// <param name="InUse">nmcli's <c>*</c> marker: this is the one we are on.</param>
    /// <param name="Signal">Strength as a percentage.</param>
    /// <param name="Security">The key management in use, empty for an open network.</param>
    public sealed record WifiNetwork(bool InUse, string Ssid, int Signal, string Security)
    {
        /// <summary>An open network, which is the case where joining asks for no passphrase.</summary>
        public bool IsOpen => Security.Length == 0;

        /// <summary>
        /// A hidden network broadcasts no name, so nmcli reports an empty SSID. Shown as such
        /// rather than as a blank row: it cannot be joined by name from here, and a row with
        /// nothing in it reads as a rendering fault.
        /// </summary>
        public bool IsHidden => Ssid.Length == 0;

        public string DisplayName => (IsHidden ? "(hidden)" : Ssid) + (InUse ? " *" : string.Empty);

        public string SignalText => $"{Signal}%";

        public string SecurityText => IsOpen ? "open" : Security;
    }

    /// <summary>
    /// Conch's window onto NetworkManager: builds nmcli invocations, runs them, and parses
    /// what comes back.
    /// </summary>
    /// <remarks>
    /// nmcli rather than D-Bus because nmcli is already on any machine that has NetworkManager,
    /// needs no binding to keep in step with the daemon, and has a machine-readable mode that
    /// has not changed shape in years. The cost is parsing, which is why every parser here is
    /// pure and tested against captured output.
    ///
    /// Two privilege levels, measured from the polkit policy NetworkManager ships rather than
    /// assumed -- these are the defaults in the file on the Conchix image:
    ///
    ///   network-control          allow_active=yes              activating and deactivating
    ///   wifi.scan                allow_active=yes              rescanning
    ///   settings.modify.own      allow_active=yes
    ///   settings.modify.system   allow_active=auth_admin_keep  creating a profile
    ///
    /// So bringing a saved connection up or down, and scanning, need no authentication at all
    /// and run quietly in the background here. Joining a Wi-Fi network for the first time
    /// creates a system profile, which does need an administrator -- and a text session has no
    /// polkit agent to ask. That one action therefore runs in a visible terminal window with
    /// sudo behind it; see <see cref="JoinWifiScript"/>.
    /// </remarks>
    public static class Nmcli
    {
        private const string LogCategory = "Network";

        /// <summary>The program, which is the same name everywhere NetworkManager exists.</summary>
        public const string Program = "nmcli";

        /// <summary>Listing is a local D-Bus round trip; it either answers at once or is stuck.</summary>
        private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);

        /// <summary>A scan is radio work, and a slow adapter takes its time over it.</summary>
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(45);

        /// <summary>
        /// How long nmcli itself waits for an activation, in seconds, passed as <c>-w</c>.
        /// </summary>
        /// <remarks>
        /// Bounded by nmcli rather than by killing the process: nmcli's own default is 90
        /// seconds, and a DHCP lease that is never coming should leave the shell with an error
        /// to show, not with a child it had to shoot. The process timeout below stays generously
        /// above this so that the answer always comes from nmcli.
        /// </remarks>
        private const int ActivationWaitSeconds = 25;

        private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(ActivationWaitSeconds + 15);

        /// <summary>Whether this machine has nmcli at all.</summary>
        /// <remarks>
        /// Read once. NetworkManager is Linux-only, so on Windows and macOS this is simply
        /// false and the built-in network provider reports itself unavailable rather than
        /// opening a window that could never list anything.
        /// </remarks>
        public static bool IsPresent { get; } = PathUtils.IsOnPath(Program);

        /// <summary>Arguments that list the interfaces.</summary>
        public static IReadOnlyList<string> DeviceArgs =>
            [.. Terse, "-f", "DEVICE,TYPE,STATE,CONNECTION", "device", "status"];

        /// <summary>Arguments that list the saved connection profiles.</summary>
        public static IReadOnlyList<string> ConnectionArgs =>
            [.. Terse, "-f", "NAME,UUID,TYPE,DEVICE", "connection", "show"];

        /// <summary>
        /// Arguments that list the access points in range.
        /// </summary>
        /// <param name="rescan">
        /// True to make the adapter go and look now. Left to nmcli's own judgement otherwise,
        /// which reuses a recent scan and so opens the window immediately.
        /// </param>
        public static IReadOnlyList<string> WifiArgs(bool rescan) =>
            [.. Terse, "-f", "IN-USE,SSID,SIGNAL,SECURITY", "device", "wifi", "list",
                "--rescan", rescan ? "yes" : "auto"];

        /// <summary>Arguments that activate a saved connection.</summary>
        public static IReadOnlyList<string> UpArgs(string uuid) =>
            ["-w", ActivationWaitSeconds.ToString(), "connection", "up", "uuid", uuid];

        /// <summary>Arguments that deactivate a saved connection.</summary>
        public static IReadOnlyList<string> DownArgs(string uuid) =>
            ["-w", ActivationWaitSeconds.ToString(), "connection", "down", "uuid", uuid];

        /// <summary>
        /// The shell command that joins a Wi-Fi network for the first time.
        /// </summary>
        /// <remarks>
        /// A command line rather than a process invocation, and a visible window rather than a
        /// background call, because of three facts that all point the same way:
        ///
        /// - Joining a network nmcli has no profile for writes a system connection, which polkit
        ///   guards with auth_admin_keep. A text session has no agent to answer that, so the
        ///   attempt fails with "Not authorized to control networking" and nothing to type into.
        /// - The account that runs Conch on Conchix is in the sudo group, so sudo is the way
        ///   through -- and sudo may ask for a password, which needs somewhere to be asked.
        /// - The passphrase itself is asked for by nmcli's own <c>--ask</c>, in that same window.
        ///   Conch never holds it, never puts it on a command line where it would be visible in
        ///   the process table, and never writes it to a log.
        ///
        /// Plainly first and through sudo only if that is refused, exactly as
        /// <see cref="SessionActions"/> does for powering the machine down: a desktop with a
        /// graphical polkit agent will have answered the first attempt already, and on that
        /// machine nobody should be asked for a password Conch did not need.
        /// </remarks>
        public static string JoinWifiScript(string ssid)
        {
            var join = $"{Program} --ask device wifi connect {Quote(ssid)}";
            return $"{join} || sudo {join}";
        }

        /// <summary>
        /// Wraps <paramref name="value"/> so a shell hands it on exactly as it stands.
        /// </summary>
        /// <remarks>
        /// Single quotes, because inside them a POSIX shell interprets nothing at all -- and the
        /// one character that ends them is spliced out, quoted and put back. This matters more
        /// than it looks: an SSID is named by whoever owns the access point, it reaches here
        /// from a radio scan, and people do call their networks things like <c>Bob's; rm -rf</c>.
        /// </remarks>
        public static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

        /// <summary>Reads the interface list.</summary>
        public static IReadOnlyList<NetworkDevice> ParseDevices(string output)
            => ParseLines(output, 4, f => new NetworkDevice(f[0], f[1], f[2], f[3]));

        /// <summary>Reads the saved connection list.</summary>
        public static IReadOnlyList<SavedConnection> ParseConnections(string output)
            => ParseLines(output, 4, f => new SavedConnection(f[0], f[1], f[2], f[3]));

        /// <summary>
        /// Reads the access point list.
        /// </summary>
        /// <remarks>
        /// Strongest first, which is the order the list is actually read in -- and the one the
        /// network you want is usually at the top of. The network already in use leads
        /// regardless, because "what am I on" is the first question the list answers.
        /// </remarks>
        public static IReadOnlyList<WifiNetwork> ParseWifi(string output)
        {
            var networks = ParseLines(output, 4, f => new WifiNetwork(
                InUse: f[0].Trim() == "*",
                Ssid: f[1],
                Signal: int.TryParse(f[2], out var signal) ? signal : 0,
                Security: f[3].Trim()));

            return [.. networks
                .OrderByDescending(n => n.InUse)
                .ThenByDescending(n => n.Signal)
                .ThenBy(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase)];
        }

        /// <summary>
        /// Splits one line of terse output into its fields.
        /// </summary>
        /// <remarks>
        /// Terse mode joins the fields with colons and escapes any colon or backslash inside a
        /// value with a backslash, which is the whole reason this is not a call to
        /// <c>string.Split</c>. Measured rather than assumed -- a connection named
        /// <c>weird:name with\back</c> comes back from nmcli 1.46 as:
        ///
        ///   weird\:name with\\back:8e2b62c0-fa7a-4c2a-8619-e101d6b8ce83:dummy:
        ///
        /// Splitting that naively yields six fields for a four-field record, so the UUID lands
        /// in the type column and the row is nonsense. Wi-Fi makes it likelier still: an SSID
        /// is free text chosen by a stranger.
        ///
        /// A trailing backslash is kept as itself rather than treated as the start of an escape
        /// that never arrives, because a truncated line should cost one odd character, not the
        /// whole row.
        /// </remarks>
        public static IReadOnlyList<string> SplitFields(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '\\' && i + 1 < line.Length)
                {
                    field.Append(line[++i]);
                }
                else if (c == ':')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }

            fields.Add(field.ToString());
            return fields;
        }

        /// <summary>
        /// Runs nmcli and hands back what it said.
        /// </summary>
        /// <remarks>
        /// Through <see cref="ProcessStartInfo.ArgumentList"/> with no shell anywhere in the
        /// path, so an SSID or a profile name cannot be read as syntax. The one place a shell is
        /// involved is <see cref="JoinWifiScript"/>, which needs one and quotes for it.
        ///
        /// Never throws: the caller is a window refreshing a list, and every failure here is
        /// something to show in the status line rather than an exception to handle.
        /// </remarks>
        public static async Task<NmcliResult> RunAsync(
            IReadOnlyList<string> args,
            CancellationToken cancellationToken = default)
        {
            var timeout = args.Contains("--rescan") ? ScanTimeout
                : args.Contains("connection") && (args.Contains("up") || args.Contains("down")) ? ActionTimeout
                : ListTimeout;

            var startInfo = new ProcessStartInfo
            {
                FileName = Program,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            // So the output does not change shape with the session language. nmcli translates
            // the sentences it writes to stderr, and the parsers below are matched to what the
            // C locale prints.
            startInfo.Environment["LC_ALL"] = "C";

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return new NmcliResult(-1, string.Empty, "Could not start nmcli.");
                }

                var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(timeout);

                try
                {
                    await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    Log.Warning(LogCategory, $"nmcli {string.Join(' ', args)} timed out after {timeout.TotalSeconds:0}s.");
                    return new NmcliResult(-1, string.Empty,
                        $"nmcli did not answer within {timeout.TotalSeconds:0} seconds.");
                }

                return new NmcliResult(
                    process.ExitCode,
                    await stdout.ConfigureAwait(false),
                    await stderr.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                // The window closed while a refresh was in flight. Nothing to report.
                return new NmcliResult(-1, string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"Could not run nmcli: {ex.Message}");
                return new NmcliResult(-1, string.Empty, ex.Message);
            }
        }

        /// <summary>
        /// Terse, and explicitly escaped.
        /// </summary>
        /// <remarks>
        /// <c>--escape yes</c> is nmcli's default, and is still said out loud: it is the thing
        /// <see cref="SplitFields"/> depends on to tell a separator from a colon in a name, and
        /// a parser whose contract is only implied is one nobody knows they have broken.
        /// </remarks>
        private static string[] Terse => ["-t", "--escape", "yes"];

        private static IReadOnlyList<T> ParseLines<T>(string output, int fields, Func<IReadOnlyList<string>, T> build)
        {
            var rows = new List<T>();

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var values = SplitFields(trimmed);

                // A short line is a line from some other version of nmcli, or a warning that
                // reached stdout. Skipping it loses one row; letting it through would index off
                // the end and lose the window.
                if (values.Count < fields)
                {
                    Log.Info(LogCategory, $"Ignoring unexpected nmcli output: {trimmed}");
                    continue;
                }

                rows.Add(build(values));
            }

            return rows;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort; the caller already has a timeout to report.
            }
        }
    }
}
