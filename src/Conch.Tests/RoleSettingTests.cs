using Conch.Services.Roles;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

public class RoleSettingTests
{
    private sealed record FakeProvider(string Id, string Name, bool IsBuiltIn, bool IsAvailable)
        : IRoleProvider
    {
        public void Invoke(string? argument) { }
    }

    private static FakeProvider BuiltIn(string id) => new(id, id, IsBuiltIn: true, IsAvailable: true);
    private static FakeProvider App(string id, bool installed = true) => new(id, id, IsBuiltIn: false, installed);

    [Fact]
    public void PickingAnAppRecordsTheChoice()
    {
        var saved = new List<(string Role, string? Id)>();
        var setting = new RoleSettingViewModel(
            ShellRoles.FileExplorer,
            new IRoleProvider[] { BuiltIn("builtin.files"), App("ranger") },
            chosenId: null,
            (role, id) => saved.Add((role, id)));

        setting.Selected = setting.Choices.Single(c => c.Id == "ranger");

        Assert.Equal((ShellRoles.FileExplorer, "ranger"), saved.Single());
    }

    [Fact]
    public void BuildingTheListDoesNotWriteAChoiceNobodyMade()
    {
        // Selecting the current value while populating would turn a default that tracks what
        // is installed into a pin that no longer moves -- so the first app to claim a role
        // would stay claimed even after something better was installed.
        var saved = new List<(string Role, string? Id)>();

        _ = new RoleSettingViewModel(
            ShellRoles.TextEditor,
            new IRoleProvider[] { App("vim") },
            chosenId: null,
            (role, id) => saved.Add((role, id)));

        Assert.Empty(saved);
    }

    [Fact]
    public void AStoredChoiceComesBackSelected()
    {
        var setting = new RoleSettingViewModel(
            ShellRoles.TextEditor,
            new IRoleProvider[] { App("vim"), App("nano") },
            chosenId: "nano",
            (_, _) => { });

        Assert.Equal("nano", setting.Selected?.Id);
    }

    [Fact]
    public void AChoiceThatIsGoneFallsBackToTheFirstCandidate()
    {
        var setting = new RoleSettingViewModel(
            ShellRoles.TextEditor,
            new IRoleProvider[] { App("vim") },
            chosenId: "an-app-that-left",
            (_, _) => { });

        Assert.Equal("vim", setting.Selected?.Id);
    }

    [Fact]
    public void ARoleWithNoCandidatesSaysSoRatherThanShowingAnEmptyList()
    {
        // The state most roles are in on a fresh machine. An empty dropdown reads as broken.
        var setting = new RoleSettingViewModel(
            ShellRoles.NetworkConfig, Array.Empty<IRoleProvider>(), null, (_, _) => { });

        Assert.True(setting.IsEmpty);
        Assert.Null(setting.Selected);
    }

    [Theory]
    [InlineData(true, true, " (built in)")]
    [InlineData(false, false, " (not installed)")]
    [InlineData(false, true, "")]
    public void ALabelSaysWhyAnEntryIsListedButWillNotRun(bool builtIn, bool available, string suffix)
    {
        var choice = new RoleChoiceViewModel(new FakeProvider("thing", "Thing", builtIn, available));

        Assert.Equal("Thing" + suffix, choice.Label);
    }

    [Fact]
    public void EveryKnownRoleGetsARow()
    {
        // Including the ones nothing can serve yet: the empty slot is how someone learns the
        // role exists and that installing something would fill it.
        var registry = new RoleRegistry(_ => Array.Empty<IRoleProvider>(), _ => null);
        var app = new AppViewModel(new Conch.Services.ShellSettings(Path.GetTempPath()));

        var settings = new SettingsViewModel(app, registry);

        Assert.Equal(ShellRoles.All.Count, settings.Roles.Count);
        Assert.Equal(ShellRoles.All, settings.Roles.Select(r => r.Role).ToList());
    }

    [Fact]
    public void TheDefaultThemeIsOneSettingsOffers()
    {
        // Otherwise the dropdown opens on a blank row for anyone who has never chosen.
        Assert.Contains(AppViewModel.DefaultTheme, SettingsViewModel.ThemeNames);
    }

    [Fact]
    public void WithNothingStoredTheShownChoiceIsOneThatCouldActuallyRun()
    {
        // A built-in can be unavailable: the Network window needs NetworkManager, which Windows
        // and macOS do not have. Showing it as the current setting would name something that
        // cannot run while something else quietly did the job -- so Settings agrees with
        // RoleRegistry.Resolve and shows the first available candidate instead.
        var unavailableBuiltIn = new FakeProvider(
            "builtin.network", "Network", IsBuiltIn: true, IsAvailable: false);

        var setting = new RoleSettingViewModel(
            ShellRoles.NetworkConfig,
            new IRoleProvider[] { unavailableBuiltIn, App("nmtui") },
            chosenId: null,
            (_, _) => { });

        Assert.Equal("nmtui", setting.Selected?.Id);

        // Still listed, and still saying why it is not the one.
        Assert.Contains(setting.Choices, c => c.Label == "Network (not available here)");
    }

    [Fact]
    public void AnUnavailableBuiltInIsStillShownWhenItIsTheOnlyCandidate()
    {
        // Falling back to nothing selected would leave the dropdown blank, which says less than
        // naming the thing and why it cannot run.
        var setting = new RoleSettingViewModel(
            ShellRoles.NetworkConfig,
            new IRoleProvider[]
            {
                new FakeProvider("builtin.network", "Network", IsBuiltIn: true, IsAvailable: false),
            },
            chosenId: null,
            (_, _) => { });

        Assert.Equal("builtin.network", setting.Selected?.Id);
    }
}
