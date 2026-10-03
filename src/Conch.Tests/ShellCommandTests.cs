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
        Assert.Equal(new[] { "--exec", "bash", "-lc", "exec \"$0\" \"$@\"", "btop", "-p" }, command.Args);
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
    public void RenderedCommandEscapesQuotesInsideAnArgument()
    {
        // The WSL launcher passes a script that contains quotes. Rendered naively it came out
        // as wsl --exec bash -lc "exec "$0" "$@"" btop -- unpasteable, and it reads as though
        // the quoting were broken, in the log you are reading precisely because something is.
        var command = new ResolvedCommand("bash", new List<string> { "-lc", "exec \"$0\" \"$@\"" });

        Assert.Equal("bash -lc \"exec \\\"$0\\\" \\\"$@\\\"\"", command.ToString());
    }

    [Fact]
    public void RenderedCommandWithNoArgumentsIsJustTheProcess()
    {
        Assert.Equal("btop", new ResolvedCommand("btop", new List<string>()).ToString());
    }

    [Fact]
    public void WslInvocationsUseExecRatherThanTheDefaultShell()
    {
        // `wsl -- cmd args` does not run cmd: wsl.exe joins the arguments into a command
        // line for a shell, quoting only the ones containing whitespace, so any other
        // argument is read as shell syntax. Measured against the real wsl.exe: a ';' in an
        // argument started a second command, and '<' and '>' became redirections. --exec
        // runs the command directly, with no shell to reinterpret anything.
        var process = ShellCommand.ForProcess("btop", Array.Empty<string>(), viaWsl: true, os: HostOs.Windows);
        var script = ShellCommand.ForScript("apt-get install -y btop", viaWsl: true, os: HostOs.Windows);

        Assert.Equal("--exec", process.Args[0]);
        Assert.Equal("--exec", script.Args[0]);
        Assert.DoesNotContain("--", process.Args.Skip(1));
        Assert.DoesNotContain("--", script.Args.Skip(1));
    }

    [Fact]
    public void ScriptKeepsItsShellSyntaxInOneArgument()
    {
        // The install line is shell source and has to arrive as a single argument, or the
        // '&&' joining its two halves is acted on by the wrong shell -- which ran the first
        // half as a bare `sudo` and the second outside the login profile.
        var command = ShellCommand.ForScript("apt-get update && apt-get install -y btop",
            viaWsl: true, os: HostOs.Windows);

        Assert.Equal(new[] { "--exec", "bash", "-lc", "apt-get update && apt-get install -y btop" },
            command.Args);
    }

    [Fact]
    public void TypedCommandLineRunsThroughAShellSoItsSyntaxStillMeansSomething()
    {
        // Someone typing a command line expects a pipe in it to be a pipe. The tokens alone
        // cannot carry that: Tokenize has already taken the quotes off, so replaying them is
        // no longer the string that was typed.
        // A name nothing will resolve on either host, so this takes the same branch on the
        // Windows developer machine and on the Linux build.
        const string typed = "conch-no-such-command -la | grep conch";

        var command = ShellCommand.ForCommandLine(typed);

        Assert.Equal(typed, command.Args[^1]);
        Assert.Contains("-lc", command.Args);
    }

    [Fact]
    public void ACommandOnPathFollowedByShellSyntaxStillRunsThroughAShell()
    {
        // echo is on PATH on Linux, and on Windows wherever Git is; run directly, the ';' and
        // what follows were its arguments. Through a shell the line arrives whole.
        const string typed = "echo hi; sleep 5";

        var command = ShellCommand.ForCommandLine(typed);

        Assert.Equal(typed, command.Args[^1]);
        Assert.DoesNotContain("echo", Path.GetFileName(command.Process));
    }

    [Theory]
    [InlineData("ls -la | less", true)]
    [InlineData("make && make install", true)]
    [InlineData("echo $HOME", true)]
    [InlineData("ls *.txt", true)]
    [InlineData("grep 'two words' notes", true)]
    [InlineData("htop", false)]
    [InlineData("ls -la /var/log", false)]
    public void ShellSyntaxIsRecognised(string typed, bool expected)
    {
        Assert.Equal(expected, ShellCommand.HasShellSyntax(typed));
    }

    [Fact]
    public void DotnetToolShimIsRunThroughTheInterpreterThatUnderstandsIt()
    {
        // A .NET global tool installs a .cmd shim -- Edit.NET arrives on disk as
        // edit.net.cmd -- and a .cmd is a script, not an image CreateProcess can start.
        const string shim = @"C:\Users\someone\.dotnet\tools\edit.net.cmd";

        var command = ShellCommand.ForProcess("Edit.NET", new[] { "notes.txt" },
            viaWsl: false, os: HostOs.Windows, resolveOnPath: _ => shim);

        Assert.Equal("cmd.exe", command.Process);
        Assert.Equal(new[] { "/c", shim, "notes.txt" }, command.Args);
    }

    [Fact]
    public void BatchFileShimIsTreatedTheSameWay()
    {
        const string shim = @"C:\tools\thing.bat";

        var command = ShellCommand.ForProcess("thing", Array.Empty<string>(),
            viaWsl: false, os: HostOs.Windows, resolveOnPath: _ => shim);

        Assert.Equal("cmd.exe", command.Process);
        Assert.Equal(new[] { "/c", shim }, command.Args);
    }

    [Fact]
    public void ResolvedExecutableIsStartedDirectly()
    {
        const string exe = @"C:\Program Files\thing\thing.exe";

        var command = ShellCommand.ForProcess("thing", new[] { "-v" },
            viaWsl: false, os: HostOs.Windows, resolveOnPath: _ => exe);

        Assert.Equal(exe, command.Process);
        Assert.Equal(new[] { "-v" }, command.Args);
    }

    [Fact]
    public void UnresolvableCommandIsStillHandedOverByName()
    {
        // Nothing found on PATH: pass the name along rather than inventing a path. The
        // process layer gets its own chance, and its failure names the command.
        var command = ShellCommand.ForProcess("mystery", new[] { "-x" },
            viaWsl: false, os: HostOs.Windows, resolveOnPath: _ => null);

        Assert.Equal("mystery", command.Process);
        Assert.Equal(new[] { "-x" }, command.Args);
    }

    [Fact]
    public void PathResolutionDoesNotApplyToCommandsRunInsideWsl()
    {
        // The command lives in the WSL filesystem; a Windows PATH lookup has no business
        // answering for it, and a hit would be the wrong binary entirely.
        var resolverCalled = false;

        var command = ShellCommand.ForProcess("btop", Array.Empty<string>(),
            viaWsl: true, os: HostOs.Windows,
            resolveOnPath: _ => { resolverCalled = true; return @"C:\nope\btop.exe"; });

        Assert.False(resolverCalled);
        Assert.Equal("wsl", command.Process);
    }
}
