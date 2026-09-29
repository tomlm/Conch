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
    public void PoweringOffOnLinuxGoesThroughSudo()
    {
        // systemd asks polkit whether a non-root user may power the machine down, and polkit
        // is not in the Conchix image: mmdebstrap installs no Recommends, and polkitd is a
        // Recommends of systemd rather than a dependency. A bare systemctl call from the
        // session account fails authentication, so sudo is the way through.
        Assert.Equal("sudo systemctl poweroff", SessionActions.PowerOffCommand(HostOs.Linux));
        Assert.Equal("sudo systemctl reboot", SessionActions.RestartCommand(HostOs.Linux));
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
