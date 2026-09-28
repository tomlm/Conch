using Conch.Utilities;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// Covers how a registration picks its install, uninstall and detect commands for a given
/// platform, and how the chosen command becomes a process invocation.
/// </summary>
/// <remarks>
/// These are the paths that run package managers with elevated privileges. Picking the wrong
/// platform entry means running a Debian command on Windows or the reverse, so every branch is
/// exercised for every platform rather than only the one the tests happen to run on.
/// </remarks>
public class InstallResolutionTests
{
    private static ToolViewModel Tool(
        InstallDefinition? windows = null,
        InstallDefinition? linux = null,
        InstallDefinition? macos = null,
        InstallDefinition? dflt = null,
        string command = "thing") => new()
        {
            Id = "test.tool",
            Name = "Test Tool",
            Command = command,
            Platforms = new PlatformInstallDefinitions
            {
                Windows = windows,
                Linux = linux,
                MacOS = macos,
                Default = dflt,
            },
        };

    private static InstallDefinition Def(string tag) => new()
    {
        Install = $"install-{tag}",
        Uninstall = $"uninstall-{tag}",
    };

    // ------------------------------------------------------------- resolution ----

    [Theory]
    [InlineData(HostOs.Windows, ToolPlatform.Windows)]
    [InlineData(HostOs.Linux, ToolPlatform.Linux)]
    [InlineData(HostOs.MacOS, ToolPlatform.MacOS)]
    public void PrefersTheEntryForTheHost(HostOs os, ToolPlatform expected)
    {
        var tool = Tool(windows: Def("win"), linux: Def("lin"), macos: Def("mac"), dflt: Def("any"));

        Assert.Equal(expected, tool.ResolvePlatformFor(os));
    }

    [Theory]
    [InlineData(HostOs.Windows)]
    [InlineData(HostOs.Linux)]
    [InlineData(HostOs.MacOS)]
    [InlineData(HostOs.Other)]
    public void FallsBackToDefaultWhenTheHostHasNoEntry(HostOs os)
    {
        var tool = Tool(dflt: Def("any"));

        Assert.Equal(ToolPlatform.Default, tool.ResolvePlatformFor(os));
        Assert.Equal("install-any", tool.GetInstallDefinitionFor(os)!.Install);
    }

    [Fact]
    public void WindowsFallsBackToTheLinuxEntry()
    {
        // Deliberate: a Linux-only registration is still offered on Windows and run through WSL,
        // matching how an unresolvable command line already falls back to WSL.
        var tool = Tool(linux: Def("lin"));

        Assert.Equal(ToolPlatform.Linux, tool.ResolvePlatformFor(HostOs.Windows));
        Assert.Equal("install-lin", tool.GetInstallDefinitionFor(HostOs.Windows)!.Install);
        Assert.True(tool.RunsUnderWslOn(HostOs.Windows));
    }

    [Fact]
    public void WindowsEntryWinsOverLinuxAndDoesNotUseWsl()
    {
        var tool = Tool(windows: Def("win"), linux: Def("lin"));

        Assert.Equal("install-win", tool.GetInstallDefinitionFor(HostOs.Windows)!.Install);
        Assert.False(tool.RunsUnderWslOn(HostOs.Windows));
    }

    [Theory]
    [InlineData(HostOs.Linux)]
    [InlineData(HostOs.MacOS)]
    [InlineData(HostOs.Other)]
    public void OnlyWindowsEverRoutesThroughWsl(HostOs os)
    {
        var tool = Tool(linux: Def("lin"));

        Assert.False(tool.RunsUnderWslOn(os));
    }

    [Fact]
    public void LinuxDoesNotBorrowTheWindowsEntry()
    {
        // The reverse of the WSL fallback must not happen: winget lines must never run on Linux.
        var tool = Tool(windows: Def("win"));

        Assert.Equal(ToolPlatform.None, tool.ResolvePlatformFor(HostOs.Linux));
        Assert.Null(tool.GetInstallDefinitionFor(HostOs.Linux));
    }

    [Fact]
    public void MacOsDoesNotBorrowTheLinuxEntry()
    {
        var tool = Tool(linux: Def("lin"));

        Assert.Equal(ToolPlatform.None, tool.ResolvePlatformFor(HostOs.MacOS));
        Assert.Null(tool.GetInstallDefinitionFor(HostOs.MacOS));
    }

    [Fact]
    public void NoPlatformEntriesMeansNothingToInstall()
    {
        var tool = Tool();

        Assert.Equal(ToolPlatform.None, tool.ResolvePlatformFor(HostOs.Linux));
        Assert.Equal(string.Empty, tool.Install);
        Assert.Equal(string.Empty, tool.Uninstall);
        Assert.False(tool.HasInstall);
        Assert.False(tool.HasUninstall);
    }

    [Fact]
    public void UninstallResolvesFromTheSameEntryAsInstall()
    {
        var tool = Tool(windows: Def("win"), linux: Def("lin"));

        var onWindows = tool.GetInstallDefinitionFor(HostOs.Windows)!;
        Assert.Equal("install-win", onWindows.Install);
        Assert.Equal("uninstall-win", onWindows.Uninstall);

        var onLinux = tool.GetInstallDefinitionFor(HostOs.Linux)!;
        Assert.Equal("install-lin", onLinux.Install);
        Assert.Equal("uninstall-lin", onLinux.Uninstall);
    }

    [Fact]
    public void DetectResolvesFromTheSameEntryAsInstall()
    {
        var tool = Tool(
            windows: new InstallDefinition { Install = "i-win", Uninstall = "u-win", Detect = "where thing" },
            linux: new InstallDefinition { Install = "i-lin", Uninstall = "u-lin", Detect = "command -v thing" });

        Assert.Equal("where thing", tool.GetInstallDefinitionFor(HostOs.Windows)!.Detect);
        Assert.Equal("command -v thing", tool.GetInstallDefinitionFor(HostOs.Linux)!.Detect);
    }

    [Fact]
    public void DetectIsOptional()
    {
        var tool = Tool(linux: Def("lin"));

        Assert.Null(tool.GetInstallDefinitionFor(HostOs.Linux)!.Detect);
    }

    // -------------------------------------------------------- command building ----

    [Fact]
    public void LinuxInstallRunsThroughALoginShell()
    {
        var tool = Tool(linux: new InstallDefinition
        {
            Install = "sudo apt-get update && sudo apt-get install -y btop",
            Uninstall = "sudo apt-get remove -y btop",
        });

        var def = tool.GetInstallDefinitionFor(HostOs.Linux)!;
        var command = ShellCommand.ForScript(def.Install, tool.RunsUnderWslOn(HostOs.Linux), HostOs.Linux);

        // A shell is required: these lines chain with && and cannot be exec'd directly.
        Assert.Equal(new[] { "-lc", "sudo apt-get update && sudo apt-get install -y btop" }, command.Args);
        Assert.Contains("sh", command.Process);
    }

    [Fact]
    public void WindowsInstallRunsThroughCmd()
    {
        var tool = Tool(windows: new InstallDefinition
        {
            Install = "winget install -e --id Vim.Vim",
            Uninstall = "winget uninstall -e --id Vim.Vim",
        });

        var def = tool.GetInstallDefinitionFor(HostOs.Windows)!;
        var command = ShellCommand.ForScript(def.Install, tool.RunsUnderWslOn(HostOs.Windows), HostOs.Windows);

        Assert.Equal("cmd.exe", command.Process);
        Assert.Equal(new[] { "/c", "winget install -e --id Vim.Vim" }, command.Args);
    }

    [Fact]
    public void LinuxOnlyInstallOnWindowsGoesThroughWslNotCmd()
    {
        // The regression that matters: an apt line must never be handed to cmd.exe.
        var tool = Tool(linux: new InstallDefinition
        {
            Install = "sudo apt-get install -y ncdu",
            Uninstall = "sudo apt-get remove -y ncdu",
        });

        var def = tool.GetInstallDefinitionFor(HostOs.Windows)!;
        var command = ShellCommand.ForScript(def.Install, tool.RunsUnderWslOn(HostOs.Windows), HostOs.Windows);

        Assert.Equal("wsl", command.Process);
        Assert.Equal(new[] { "--", "bash", "-lc", "sudo apt-get install -y ncdu" }, command.Args);
        Assert.DoesNotContain("cmd.exe", command.Process);
    }

    [Fact]
    public void UninstallBuildsTheSameWayAsInstall()
    {
        var tool = Tool(linux: new InstallDefinition
        {
            Install = "sudo apt-get install -y ncdu",
            Uninstall = "sudo apt-get remove -y ncdu",
        });

        var def = tool.GetInstallDefinitionFor(HostOs.Windows)!;
        var command = ShellCommand.ForScript(def.Uninstall, tool.RunsUnderWslOn(HostOs.Windows), HostOs.Windows);

        Assert.Equal("wsl", command.Process);
        Assert.Equal(new[] { "--", "bash", "-lc", "sudo apt-get remove -y ncdu" }, command.Args);
    }

    [Fact]
    public void ScriptArgumentIsPassedWholeAndNotSplit()
    {
        // The script reaches the shell as one argument; splitting it would break every
        // registration that chains with && or quotes a path.
        var script = "sudo sh -c \"echo hello && echo world\"";

        var command = ShellCommand.ForScript(script, viaWsl: false, os: HostOs.Linux);

        Assert.Equal(2, command.Args.Count);
        Assert.Equal(script, command.Args[1]);
    }

    [Fact]
    public void DefaultEntryOnWindowsDoesNotUseWsl()
    {
        var tool = Tool(dflt: new InstallDefinition
        {
            Install = "dotnet tool install -g Edit.NET",
            Uninstall = "dotnet tool uninstall -g Edit.NET",
        });

        Assert.False(tool.RunsUnderWslOn(HostOs.Windows));

        var def = tool.GetInstallDefinitionFor(HostOs.Windows)!;
        var command = ShellCommand.ForScript(def.Install, tool.RunsUnderWslOn(HostOs.Windows), HostOs.Windows);

        Assert.Equal("cmd.exe", command.Process);
    }
}
