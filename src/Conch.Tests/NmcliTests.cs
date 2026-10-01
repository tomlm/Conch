using Conch.Services;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// The nmcli boundary: how its output is read, and how a value reaches it safely.
/// </summary>
/// <remarks>
/// The connection and device fixtures are nmcli 1.46 output captured verbatim, including a
/// connection deliberately named <c>weird:name with\back</c> -- because the escaping that name
/// produces is the whole reason terse output cannot be read with a string split, and a fixture
/// nobody measured would have encoded a guess about it. The Wi-Fi fixture is built by hand from
/// the same escaping rule and the column order nmcli was asked for: the host these were captured
/// on has no wireless interface, so its access point list cannot be captured, and inventing one
/// is better than pretending the shape is unknown.
/// </remarks>
public class NmcliTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string Connections => Fixture("nmcli-connections-wsl.txt");

    private static string Devices => Fixture("nmcli-devices-wsl.txt");

    private static string WifiList => Fixture("nmcli-wifi.txt");

    [Fact]
    public void TheFixturesAreStillTheRealThing()
    {
        // If either shrinks, someone has trimmed a capture and the tests below stop meaning
        // what they say.
        Assert.Equal(3, Nmcli.ParseConnections(Connections).Count);
        Assert.Equal(3, Nmcli.ParseDevices(Devices).Count);
    }

    [Fact]
    public void AColonInAConnectionNameIsNotAFieldSeparator()
    {
        // The row as nmcli wrote it:
        //   weird\:name with\\back:4f221bcb-1709-4ce1-bc39-a11a93fa39a6:dummy:
        // A naive split gives six fields for a four-field record, which puts the UUID in the
        // type column and makes the row nonsense.
        var profile = Nmcli.ParseConnections(Connections).Single(c => c.Type == "dummy");

        Assert.Equal("weird:name with\\back", profile.Name);
        Assert.Equal("4f221bcb-1709-4ce1-bc39-a11a93fa39a6", profile.Uuid);
    }

    [Fact]
    public void TheTypeIsTheSettingNameAndIsShownAsTheFriendlyOne()
    {
        // Terse output prints 802-11-wireless where the ordinary listing prints wifi. Both
        // matter: the raw name is what identifies a wireless profile, the friendly one is what
        // a person reads.
        var wifi = Nmcli.ParseConnections(Connections).Single(c => c.Name == "Cafe WiFi");

        Assert.Equal("802-11-wireless", wifi.Type);
        Assert.Equal("wifi", wifi.FriendlyType);
        Assert.True(wifi.IsWifi);

        var wired = Nmcli.ParseConnections(Connections).Single(c => c.Name == "Wired boring");

        Assert.Equal("ethernet", wired.FriendlyType);
        Assert.False(wired.IsWifi);
    }

    [Fact]
    public void AProfileWithNoDeviceIsNotActive()
    {
        // Every profile in the capture is inactive, and terse output says so by leaving the
        // device column empty.
        Assert.All(Nmcli.ParseConnections(Connections), c => Assert.False(c.IsActive));
    }

    [Fact]
    public void AProfileOnADeviceIsActive()
    {
        var active = Nmcli.ParseConnections(
            "Home WiFi:2c4a1f90-0000-4000-8000-000000000001:802-11-wireless:wlan0").Single();

        Assert.True(active.IsActive);
        Assert.Equal("Home WiFi *", active.DisplayName);
        Assert.Equal("wifi on wlan0", active.Detail);
    }

    [Fact]
    public void TwoProfilesWithOneNameAreToldApartByUuid()
    {
        // Measured: running the same `nmcli connection add` twice gives exactly this.
        var connections = Nmcli.ParseConnections(
            "test-dummy:fa9dd0bb-13dc-41ba-951a-98b89ccd3afa:dummy:\n"
            + "test-dummy:2bec7587-b55d-48ce-84dd-8f422f59edc9:dummy:dummy0\n"
            + "Wired:22222222-2222-4222-8222-222222222222:802-3-ethernet:");

        Assert.Equal(
            new[] { "test-dummy (fa9dd0bb)", "test-dummy (2bec7587) *", "Wired" },
            connections.Select(c => c.DisplayName));
    }

    [Theory]
    [InlineData("Error: Connection activation failed: Not authorized to control networking.", true)]
    [InlineData("Error: Failed to add 'test-dummy' connection: Insufficient privileges", true)]
    [InlineData("Error: Connection activation failed: Secrets were required, but not provided.", false)]
    [InlineData("Error: NetworkManager is not running.", false)]
    public void ARefusalIsToldApartFromAFailure(string error, bool refused)
    {
        // Both measured refusals exit 4, as does an ordinary activation failure, so only the
        // words can tell them apart.
        Assert.Equal(refused, new NmcliResult(4, string.Empty, error).IsNotAuthorized);
    }

    [Fact]
    public void ARefusedActionIsRetriedUnderSudoWithEveryArgumentQuoted()
    {
        Assert.Equal(
            "sudo nmcli '-w' '25' 'connection' 'up' 'uuid' 'abc'",
            Nmcli.SudoScript(Nmcli.UpArgs("abc")));
    }

    [Fact]
    public void DevicesKeepTheirStateAndTheirConnection()
    {
        var devices = Nmcli.ParseDevices(Devices);

        Assert.Equal(new[] { "dummy0", "eth0", "lo" }, devices.Select(d => d.Name));

        // Nothing is managed on the capture host, so each device's detail falls back to saying
        // what it is doing rather than what is on it.
        Assert.All(devices, d => Assert.Equal("unmanaged", d.Detail));
        Assert.DoesNotContain(devices, d => d.IsWifi);
    }

    [Fact]
    public void AWirelessInterfaceIsRecognised()
    {
        // The one fact the Wi-Fi tab needs from the device list, because nmcli cannot otherwise
        // tell "no wireless hardware" from "nothing in range": both are no output.
        var device = Nmcli.ParseDevices("wlan0:wifi:connected:Home WiFi").Single();

        Assert.True(device.IsWifi);
        Assert.Equal("Home WiFi", device.Detail);
    }

    [Fact]
    public void TheNetworkInUseLeadsAndTheRestFollowTheSignal()
    {
        var networks = Nmcli.ParseWifi(WifiList);

        Assert.Equal(
            new[] { "Cafe: Free", "Neighbour 2.4GHz", "Bob's Hotspot", string.Empty, "Open Guest" },
            networks.Select(n => n.Ssid));

        Assert.True(networks[0].InUse);
        Assert.Equal("Cafe: Free *", networks[0].DisplayName);
    }

    [Fact]
    public void AnOpenNetworkIsToldApartFromASecuredOne()
    {
        var networks = Nmcli.ParseWifi(WifiList);

        var open = networks.Single(n => n.Ssid == "Open Guest");
        Assert.True(open.IsOpen);
        Assert.Equal("open", open.SecurityText);

        var secured = networks.Single(n => n.Ssid == "Bob's Hotspot");
        Assert.False(secured.IsOpen);
        Assert.Equal("WPA2 WPA3", secured.SecurityText);
    }

    [Fact]
    public void AHiddenNetworkIsShownAsHiddenRatherThanAsABlankRow()
    {
        // A network that broadcasts no name arrives with an empty SSID field. Left as it comes,
        // it is a row with nothing in it, which reads as a rendering fault.
        var hidden = Nmcli.ParseWifi(WifiList).Single(n => n.IsHidden);

        Assert.Equal("(hidden)", hidden.DisplayName);
        Assert.Equal(30, hidden.Signal);
    }

    [Theory]
    // Two fields where four are expected: a listing from another nmcli, or a warning that
    // reached stdout. One row is lost; the alternative is indexing off the end of it.
    [InlineData("eth0:ethernet")]
    [InlineData("")]
    [InlineData("\n\n")]
    public void OutputThatIsNotARowIsSkippedRatherThanThrowing(string output)
    {
        Assert.Empty(Nmcli.ParseDevices(output));
    }

    [Theory]
    [InlineData("a:b", new[] { "a", "b" })]
    [InlineData("a\\:b", new[] { "a:b" })]
    [InlineData("a\\\\b:c", new[] { "a\\b", "c" })]
    [InlineData("::", new[] { "", "", "" })]
    // A line cut short mid-escape costs one odd character, not the row.
    [InlineData("a\\", new[] { "a\\" })]
    public void FieldsAreSplitOnUnescapedColonsOnly(string line, string[] expected)
    {
        Assert.Equal(expected, Nmcli.SplitFields(line));
    }

    [Theory]
    [InlineData("Home", "'Home'")]
    [InlineData("Free WiFi", "'Free WiFi'")]
    [InlineData("Bob's", "'Bob'\\''s'")]
    // The reason this function exists: an SSID is named by a stranger and arrives over the air.
    [InlineData("a; rm -rf /", "'a; rm -rf /'")]
    [InlineData("$(whoami)", "'$(whoami)'")]
    [InlineData("back\\slash", "'back\\slash'")]
    public void AValueIsQuotedSoAShellCannotReadItAsSyntax(string value, string expected)
    {
        Assert.Equal(expected, Nmcli.Quote(value));
    }

    [Fact]
    public void JoiningANetworkAsksNmcliForTheSecretAndFallsBackToSudo()
    {
        // Pinned whole, because every part of this line is a decision:
        //   --ask   nmcli prompts for the passphrase itself, so Conch never holds it
        //   quoting  the SSID cannot become shell syntax
        //   || sudo  a first-time join writes a system profile, which polkit guards with
        //            auth_admin_keep and a text session has no agent to answer
        Assert.Equal(
            "nmcli --ask device wifi connect 'Cafe: Free' || sudo nmcli --ask device wifi connect 'Cafe: Free'",
            Nmcli.JoinWifiScript("Cafe: Free"));
    }

    [Fact]
    public void AnSsidWithAQuoteInItStaysOneArgument()
    {
        var script = Nmcli.JoinWifiScript("Bob's; reboot");

        Assert.Equal(
            "nmcli --ask device wifi connect 'Bob'\\''s; reboot' "
                + "|| sudo nmcli --ask device wifi connect 'Bob'\\''s; reboot'",
            script);
    }

    [Fact]
    public void EveryListingAsksForTerseEscapedOutput()
    {
        // The contract SplitFields is written against. A listing that lost either flag would
        // parse into plausible nonsense rather than failing outright.
        foreach (var args in new[] { Nmcli.DeviceArgs, Nmcli.ConnectionArgs, Nmcli.WifiArgs(false) })
        {
            Assert.Contains("-t", args);
            Assert.Equal("yes", args[args.ToList().IndexOf("--escape") + 1]);
        }
    }

    [Fact]
    public void AScanIsOnlyForcedWhenItIsAskedFor()
    {
        // Opening the window reuses nmcli's cached scan, so it paints at once; RESCAN is what
        // makes the adapter go and look.
        Assert.Equal("auto", Nmcli.WifiArgs(rescan: false).Last());
        Assert.Equal("yes", Nmcli.WifiArgs(rescan: true).Last());
    }

    [Fact]
    public void ConnectionsAreActedOnByUuidRatherThanByName()
    {
        // Two profiles may share a name -- nmcli allows it -- and a name may also look like a
        // UUID. The 'uuid' keyword removes both ambiguities.
        var up = Nmcli.UpArgs("2c4a1f90-0000-4000-8000-000000000001");

        Assert.Equal(new[] { "connection", "up", "uuid", "2c4a1f90-0000-4000-8000-000000000001" },
            up.Skip(2));
        Assert.Equal(new[] { "connection", "down", "uuid", "abc" }, Nmcli.DownArgs("abc").Skip(2));
    }

    [Fact]
    public void NmcliIsGivenItsOwnDeadlineRatherThanBeingKilled()
    {
        // -w, so a lease that never arrives comes back as an error nmcli can explain instead of
        // as a process Conch had to shoot.
        Assert.Equal("-w", Nmcli.UpArgs("abc")[0]);
        Assert.True(int.Parse(Nmcli.UpArgs("abc")[1]) > 0);
    }

    [Fact]
    public void AFailureIsReportedInNmclisOwnWords()
    {
        var result = new NmcliResult(8, string.Empty, "Error: NetworkManager is not running.\n");

        Assert.False(result.Ok);
        Assert.Equal("Error: NetworkManager is not running.", result.Message);
    }

    [Fact]
    public void AFailureWithNothingToSayStillSaysSomething()
    {
        Assert.Equal("nmcli exited with code 4.", new NmcliResult(4, string.Empty, string.Empty).Message);
    }

    [Fact]
    public async Task TheRealNmcliOnThisMachineIsUnderstood()
    {
        // Every other test here reads a capture. This one runs whatever nmcli is installed and
        // holds the whole path to its promise: start the process, read the C locale's output,
        // parse it -- or come back with a sentence explaining why not. On a machine without
        // NetworkManager there is nothing to ask, which includes every Windows run.
        if (!Nmcli.IsPresent)
        {
            return;
        }

        var result = await Nmcli.RunAsync(Nmcli.DeviceArgs);

        if (!result.Ok)
        {
            // A daemon that is not running is a legitimate answer, and the window shows this
            // sentence. What would not be legitimate is failing silently.
            Assert.NotEmpty(result.Message);
            return;
        }

        // Loopback is the one interface every machine has.
        Assert.Contains(Nmcli.ParseDevices(result.Output), d => d.Name == "lo");
    }
}
