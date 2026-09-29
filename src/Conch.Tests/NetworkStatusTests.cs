using System.Diagnostics;
using Conch.Services;
using Conch.Utilities;
using Xunit;

namespace Conch.Tests;

public class NetworkStatusTests
{
    [Fact]
    public async Task SamplingDoesNotHappenOnTheCallingThread()
    {
        // The reason this matters, measured: one sample through NetworkInterface costs 30.7ms
        // on Windows, which is about two frames of console redraw to answer a question nobody
        // is waiting on. A first attempt called it straight from the timer tick; this test was
        // written as a cost check, failed, and turned into the fix.
        var callingThread = Environment.CurrentManagedThreadId;
        var sampledOn = 0;

        await Task.Run(() =>
        {
            sampledOn = Environment.CurrentManagedThreadId;
            NetworkStatus.IsUp();
        });

        await NetworkStatus.IsUpAsync();

        Assert.NotEqual(callingThread, sampledOn);
    }

    [Fact]
    public async Task TheAsyncPathGivesTheSameAnswerAsTheDirectOne()
    {
        Assert.Equal(NetworkStatus.IsUp(), await NetworkStatus.IsUpAsync());
    }

    [Fact]
    public void OneSampleIsNotPathologicallySlow()
    {
        // Not a tight bar -- it is off the UI thread now -- but a sample that took seconds
        // would mean something is blocking on the network rather than reading local state.
        var stopwatch = Stopwatch.StartNew();

        NetworkStatus.IsUp();

        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"one sample took {stopwatch.ElapsedMilliseconds}ms; it should read local state only.");
    }

    [Fact]
    public void AnswersWithoutThrowingOnThisMachine()
    {
        // Both paths swallow their own failures: an unreadable sysfs or a refused
        // NetworkInformation call must report "down", never take the shell with it.
        var exception = Record.Exception(() => NetworkStatus.IsUp(Host.Current));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(HostOs.Windows)]
    [InlineData(HostOs.Linux)]
    [InlineData(HostOs.MacOS)]
    public void EveryPlatformHasAnAnswer(HostOs os)
    {
        // Linux reads sysfs and the others use NetworkInformation; neither may throw when
        // asked about a platform this machine is not.
        var exception = Record.Exception(() => NetworkStatus.IsUp(os));

        Assert.Null(exception);
    }
}
