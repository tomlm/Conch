using Conch.Utilities;
using Xunit;

namespace Conch.Tests;

public class BackgroundProcessTests
{
    [Fact]
    public void ABackgroundProcessSharesNoStreamWithTheTerminal()
    {
        // Any one stream left shared and .NET on Unix restores the terminal's echo for as long
        // as the child runs, which printed mouse reports over the Network window as it opened.
        var startInfo = BackgroundProcess.StartInfo("nmcli", ["-t", "device"]);

        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(new[] { "-t", "device" }, startInfo.ArgumentList);
    }
}
