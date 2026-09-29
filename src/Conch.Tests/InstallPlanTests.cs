using System.Collections.ObjectModel;
using Conch.Services;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

public class InstallPlanTests
{
    private static ToolViewModel Tool(string id, bool installed = false, params string[] requires)
    {
        var tool = new ToolViewModel
        {
            Id = id,
            Name = id,
            Command = id,
            IsInstalled = installed,
            Requires = new ObservableCollection<string>(requires),
        };

        return tool;
    }

    [Fact]
    public void AnAppWithNoRequirementsIsJustItself()
    {
        var app = Tool("btop");

        var plan = InstallPlan.For(app, new[] { app });

        Assert.Equal(new[] { "btop" }, plan.Steps.Select(s => s.Id));
        Assert.Empty(plan.Prerequisites);
    }

    [Fact]
    public void ARequirementIsInstalledBeforeTheAppThatNeedsIt()
    {
        // The order is the whole point: the tool's own install runs `dotnet tool install`,
        // which needs the SDK to already be there.
        var sdk = Tool("dotnet.10");
        var app = Tool("edit.net", requires: "dotnet.10");

        var plan = InstallPlan.For(app, new[] { sdk, app });

        Assert.Equal(new[] { "dotnet.10", "edit.net" }, plan.Steps.Select(s => s.Id));
        Assert.Equal(new[] { "dotnet.10" }, plan.Prerequisites.Select(s => s.Id));
    }

    [Fact]
    public void ARequirementAlreadyInstalledIsNotInstalledAgain()
    {
        // Once a toolchain is on the machine, every later app that needs it should install in
        // one step. Reinstalling 610 MB of SDK per app would be both slow and alarming to watch.
        var sdk = Tool("dotnet.10", installed: true);
        var app = Tool("edit.net", requires: "dotnet.10");

        var plan = InstallPlan.For(app, new[] { sdk, app });

        Assert.Equal(new[] { "edit.net" }, plan.Steps.Select(s => s.Id));
        Assert.Empty(plan.Prerequisites);
    }

    [Fact]
    public void RequirementsOfRequirementsComeFirst()
    {
        var deep = Tool("base");
        var middle = Tool("sdk", requires: "base");
        var app = Tool("app", requires: "sdk");

        var plan = InstallPlan.For(app, new[] { deep, middle, app });

        Assert.Equal(new[] { "base", "sdk", "app" }, plan.Steps.Select(s => s.Id));
    }

    [Fact]
    public void AnAppAlreadyInstalledIsStillReinstalledWhenAsked()
    {
        // Asking to install something is a request to install it. Refusing because detection
        // says it is present would take away the only way to repair a broken install.
        var app = Tool("btop", installed: true);

        var plan = InstallPlan.For(app, new[] { app });

        Assert.Equal(new[] { "btop" }, plan.Steps.Select(s => s.Id));
    }

    [Fact]
    public void ARequirementNamingNothingIsReportedRatherThanIgnored()
    {
        // The catalog refreshes from GitHub, so a registration can name a prerequisite that
        // this build has never seen. Saying which one beats failing partway through an install
        // with a package manager error that names nothing recognisable.
        var app = Tool("edit.net", requires: "dotnet.11");

        var plan = InstallPlan.For(app, new[] { app });

        Assert.True(plan.HasUnknownRequirements);
        Assert.Equal(new[] { "dotnet.11" }, plan.Missing);
    }

    [Fact]
    public void ACycleInTheCatalogDoesNotHang()
    {
        // A mistake in the catalog, not something the user can act on. Refusing to install
        // would turn one bad edit into a broken app for everybody.
        var a = Tool("a", requires: "b");
        var b = Tool("b", requires: "a");

        var plan = InstallPlan.For(a, new[] { a, b });

        Assert.Contains(plan.Steps, s => s.Id == "a");
        Assert.Equal(plan.Steps.Count, plan.Steps.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void ARequirementSharedByTwoPathsIsInstalledOnce()
    {
        var sdk = Tool("sdk");
        var lib = Tool("lib", requires: "sdk");
        var app = Tool("app", requires: new[] { "sdk", "lib" });

        var plan = InstallPlan.For(app, new[] { sdk, lib, app });

        Assert.Equal(new[] { "sdk", "lib", "app" }, plan.Steps.Select(s => s.Id));
    }

    [Fact]
    public void RequirementIdsAreMatchedWithoutRegardToCase()
    {
        var sdk = Tool("dotnet.10");
        var app = Tool("edit.net", requires: "DOTNET.10");

        var plan = InstallPlan.For(app, new[] { sdk, app });

        Assert.False(plan.HasUnknownRequirements);
        Assert.Equal(new[] { "dotnet.10", "edit.net" }, plan.Steps.Select(s => s.Id));
    }
}
