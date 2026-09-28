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

        Assert.Equal("wsl", command.Process);
        Assert.Equal(new[] { "--", "btop", "-p" }, command.Args);
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
