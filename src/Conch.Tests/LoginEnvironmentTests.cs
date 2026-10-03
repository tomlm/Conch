using Conch.Utilities;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// How a login PATH is combined with Conch's own, and read out of a login shell's output.
/// </summary>
/// <remarks>
/// Where Conch is the login shell (Conchix), nothing reads /etc/profile.d or ~/.profile, so
/// ~/.dotnet/tools never reached Conch's PATH: Edit.NET installed and then could not be found.
/// These cover the pure parts; the probe itself is one bash -lc call.
/// </remarks>
public class LoginEnvironmentTests
{
    [Fact]
    public void TheLoginPathComesFirst()
    {
        var merged = LoginEnvironment.Merge(
            "/usr/local/bin:/usr/bin:/home/tom/.dotnet/tools",
            "/usr/bin:/bin",
            ':', ignoreCase: false);

        Assert.Equal("/usr/local/bin:/usr/bin:/home/tom/.dotnet/tools:/bin", merged);
    }

    [Fact]
    public void WhatTheCurrentPathHasIsKept()
    {
        // Conch may have been started with a PATH of someone's choosing; losing it would make
        // commands that worked a moment ago disappear.
        var merged = LoginEnvironment.Merge("/usr/bin", "/opt/mine/bin:/usr/bin", ':', ignoreCase: false);

        Assert.Equal("/usr/bin:/opt/mine/bin", merged);
    }

    [Fact]
    public void DuplicatesAndEmptyEntriesGo()
    {
        var merged = LoginEnvironment.Merge("/usr/bin::/usr/bin/: /bin ", "/bin:", ':', ignoreCase: false);

        Assert.Equal("/usr/bin:/bin", merged);
    }

    [Fact]
    public void RootIsNotMistakenForAnEmptyEntry()
    {
        Assert.Equal("/:/usr/bin", LoginEnvironment.Merge("/", "/usr/bin", ':', ignoreCase: false));
    }

    [Fact]
    public void WindowsPathsCompareWithoutCase()
    {
        var merged = LoginEnvironment.Merge(
            @"C:\Windows\System32;C:\Users\tom\AppData\Local\Microsoft\WinGet\Links",
            @"c:\windows\system32;C:\Tools",
            ';', ignoreCase: true);

        Assert.Equal(@"C:\Windows\System32;C:\Users\tom\AppData\Local\Microsoft\WinGet\Links;C:\Tools", merged);
    }

    [Fact]
    public void LinuxPathsCompareWithCase()
    {
        Assert.Equal("/opt/A:/opt/a", LoginEnvironment.Merge("/opt/A", "/opt/a", ':', ignoreCase: false));
    }

    [Fact]
    public void ThePathIsReadFromBetweenTheMarkersWhateverTheProfilePrints()
    {
        // A .profile may print a greeting or a fortune before the PATH is printed.
        var output = "Welcome back!\nToday is a good day.\n__CONCH_PATH_BEGIN__/usr/bin:/home/tom/.local/bin__CONCH_PATH_END__";

        Assert.Equal("/usr/bin:/home/tom/.local/bin", LoginEnvironment.ParseProbeOutput(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no markers at all")]
    [InlineData("__CONCH_PATH_BEGIN__/usr/bin")]
    public void OutputWithoutBothMarkersGivesNothing(string output)
    {
        // Rather than a half-read PATH: the current one is kept instead.
        Assert.Null(LoginEnvironment.ParseProbeOutput(output));
    }
}
