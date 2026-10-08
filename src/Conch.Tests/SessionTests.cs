using Conch.Services;
using Conch.Utilities;
using Xunit;

namespace Conch.Tests;

public class SessionTests
{
    [Fact]
    public void ShutdownIsOffUnlessConchWasToldItOwnsTheSession()
    {
        // The default matters more than the flag. Conch on a developer's machine is an
        // ordinary application, and an ordinary application does not offer to reboot the
        // box being worked on.
        Assert.False(CommandLineOptions.Parse(Array.Empty<string>()).OwnsSession);
        Assert.False(CommandLineOptions.Parse(null).OwnsSession);
        Assert.False(CommandLineOptions.Parse(new[] { "--verbose", "somefile" }).OwnsSession);
    }

    [Fact]
    public void TheSessionFlagTurnsShutdownOn()
    {
        Assert.True(CommandLineOptions.Parse(new[] { "--session" }).OwnsSession);
        Assert.True(CommandLineOptions.Parse(new[] { "--other", "--session" }).OwnsSession);
    }

    [Fact]
    public void OtherArgumentsAreLeftForAvaloniaToRead()
    {
        // Parsing here must not consume anything: the same array goes on to the toolkit, and
        // a flag meant for it that Conch swallowed would simply stop working.
        var args = new[] { "--session", "--tweak" };

        CommandLineOptions.Parse(args);

        Assert.Equal(new[] { "--session", "--tweak" }, args);
    }

    [Fact]
    public void PoweringOffOnLinuxTriesTheDirectRouteBeforeSudo()
    {
        // Whether a non-root user may power the machine down is polkit's answer, and it
        // differs per machine: with polkitd present -- Cursix, since NetworkManager needs
        // it -- logind authorises a local seat user and there is nothing to type. Without it
        // the call fails authentication and sudo is the way through.
        //
        // The order is the point. Reversing it would prompt for a password on every shutdown
        // of a machine that never needed one, which on an appliance is a password between
        // the user and turning their computer off.
        Assert.Equal("systemctl poweroff || sudo systemctl poweroff",
            SessionActions.PowerOffCommand(HostOs.Linux));
        Assert.Equal("systemctl reboot || sudo systemctl reboot",
            SessionActions.RestartCommand(HostOs.Linux));
    }

    [Fact]
    public void WindowsUsesItsOwnShutdownCommand()
    {
        Assert.Equal("shutdown /s /t 0", SessionActions.PowerOffCommand(HostOs.Windows));
        Assert.Equal("shutdown /r /t 0", SessionActions.RestartCommand(HostOs.Windows));
    }

    [Fact]
    public void MacOsOffersNoPowerCommand()
    {
        // Conch is not the session on a Mac, and guessing at a command that needs a password
        // prompt nobody asked for is worse than the menu item being absent.
        Assert.Null(SessionActions.PowerOffCommand(HostOs.MacOS));
        Assert.Null(SessionActions.RestartCommand(HostOs.MacOS));
    }
}
