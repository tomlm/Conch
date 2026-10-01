using Conch.Services;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// What the Network window decides, with nmcli replaced by a script of canned answers.
/// </summary>
/// <remarks>
/// The decisions are all about privilege, and they are the reason this class is worth testing
/// without a network: coming back to a known Wi-Fi network must cost nothing, joining a new one
/// has to go somewhere a password can be typed, and neither must depend on a live NetworkManager
/// to get right.
/// </remarks>
public class NetworkViewModelTests
{
    private const string WifiDevice = "wlan0:wifi:connected:Cafe\\: Free";
    private const string WiredOnly = "eth0:ethernet:connected:Wired\nlo:loopback:unmanaged:";

    private const string SavedProfiles =
        "Cafe\\: Free:11111111-1111-4111-8111-111111111111:802-11-wireless:wlan0\n"
        + "Wired:22222222-2222-4222-8222-222222222222:802-3-ethernet:eth0";

    private const string InRange =
        "*:Cafe\\: Free:72:WPA2\n"
        + ":New Place:55:WPA2\n"
        + ":Open Guest:31:\n"
        + "::20:WPA2";

    /// <summary>
    /// Answers nmcli calls from a script, and records what was asked.
    /// </summary>
    private sealed class FakeNmcli
    {
        private readonly Dictionary<string, NmcliResult> _answers = new();

        public List<string> Calls { get; } = new();

        public List<(string Title, string Script)> Terminals { get; } = new();

        /// <param name="key">A token the call is recognised by: "status", "show", "list", "up", "down".</param>
        public FakeNmcli Answer(string key, string output, int exitCode = 0, string error = "")
        {
            _answers[key] = new NmcliResult(exitCode, output, error);
            return this;
        }

        public Task<NmcliResult> Run(IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            var line = string.Join(' ', args);
            Calls.Add(line);

            foreach (var (key, answer) in _answers)
            {
                if (args.Contains(key))
                {
                    return Task.FromResult(answer);
                }
            }

            return Task.FromResult(new NmcliResult(0, string.Empty, string.Empty));
        }

        /// <summary>What each terminal window will be told when it finishes, in opening order.</summary>
        public List<Action<int>> TerminalExits { get; } = new();

        public void RunInTerminal(string title, string script, Action<int> onExit)
        {
            Terminals.Add((title, script));
            TerminalExits.Add(onExit);
        }

        public int CallsContaining(string text) => Calls.Count(c => c.Contains(text));
    }

    private static NetworkViewModel Model(FakeNmcli nmcli)
        => new(nmcli.Run, nmcli.RunInTerminal);

    private static FakeNmcli FullyStocked() => new FakeNmcli()
        .Answer("status", WifiDevice)
        .Answer("show", SavedProfiles)
        .Answer("list", InRange);

    [Fact]
    public async Task ARefreshReadsTheInterfacesTheProfilesAndTheAir()
    {
        var nmcli = FullyStocked();
        var model = Model(nmcli);

        await model.RefreshAsync();

        Assert.Single(model.Devices);
        Assert.Equal(2, model.Connections.Count);
        Assert.Equal(4, model.Wifi.Count);
        Assert.True(model.HasWifiDevice);
        Assert.Equal(string.Empty, model.Status);
    }

    [Fact]
    public async Task AMachineWithNoWirelessSaysSoInsteadOfLookingEmpty()
    {
        // nmcli exits 0 and prints nothing for 'device wifi list' whether the radio found
        // nothing or there is no radio. Only the device list can tell those apart, so the Wi-Fi
        // list is not even asked for here.
        var nmcli = new FakeNmcli().Answer("status", WiredOnly).Answer("show", SavedProfiles);
        var model = Model(nmcli);

        await model.RefreshAsync();

        Assert.False(model.HasWifiDevice);
        Assert.Empty(model.Wifi);
        Assert.Equal("This machine has no wireless interface.", model.WifiPlaceholder);
        Assert.Equal(0, nmcli.CallsContaining("wifi list"));
    }

    [Fact]
    public async Task AMachineWithWirelessAndNothingInRangeSaysThatInstead()
    {
        var nmcli = new FakeNmcli()
            .Answer("status", WifiDevice)
            .Answer("show", SavedProfiles)
            .Answer("list", string.Empty);
        var model = Model(nmcli);

        await model.RefreshAsync();

        Assert.True(model.WifiEmpty);
        Assert.Equal("No networks in range.", model.WifiPlaceholder);
    }

    [Fact]
    public async Task WhenNetworkManagerIsNotRunningItSaysSoOnceAndStops()
    {
        // The listing that fails first explains everything that would follow, so the other two
        // are not attempted and the person is not told the same thing three times.
        var nmcli = new FakeNmcli()
            .Answer("status", string.Empty, exitCode: 8, error: "Error: NetworkManager is not running.");
        var model = Model(nmcli);

        await model.RefreshAsync();

        Assert.Equal("Error: NetworkManager is not running.", model.Status);
        Assert.Empty(model.Devices);
        Assert.Empty(model.Connections);
        Assert.Empty(model.Wifi);
        Assert.Single(nmcli.Calls);
    }

    [Fact]
    public async Task ReturningToAKnownNetworkNeedsNoTerminalAndNoPassword()
    {
        // The point of the whole design: there is already a profile for this SSID, so joining it
        // is an activation, which NetworkManager's policy allows any active session to do.
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.JoinAsync(model.Wifi.Single(n => n.Ssid == "Cafe: Free"));

        Assert.Empty(nmcli.Terminals);
        Assert.Equal(1, nmcli.CallsContaining("connection up uuid 11111111-1111-4111-8111-111111111111"));
    }

    [Fact]
    public async Task JoiningANewNetworkGoesToATerminalWhereAPasswordCanBeTyped()
    {
        // No profile for this one, so a profile has to be created -- which polkit guards with
        // auth_admin_keep, and a text session has no agent to answer it.
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.JoinAsync(model.Wifi.Single(n => n.Ssid == "New Place"));

        var (title, script) = Assert.Single(nmcli.Terminals);
        Assert.Equal("Join New Place", title);
        Assert.Contains("--ask", script);
        Assert.Contains("|| sudo", script);
        Assert.Equal(0, nmcli.CallsContaining("connection up"));
    }

    [Fact]
    public async Task AnOpenNetworkTakesTheSamePathAsASecuredOne()
    {
        // It needs no passphrase and still needs a profile, so it is still an administrator's
        // business. nmcli's --ask simply asks for nothing.
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.JoinAsync(model.Wifi.Single(n => n.Ssid == "Open Guest"));

        Assert.Single(nmcli.Terminals);
    }

    [Fact]
    public async Task AHiddenNetworkSaysWhyItCannotBeJoinedFromTheList()
    {
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.JoinAsync(model.Wifi.Single(n => n.IsHidden));

        Assert.Empty(nmcli.Terminals);
        Assert.Contains("does not broadcast its name", model.Status);
    }

    [Fact]
    public async Task AFailedActivationIsReportedInNmclisOwnWords()
    {
        var nmcli = FullyStocked()
            .Answer("up", string.Empty, exitCode: 4,
                error: "Error: Connection activation failed: Secrets were required, but not provided.");
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.ActivateAsync(model.Connections.First());

        Assert.Equal(
            "Error: Connection activation failed: Secrets were required, but not provided.",
            model.Status);
    }

    private const string NotAuthorized =
        "Error: Connection activation failed: Not authorized to control networking.";

    [Fact]
    public async Task ARefusedActivationContinuesUnderSudoInATerminal()
    {
        // What a session with no seat gets -- measured from WSL, and SSH is the same. polkit
        // falls through to auth_admin, which sudo can answer and a background call cannot.
        var nmcli = FullyStocked().Answer("up", string.Empty, exitCode: 4, error: NotAuthorized);
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.ActivateAsync(model.Connections.Single(c => c.Name == "Wired"));

        var (title, script) = Assert.Single(nmcli.Terminals);
        Assert.Equal("Connecting to Wired", title);
        Assert.StartsWith("sudo nmcli ", script);
        Assert.Contains("'up' 'uuid' '22222222-2222-4222-8222-222222222222'", script);
        Assert.Contains("terminal window", model.Status);
    }

    [Fact]
    public async Task ARefusedDeactivationTakesTheSameRoute()
    {
        var nmcli = FullyStocked().Answer("down", string.Empty, exitCode: 4, error: NotAuthorized);
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.DeactivateAsync(model.Connections.First());

        Assert.Contains("'down'", Assert.Single(nmcli.Terminals).Script);
    }

    [Fact]
    public async Task AnActivationThatFailsForAnyOtherReasonStaysInTheWindow()
    {
        // A wrong secret or a missing lease is not something sudo would fix, and a terminal
        // asking for a password over it would be both useless and alarming.
        var nmcli = FullyStocked()
            .Answer("up", string.Empty, exitCode: 4,
                error: "Error: Connection activation failed: Secrets were required, but not provided.");
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.ActivateAsync(model.Connections.First());

        Assert.Empty(nmcli.Terminals);
    }

    [Fact]
    public async Task WhenTheTerminalFinishesTheListsAreReadAgain()
    {
        // Otherwise the window goes on showing the state from before the terminal opened.
        var nmcli = FullyStocked().Answer("up", string.Empty, exitCode: 4, error: NotAuthorized);
        var model = Model(nmcli);
        await model.RefreshAsync();
        await model.ActivateAsync(model.Connections.Single(c => c.Name == "Wired"));
        var before = nmcli.CallsContaining("device status");

        nmcli.TerminalExits.Single()(0);

        Assert.Equal(before + 1, nmcli.CallsContaining("device status"));
        Assert.Equal("Wired is up.", model.Status);
    }

    [Fact]
    public async Task ATerminalThatFailsSaysSo()
    {
        // Wrong sudo password three times, or the window closed: sudo exits 1, and the lists on
        // their own would not say that anything was attempted.
        var nmcli = FullyStocked().Answer("up", string.Empty, exitCode: 4, error: NotAuthorized);
        var model = Model(nmcli);
        await model.RefreshAsync();
        await model.ActivateAsync(model.Connections.Single(c => c.Name == "Wired"));

        nmcli.TerminalExits.Single()(1);

        Assert.Equal("Connecting to Wired did not finish (exit code 1).", model.Status);
    }

    [Fact]
    public async Task JoiningANewNetworkRereadsTheListsWhenTheTerminalFinishes()
    {
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();
        await model.JoinAsync(model.Wifi.Single(n => n.Ssid == "New Place"));
        var before = nmcli.CallsContaining("device status");

        nmcli.TerminalExits.Single()(0);

        Assert.Equal(before + 1, nmcli.CallsContaining("device status"));
        Assert.Equal("Joined New Place.", model.Status);
    }

    [Fact]
    public async Task ATerminalThatOutlivesTheWindowChangesNothing()
    {
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        using var closing = new CancellationTokenSource();
        await model.RefreshAsync(cancellationToken: closing.Token);
        await model.JoinAsync(model.Wifi.Single(n => n.Ssid == "New Place"), closing.Token);
        var calls = nmcli.Calls.Count;

        closing.Cancel();
        nmcli.TerminalExits.Single()(0);

        Assert.Equal(calls, nmcli.Calls.Count);
    }

    [Fact]
    public async Task AnActionRereadsTheListsRatherThanAssumingWhatItDid()
    {
        // Which profile is on which device is exactly what an activation changes, and the only
        // trustworthy source for it is nmcli.
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();
        var before = nmcli.CallsContaining("device status");

        await model.ActivateAsync(model.Connections.First());

        Assert.Equal(before + 1, nmcli.CallsContaining("device status"));
    }

    [Fact]
    public async Task TheOutcomeOfAnActionSurvivesTheRefreshThatFollowsIt()
    {
        // The refresh clears the status line; the result of what the person just did is the
        // more useful of the two things that could be on it.
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();

        await model.ActivateAsync(model.Connections.First());

        Assert.Equal("Cafe: Free is up.", model.Status);
    }

    [Fact]
    public async Task TheSelectionStaysWhereItWasWhenTheListIsScannedAgain()
    {
        // The Wi-Fi list is re-sorted by signal on every scan, so keeping an index would move
        // the selection under the person's hands between one scan and the next.
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();
        model.SelectedWifi = model.Wifi.Single(n => n.Ssid == "Open Guest");

        nmcli.Answer("list", ":New Place:81:WPA2\n:Open Guest:44:\n*:Cafe\\: Free:39:WPA2");
        await model.RefreshAsync(rescan: true);

        Assert.Equal("Open Guest", model.SelectedWifi?.Ssid);
        Assert.Equal("New Place", model.Wifi.Skip(1).First().Ssid);
    }

    [Fact]
    public async Task NothingSelectedIsNotAnAction()
    {
        var nmcli = FullyStocked();
        var model = Model(nmcli);
        await model.RefreshAsync();
        var calls = nmcli.Calls.Count;

        await model.JoinAsync(null);
        await model.ActivateAsync(null);
        await model.DeactivateAsync(null);

        Assert.Equal(calls, nmcli.Calls.Count);
        Assert.Empty(nmcli.Terminals);
    }
}
