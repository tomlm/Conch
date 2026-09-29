using Conch.Utilities;
using Xunit;

namespace Conch.Tests;

public class ShellCommandTests
{
    [Fact]
    public void ProcessRunsDirectlyWhenNotUsingWsl()
    {
        var command = ShellCommand.ForProcess("btop", new[] { "-p", "0" }, viaWsl: false, os: HostOs.Linux);

        Assert.Equal("btop", command.Process);
        Assert.Equal(new[] { "-p", "0" }, command.Args);
    }

    [Fact]
    public void ProcessIsWrappedForWslOnWindows()
    {
        var command = ShellCommand.ForProcess("btop", new[] { "-p" }, viaWsl: true, os: HostOs.Windows);

        // Through a login shell, so the command resolves on the same PATH that detection
        // used. Launching it directly finds a different PATH, which is what made a snap
        // report as installed and then fail to start.
        Assert.Equal("wsl", command.Process);
        Assert.Equal(new[] { "--", "bash", "-lc", "exec \"$0\" \"$@\"", "btop", "-p" }, command.Args);
    }

    [Fact]
    public void WslArgumentsArePassedPositionallyRatherThanInterpolated()
    {
        // An argument with a space or a quote has to survive as one argument and must not
        // be able to change what runs, so the command and its arguments go to the shell as
        // positional parameters instead of being pasted into the script text.
        var command = ShellCommand.ForProcess(
            "nano",
            new[] { "my notes.txt", "a\"; rm -rf /" },
            viaWsl: true,
            os: HostOs.Windows);

        Assert.Equal("exec \"$0\" \"$@\"", command.Args[3]);
        Assert.Equal(new[] { "nano", "my notes.txt", "a\"; rm -rf /" }, command.Args.Skip(4));
    }

    [Theory]
    [InlineData(HostOs.Linux)]
    [InlineData(HostOs.MacOS)]
    public void WslWrappingIsIgnoredOffWindows(HostOs os)
    {
        // Asking for WSL on Linux is meaningless; run the command directly rather than
        // producing an invocation that cannot exist.
        var command = ShellCommand.ForProcess("btop", Array.Empty<string>(), viaWsl: true, os);

        Assert.Equal("btop", command.Process);
        Assert.Empty(command.Args);
    }

    [Fact]
    public void DefaultShellIsPlatformAppropriate()
    {
        Assert.Equal("cmd.exe", ShellCommand.DefaultShellFor(HostOs.Windows));
        Assert.Contains("sh", ShellCommand.DefaultShellFor(HostOs.Linux));
    }

    [Fact]
    public void RenderedCommandQuotesArgumentsContainingSpaces()
    {
        // Only used for the log, but a logged command that cannot be pasted back is misleading.
        var command = new ResolvedCommand("bash", new List<string> { "-lc", "echo a b" });

        Assert.Equal("bash -lc \"echo a b\"", command.ToString());
    }

    [Fact]
    public void RenderedCommandLeavesPlainArgumentsAlone()
    {
        var command = new ResolvedCommand("btop", new List<string> { "-p" });

        Assert.Equal("btop -p", command.ToString());
    }

    [Fact]
    public void RenderedCommandWithNoArgumentsIsJustTheProcess()
    {
        Assert.Equal("btop", new ResolvedCommand("btop", new List<string>()).ToString());
    }
}
