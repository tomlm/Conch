using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Conch.Services;
using Conch.Utilities;

namespace Conch.ViewModel
{
    /// <summary>
    /// Backs the Network window: what this machine has, what it is on, and what is in range.
    /// </summary>
    /// <remarks>
    /// Everything nmcli-shaped is injected -- how to run it, and how to open a terminal window --
    /// so the whole of this class can be exercised against captured nmcli output with no
    /// NetworkManager, no processes and no Avalonia in the way. That matters here more than
    /// usual: the interesting behaviour is the decisions, and the decisions are about privilege.
    /// </remarks>
    public partial class NetworkViewModel : ObservableObject
    {
        private const string LogCategory = "Network";

        private readonly Func<IReadOnlyList<string>, CancellationToken, Task<NmcliResult>> _run;
        private readonly Action<string, string, Action<int>>? _runInTerminal;

        /// <param name="run">
        /// Runs nmcli. Defaults to actually running it.
        /// </param>
        /// <param name="runInTerminal">
        /// Opens a visible terminal window on a shell command, given a title, the command, and
        /// what to do with its exit code once it finishes. Used for the actions that may have to
        /// ask the person something -- joining a new Wi-Fi network, and anything polkit refused
        /// -- and supplied by the shell, so this class needs no launcher. The callback is
        /// expected on the UI thread.
        /// </param>
        public NetworkViewModel(
            Func<IReadOnlyList<string>, CancellationToken, Task<NmcliResult>>? run = null,
            Action<string, string, Action<int>>? runInTerminal = null)
        {
            _run = run ?? Nmcli.RunAsync;
            _runInTerminal = runInTerminal;
        }

        /// <summary>Access points in range, strongest first.</summary>
        public ObservableCollection<WifiNetwork> Wifi { get; } = new();

        /// <summary>Saved connection profiles.</summary>
        public ObservableCollection<SavedConnection> Connections { get; } = new();

        /// <summary>Network interfaces.</summary>
        public ObservableCollection<NetworkDevice> Devices { get; } = new();

        [ObservableProperty]
        private WifiNetwork? _selectedWifi;

        [ObservableProperty]
        private SavedConnection? _selectedConnection;

        /// <summary>
        /// The last thing that happened, in words. Blank when there is nothing to say.
        /// </summary>
        /// <remarks>
        /// One line for both outcomes on purpose. Network operations fail for ordinary reasons --
        /// a wrong passphrase, a lease that never arrives, a daemon that is not running -- and
        /// nmcli already explains each of them in a sentence. Showing that sentence where the
        /// result would have gone is more use than a dialog to dismiss.
        /// </remarks>
        [ObservableProperty]
        private string _status = string.Empty;

        /// <summary>True while an nmcli call is in flight.</summary>
        [ObservableProperty]
        private bool _busy;

        /// <summary>
        /// Whether this machine has a wireless interface at all.
        /// </summary>
        /// <remarks>
        /// Asked of the device list rather than of the Wi-Fi list, because nmcli cannot tell
        /// those apart: on a machine with no wireless hardware, <c>device wifi list</c> exits 0
        /// and prints nothing -- exactly what it prints when the radio is on and nothing is in
        /// range. Measured on nmcli 1.46. Without this the window would say "no networks found"
        /// to someone who has no Wi-Fi, and leave them looking for the fault.
        /// </remarks>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WifiPlaceholder))]
        private bool _hasWifiDevice;

        /// <summary>
        /// True while the Wi-Fi list has nothing in it, so the window can say why instead of
        /// showing an empty box.
        /// </summary>
        [ObservableProperty]
        private bool _wifiEmpty = true;

        /// <summary>The same, for the saved connections.</summary>
        [ObservableProperty]
        private bool _connectionsEmpty = true;

        /// <summary>Why the Wi-Fi list is empty, when it is.</summary>
        public string WifiPlaceholder => HasWifiDevice
            ? "No networks in range."
            : "This machine has no wireless interface.";

        /// <summary>
        /// Reads the current state of everything.
        /// </summary>
        /// <param name="rescan">
        /// True to make the adapter scan now, which is what the RESCAN button asks for and what
        /// opening the window does not: a scan takes seconds, and the cached list is right often
        /// enough to be worth showing immediately.
        /// </param>
        public async Task RefreshAsync(bool rescan = false, CancellationToken cancellationToken = default)
        {
            if (Busy)
            {
                return;
            }

            Busy = true;
            Status = string.Empty;

            try
            {
                // Devices first, and a hard stop if that fails. Every other listing would fail
                // the same way for the same reason -- NetworkManager not running is the common
                // one -- and three copies of one sentence is not three times as informative.
                var devices = await _run(Nmcli.DeviceArgs, cancellationToken).ConfigureAwait(true);
                if (!devices.Ok)
                {
                    Devices.Clear();
                    Connections.Clear();
                    Wifi.Clear();
                    HasWifiDevice = false;
                    Status = devices.Message;
                    return;
                }

                Replace(Devices, Nmcli.ParseDevices(devices.Output));
                HasWifiDevice = Devices.Any(d => d.IsWifi);

                var connections = await _run(Nmcli.ConnectionArgs, cancellationToken).ConfigureAwait(true);
                if (connections.Ok)
                {
                    var uuid = SelectedConnection?.Uuid;
                    Replace(Connections, Nmcli.ParseConnections(connections.Output));
                    SelectedConnection = Connections.FirstOrDefault(c => c.Uuid == uuid)
                        ?? Connections.FirstOrDefault();
                }
                else
                {
                    Status = connections.Message;
                }

                if (!HasWifiDevice)
                {
                    Wifi.Clear();
                    return;
                }

                var wifi = await _run(Nmcli.WifiArgs(rescan), cancellationToken).ConfigureAwait(true);
                if (wifi.Ok)
                {
                    // Kept across a refresh by name: the list is re-sorted by signal every time,
                    // so an index would move the selection under the person's hands between one
                    // scan and the next.
                    var ssid = SelectedWifi?.Ssid;
                    Replace(Wifi, Nmcli.ParseWifi(wifi.Output));
                    SelectedWifi = Wifi.FirstOrDefault(n => n.Ssid == ssid) ?? Wifi.FirstOrDefault();
                }
                else
                {
                    Status = wifi.Message;
                }
            }
            finally
            {
                // Recomputed on the way out of every path, the failures included, so an empty
                // list always leaves with its explanation next to it.
                WifiEmpty = Wifi.Count == 0;
                ConnectionsEmpty = Connections.Count == 0;
                Busy = false;
            }
        }

        /// <summary>
        /// Brings a saved connection up.
        /// </summary>
        /// <remarks>
        /// Quietly, in the background, because at the console it needs no authentication:
        /// NetworkManager's polkit policy allows network-control to any active local session, so
        /// this is the same privilege as clicking a network in a desktop applet. A session with
        /// no seat -- SSH, WSL -- is refused, and the action is retried under sudo in a terminal.
        /// </remarks>
        public Task ActivateAsync(SavedConnection? connection, CancellationToken cancellationToken = default)
            => RunActionAsync(connection, Nmcli.UpArgs, "Connecting to", "is up.", cancellationToken);

        /// <summary>Takes a saved connection down.</summary>
        public Task DeactivateAsync(SavedConnection? connection, CancellationToken cancellationToken = default)
            => RunActionAsync(connection, Nmcli.DownArgs, "Disconnecting", "is down.", cancellationToken);

        /// <summary>
        /// Joins the selected Wi-Fi network.
        /// </summary>
        /// <remarks>
        /// Two different actions wear one button here, and which one runs is the point of this
        /// method:
        ///
        /// - A network there is already a profile for is just an activation, which needs no
        ///   authentication and no window. Coming back to a network you have used before should
        ///   not ask for anything, and this is what makes that true.
        /// - A network with no profile has to have one created, which polkit guards with
        ///   auth_admin_keep, which a text session has no agent to answer. So it goes to a
        ///   terminal window where sudo can ask for a password and nmcli can ask for the
        ///   passphrase itself.
        ///
        /// A hidden network cannot be joined from a list that does not know its name; saying so
        /// is better than a join that fails with nothing to look at.
        /// </remarks>
        public async Task JoinAsync(WifiNetwork? network, CancellationToken cancellationToken = default)
        {
            if (network == null)
            {
                return;
            }

            if (network.IsHidden)
            {
                Status = "That network does not broadcast its name, so it cannot be joined from here.";
                return;
            }

            var saved = SavedProfileFor(network);
            if (saved != null)
            {
                await ActivateAsync(saved, cancellationToken).ConfigureAwait(true);
                return;
            }

            if (_runInTerminal == null)
            {
                Status = "Joining a new network needs a terminal window, and this window has none.";
                return;
            }

            var script = Nmcli.JoinWifiScript(network.Ssid);
            Log.Info(LogCategory, $"Joining {network.Ssid} in a terminal: {script}");
            _runInTerminal($"Join {network.Ssid}", script, exitCode => _ = AfterTerminalAsync(
                exitCode, $"Joined {network.Ssid}.", $"Joining {network.Ssid}", cancellationToken));

            Status = $"Joining {network.Ssid} in a terminal window.";
        }

        /// <summary>
        /// The saved profile for an access point, if there is one.
        /// </summary>
        /// <remarks>
        /// Matched on name, because a profile nmcli created for a network is named after its
        /// SSID. A profile someone renamed by hand will not be found and the join falls through
        /// to the terminal path, where nmcli itself reuses the existing profile -- a slower
        /// route to the same place, rather than a wrong one.
        /// </remarks>
        public SavedConnection? SavedProfileFor(WifiNetwork network)
            => Connections.FirstOrDefault(c =>
                c.IsWifi && string.Equals(c.Name, network.Ssid, StringComparison.Ordinal));

        private async Task RunActionAsync(
            SavedConnection? connection,
            Func<string, IReadOnlyList<string>> args,
            string startedVerb,
            string finishedVerb,
            CancellationToken cancellationToken)
        {
            if (connection == null || Busy)
            {
                return;
            }

            Busy = true;
            Status = $"{startedVerb} {connection.Name}...";

            try
            {
                var result = await _run(args(connection.Uuid), cancellationToken).ConfigureAwait(true);
                Status = result.Ok ? $"{connection.Name} {finishedVerb}" : result.Message;

                // Refused for who asked rather than what was asked: a session polkit does not
                // count as local. sudo can answer that, and a terminal is where it can ask.
                if (result.IsNotAuthorized && _runInTerminal != null)
                {
                    var script = Nmcli.SudoScript(args(connection.Uuid));
                    Log.Info(LogCategory, $"{startedVerb} {connection.Name} was refused; retrying in a terminal: {script}");
                    _runInTerminal($"{startedVerb} {connection.Name}", script, exitCode => _ = AfterTerminalAsync(
                        exitCode, $"{connection.Name} {finishedVerb}", $"{startedVerb} {connection.Name}", cancellationToken));

                    Status = "NetworkManager wants an administrator for that here, so it continues in a terminal window.";
                }
            }
            finally
            {
                Busy = false;
            }

            // The lists are now wrong in the way that matters most -- which profile is on which
            // device -- so they are re-read rather than patched from what we think happened.
            var message = Status;
            await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(true);

            // A refresh clears the status, and the outcome of the action is the more useful of
            // the two things that could be on that line.
            if (Status.Length == 0)
            {
                Status = message;
            }
        }

        /// <summary>
        /// Re-reads everything once a terminal window has finished, and says how it went.
        /// </summary>
        /// <remarks>
        /// Without this the window would go on showing the state from before the terminal opened,
        /// and the person would have to know to ask for it again.
        /// </remarks>
        private async Task AfterTerminalAsync(
            int exitCode, string succeeded, string attempted, CancellationToken cancellationToken)
        {
            // The window closed while the terminal was open; nothing here is on screen any more.
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(true);

            if (Status.Length == 0)
            {
                Status = exitCode == 0 ? succeeded : $"{attempted} did not finish (exit code {exitCode}).";
            }
        }

        private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }
    }
}
