using Avalonia.Media;
using Conch.Services;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

/// <summary>What the app manager's buttons allow, and what RECHECK shows while it works.</summary>
public class AppManagerViewModelTests
{
    private static AppManagerViewModel Manager()
        => new(new AppViewModel(new ShellSettings(Path.GetTempPath())),
            (_, _) => Task.FromResult<byte[]?>(null),
            _ => throw new InvalidOperationException("no pictures here"));

    private static ToolViewModel Tool(bool installed) => new()
    {
        Id = "t",
        Name = "t",
        Command = "t",
        IsDetected = true,
        IsInstalled = installed,
        Platforms = new PlatformInstallDefinitions
        {
            Default = new InstallDefinition { Install = "i", Uninstall = "u" },
        },
    };

    [Fact]
    public void AnAppThatIsNotInstalledCannotBeRun()
    {
        var manager = Manager();

        manager.SelectedTool = Tool(installed: false);

        Assert.False(manager.CanRun);
        Assert.True(manager.CanInstall);
    }

    [Fact]
    public void RunFollowsTheAppOnceItIsInstalled()
    {
        // Detection finishes after selection, and an install finishes while it is selected.
        var manager = Manager();
        var tool = Tool(installed: false);
        manager.SelectedTool = tool;
        var raised = new List<string?>();
        manager.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tool.IsInstalled = true;

        Assert.True(manager.CanRun);
        Assert.Contains(nameof(AppManagerViewModel.CanRun), raised);
    }

    [Fact]
    public async Task RecheckShowsEveryAppAsCheckingWhileItRuns()
    {
        // The usual recheck changes nothing, so this is what makes it visible at all.
        var manager = Manager();
        bool? allCheckingDuringSweep = null;
        bool? checkingFlagDuringSweep = null;

        await manager.RecheckAsync(tools =>
        {
            allCheckingDuringSweep = tools.All(t => !t.IsDetected);
            checkingFlagDuringSweep = manager.IsChecking && !manager.CanRecheck;
            foreach (var t in tools)
            {
                t.IsDetected = true;
            }
            return Task.CompletedTask;
        });

        Assert.True(allCheckingDuringSweep);
        Assert.True(checkingFlagDuringSweep);
        Assert.False(manager.IsChecking);
        Assert.Matches(@"^\d+ of \d+ apps installed\.$", manager.CheckStatus);
    }

    [Fact]
    public void OnlyOneTaskRunsAtATime()
    {
        var manager = Manager();
        manager.SelectedTool = Tool(installed: false);

        manager.BeginTask("Installing t");

        Assert.False(manager.CanInstall);
        Assert.False(manager.CanRecheck);
        Assert.True(manager.IsTaskPanelOpen);
        Assert.Equal("Installing t...", manager.CheckStatus);
    }

    [Fact]
    public void ASuccessfulTaskFoldsThePanelAway()
    {
        var manager = Manager();
        manager.BeginTask("Installing t");

        var token = manager.EndTask("t installed.");
        manager.CollapseAfterSuccess(token);

        Assert.False(manager.IsTaskPanelOpen);
        Assert.True(manager.HasTaskOutput);
        Assert.Equal("SHOW LOG", manager.LogButtonText);
        Assert.Equal("t installed.", manager.CheckStatus);
    }

    [Fact]
    public void ALaterTaskKeepsThePanelOpen()
    {
        // The first task's collapse timer fires while the second is running.
        var manager = Manager();
        manager.BeginTask("Installing a");
        var first = manager.EndTask("a installed.");
        manager.BeginTask("Installing b");

        manager.CollapseAfterSuccess(first);

        Assert.True(manager.IsTaskPanelOpen);
    }

    [Fact]
    public void AFailedTaskLeavesThePanelOpenAndTheButtonsBack()
    {
        // There is no collapse after a failure: the output is what explains it.
        var manager = Manager();
        manager.SelectedTool = Tool(installed: false);
        manager.BeginTask("Installing t");

        manager.EndTask("Installing t failed (exit code 100).");

        Assert.True(manager.IsTaskPanelOpen);
        Assert.True(manager.CanInstall);
        Assert.Equal("HIDE LOG", manager.LogButtonText);
    }

    [Fact]
    public async Task AFailedRecheckSaysSoAndCanBeTriedAgain()
    {
        var manager = Manager();

        await manager.RecheckAsync(_ => throw new IOException("disk gone"));

        Assert.Equal("Checking failed; see the log.", manager.CheckStatus);
        Assert.True(manager.CanRecheck);
    }
}
