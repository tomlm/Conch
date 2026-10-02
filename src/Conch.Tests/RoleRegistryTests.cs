using Conch.Services.Roles;
using Xunit;

namespace Conch.Tests;

public class RoleRegistryTests
{
    /// <summary>A stand-in for an app from the catalog, installed or not.</summary>
    private sealed class FakeProvider : IRoleProvider
    {
        public FakeProvider(string id, bool available = true, bool builtIn = false)
        {
            Id = id;
            Name = id;
            IsAvailable = available;
            IsBuiltIn = builtIn;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsBuiltIn { get; }
        public bool IsAvailable { get; }

        public string? InvokedWith { get; private set; }
        public bool WasInvoked { get; private set; }

        public void Invoke(string? argument)
        {
            WasInvoked = true;
            InvokedWith = argument;
        }
    }

    private static RoleRegistry Build(
        IEnumerable<IRoleProvider>? tools = null,
        IDictionary<string, string>? choices = null)
    {
        return new RoleRegistry(
            _ => tools ?? Array.Empty<IRoleProvider>(),
            role => choices != null && choices.TryGetValue(role, out var id) ? id : null);
    }

    private static RoleRegistry BuildPreferring(string preferredId, params IRoleProvider[] tools)
        => new(_ => tools, _ => null, _ => preferredId);

    [Fact]
    public void ThePreferredDefaultLeadsTheApps()
    {
        // vim comes first in the catalog, but nano is what a Debian user expects to land in.
        var registry = BuildPreferring("gnu.nano",
            new FakeProvider("vim"), new FakeProvider("gnu.nano"), new FakeProvider("edit.net"));

        Assert.Equal(new[] { "gnu.nano", "vim", "edit.net" },
            registry.CandidatesFor(ShellRoles.TextEditor).Select(c => c.Id));
        Assert.Equal("gnu.nano", registry.Resolve(ShellRoles.TextEditor)?.Id);
    }

    [Fact]
    public void APreferredDefaultThatIsNotInstalledFallsBackToOneThatIs()
    {
        // Edit on a Windows machine that has only nano: open nano, rather than nothing.
        var registry = BuildPreferring("edit.exe",
            new FakeProvider("gnu.nano"), new FakeProvider("edit.exe", available: false));

        Assert.Equal("gnu.nano", registry.Resolve(ShellRoles.TextEditor)?.Id);
    }

    [Fact]
    public void AStoredChoiceStillWinsOverThePreferredDefault()
    {
        var registry = new RoleRegistry(
            _ => new IRoleProvider[] { new FakeProvider("gnu.nano"), new FakeProvider("vim") },
            _ => "vim",
            _ => "gnu.nano");

        Assert.Equal("vim", registry.Resolve(ShellRoles.TextEditor)?.Id);
    }

    [Fact]
    public void TheBuiltInStillLeadsAheadOfThePreferredApp()
    {
        var registry = BuildPreferring("ranger", new FakeProvider("nnn"), new FakeProvider("ranger"));
        registry.RegisterBuiltIn(ShellRoles.FileExplorer, new FakeProvider("builtin.files", builtIn: true));

        Assert.Equal(new[] { "builtin.files", "ranger", "nnn" },
            registry.CandidatesFor(ShellRoles.FileExplorer).Select(c => c.Id));
    }

    [Fact]
    public void TheBuiltInLeadsTheList()
    {
        // It is the default and it always works, so it is what someone opening Settings should
        // see first.
        var builtIn = new FakeProvider("builtin.files", builtIn: true);
        var registry = Build(tools: new[] { new FakeProvider("ranger") });
        registry.RegisterBuiltIn(ShellRoles.FileExplorer, builtIn);

        var candidates = registry.CandidatesFor(ShellRoles.FileExplorer);

        Assert.Equal(new[] { "builtin.files", "ranger" }, candidates.Select(c => c.Id));
    }

    [Fact]
    public void AStoredChoiceWinsOverTheBuiltIn()
    {
        var registry = Build(
            tools: new[] { new FakeProvider("ranger") },
            choices: new Dictionary<string, string> { [ShellRoles.FileExplorer] = "ranger" });
        registry.RegisterBuiltIn(ShellRoles.FileExplorer, new FakeProvider("builtin.files", builtIn: true));

        Assert.Equal("ranger", registry.Resolve(ShellRoles.FileExplorer)?.Id);
    }

    [Fact]
    public void AChoiceThatIsNoLongerInstalledFallsBackInsteadOfBreaking()
    {
        // Uninstalling something unrelated should not leave the shell unable to open a folder.
        var registry = Build(
            tools: new[] { new FakeProvider("ranger", available: false) },
            choices: new Dictionary<string, string> { [ShellRoles.FileExplorer] = "ranger" });
        registry.RegisterBuiltIn(ShellRoles.FileExplorer, new FakeProvider("builtin.files", builtIn: true));

        Assert.Equal("builtin.files", registry.Resolve(ShellRoles.FileExplorer)?.Id);
    }

    [Fact]
    public void AChoiceThatLeftTheCatalogAltogetherFallsBackToo()
    {
        // The registration itself is gone, so it is not even a candidate any more.
        var registry = Build(
            tools: Array.Empty<IRoleProvider>(),
            choices: new Dictionary<string, string> { [ShellRoles.FileExplorer] = "some-old-app" });
        registry.RegisterBuiltIn(ShellRoles.FileExplorer, new FakeProvider("builtin.files", builtIn: true));

        Assert.Equal("builtin.files", registry.Resolve(ShellRoles.FileExplorer)?.Id);
    }

    [Fact]
    public void AnUninstalledCandidateIsStillOfferedInSettings()
    {
        // So the list does not silently shrink and leave someone wondering what they had chosen.
        var registry = Build(tools: new[] { new FakeProvider("ranger", available: false) });

        Assert.Contains(registry.CandidatesFor(ShellRoles.FileExplorer), c => c.Id == "ranger");
    }

    [Fact]
    public void ARoleWithNothingToServeItResolvesToNothing()
    {
        // The ordinary state of network configuration on a machine with no network tool.
        var registry = Build();

        Assert.Null(registry.Resolve(ShellRoles.NetworkConfig));
        Assert.False(registry.TryInvoke(ShellRoles.NetworkConfig));
    }

    [Fact]
    public void InvokingARolePassesTheArgumentThrough()
    {
        var builtIn = new FakeProvider("builtin.files", builtIn: true);
        var registry = Build();
        registry.RegisterBuiltIn(ShellRoles.FileExplorer, builtIn);

        Assert.True(registry.TryInvoke(ShellRoles.FileExplorer, @"C:\Users"));

        Assert.True(builtIn.WasInvoked);
        Assert.Equal(@"C:\Users", builtIn.InvokedWith);
    }

    [Fact]
    public void RegisteringTheSameBuiltInTwiceDoesNotDuplicateIt()
    {
        var registry = Build();
        var builtIn = new FakeProvider("builtin.files", builtIn: true);

        registry.RegisterBuiltIn(ShellRoles.FileExplorer, builtIn);
        registry.RegisterBuiltIn(ShellRoles.FileExplorer, builtIn);

        Assert.Single(registry.CandidatesFor(ShellRoles.FileExplorer));
    }

    [Fact]
    public void AnUnknownRoleNameIsToleratedRatherThanThrowing()
    {
        // The catalog refreshes from GitHub, so this build will meet roles that did not exist
        // when it shipped.
        var registry = Build(tools: new[] { new FakeProvider("something") });

        Assert.Equal("something", registry.Resolve("a-role-from-the-future")?.Id);
    }
}
