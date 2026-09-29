using Conch.Services.Roles;
using Conch.Utilities;
using Conch.ViewModel;
using Xunit;
using YamlConverter;

namespace Conch.Tests;

/// <summary>
/// Checks the registrations that actually ship with the package.
/// </summary>
/// <remarks>
/// The seed catalog is the app list a machine gets with no network, so a malformed file is a
/// shipped defect rather than a content problem. These read the same files the build copies.
/// </remarks>
public class RegistrationCatalogTests
{
    private static string ToolsDirectory => Path.Combine(AppContext.BaseDirectory, "Tools");

    public static TheoryData<string> RegistrationFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(ToolsDirectory, "*.yml").OrderBy(f => f))
        {
            data.Add(Path.GetFileName(file));
        }
        return data;
    }

    private static ToolViewModel Load(string fileName)
        => YamlConvert.DeserializeObject<ToolViewModel>(File.ReadAllText(Path.Combine(ToolsDirectory, fileName)));

    [Fact]
    public void TheSeedCatalogIsNotEmpty()
    {
        // Guards the build wiring, not the content: without the csproj Content item the
        // registrations silently stop shipping and the app manager comes up blank.
        Assert.True(Directory.Exists(ToolsDirectory), $"missing {ToolsDirectory}");
        Assert.NotEmpty(Directory.GetFiles(ToolsDirectory, "*.yml"));
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void RegistrationDeserializesAndValidates(string fileName)
    {
        var tool = Load(fileName);

        tool.Validate();

        Assert.False(string.IsNullOrWhiteSpace(tool.Id));
        Assert.False(string.IsNullOrWhiteSpace(tool.Command));
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void RegistrationResolvesToSomethingOnAtLeastOnePlatform(string fileName)
    {
        var tool = Load(fileName);

        var platforms = new[] { HostOs.Windows, HostOs.Linux, HostOs.MacOS }
            .Where(os => tool.GetInstallDefinitionFor(os) != null)
            .ToList();

        Assert.NotEmpty(platforms);
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void EveryResolvableEntryHasBothInstallAndUninstall(string fileName)
    {
        var tool = Load(fileName);

        foreach (var os in new[] { HostOs.Windows, HostOs.Linux, HostOs.MacOS })
        {
            var def = tool.GetInstallDefinitionFor(os);
            if (def == null)
            {
                continue;
            }

            // An app that can be installed but not removed is a one-way door on an appliance.
            Assert.False(string.IsNullOrWhiteSpace(def.Install), $"{fileName} on {os} has no install");
            Assert.False(string.IsNullOrWhiteSpace(def.Uninstall), $"{fileName} on {os} has no uninstall");
        }
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void ArgumentTemplateIsWellFormed(string fileName)
    {
        var tool = Load(fileName);

        // Supplying nothing must either succeed or fail cleanly, never throw.
        var ok = ArgumentTemplate.TryBuild(tool.Args, Array.Empty<string>(), out var args, out var error);

        if (!ok)
        {
            Assert.Contains("required", error);
            return;
        }

        Assert.DoesNotContain(args, a => a.Contains('%'));
    }

    [Fact]
    public void RegistrationIdsAreUnique()
    {
        // The catalog upserts by id; duplicates would silently shadow each other.
        var ids = Directory.GetFiles(ToolsDirectory, "*.yml")
            .Select(f => Load(Path.GetFileName(f)).Id)
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void LinuxEntriesDoNotUseWindowsPackageManagers(string fileName)
    {
        var tool = Load(fileName);
        var linux = tool.Platforms?.Linux;
        if (linux == null)
        {
            return;
        }

        Assert.DoesNotContain("winget", linux.Install, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("winget", linux.Uninstall, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void WindowsEntriesDoNotUseLinuxPackageManagers(string fileName)
    {
        var tool = Load(fileName);
        var windows = tool.Platforms?.Windows;
        if (windows == null)
        {
            return;
        }

        Assert.DoesNotContain("apt-get", windows.Install, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apt-get", windows.Uninstall, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void RegistrationAsksForAGridEveryTuiWillStartIn(string fileName)
    {
        // 80x24 is the floor several full-screen TUIs enforce rather than adapt to: btop
        // prints "Terminal size too small" and exits at 79 columns or 23 rows, and it is
        // the size the whole genre was written against. A registration asking for less
        // gives that app a window it refuses to run in.
        //
        // These are grid, not window. ManagedTerminalWindow sizes the window around them,
        // so what a registration asks for is what the process is handed -- the earlier
        // arrangement passed them as window Width and Height, and the border and title bar
        // came out of the app's share.
        var tool = Load(fileName);

        Assert.True(tool.Cols >= 80, $"{fileName} asks for {tool.Cols} columns; 80 is the floor.");
        Assert.True(tool.Rows >= 24, $"{fileName} asks for {tool.Rows} rows; 24 is the floor.");
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void DeclaredRolesAreOnesTheShellKnows(string fileName)
    {
        // A role is a slot the shell opens itself, so a name it does not recognise is a slot
        // nothing will ever fill -- almost always a typo. This is a catalog check rather than
        // a validation rule on purpose: ToolViewModel deliberately accepts unknown roles, so
        // that publishing a new one does not break older builds reading the same feed. The
        // shipped files are the place to be strict.
        var tool = Load(fileName);

        foreach (var role in tool.Roles)
        {
            Assert.True(ShellRoles.IsKnown(role), $"{fileName} declares unknown role '{role}'.");
        }
    }

    [Fact]
    public void EveryRoleTheShellOpensHasSomethingThatCanFillIt()
    {
        // Not every role needs a candidate -- network-config and audio-config have none, and
        // built-ins cover the launcher, manager and file explorer. But text-editor and
        // system-monitor are invoked by the shell with no built-in behind them, so if the
        // catalog stops offering one the feature silently stops working.
        var declared = Directory.GetFiles(ToolsDirectory, "*.yml")
            .SelectMany(f => Load(Path.GetFileName(f)).Roles)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(ShellRoles.TextEditor, declared);
        Assert.Contains(ShellRoles.SystemMonitor, declared);
        Assert.Contains(ShellRoles.FileExplorer, declared);
    }

    [Fact]
    public void GridDefaultsToTheClassicTerminalSize()
    {
        var tool = new ToolViewModel();

        Assert.Equal(80, tool.Cols);
        Assert.Equal(25, tool.Rows);
    }

    [Theory]
    [MemberData(nameof(RegistrationFiles))]
    public void DeclaredRequirementsExistInTheCatalog(string fileName)
    {
        // A shipped registration naming a prerequisite that is not there is a broken install
        // waiting to happen, and the failure would appear halfway through as a package manager
        // error naming nothing recognisable. Unknown ids are tolerated at runtime -- the
        // catalog refreshes from GitHub and can be ahead of the build -- so the seed files are
        // where this is worth being strict.
        var ids = Directory.GetFiles(ToolsDirectory, "*.yml")
            .Select(f => Load(Path.GetFileName(f)).Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var required in Load(fileName).Requires)
        {
            Assert.True(ids.Contains(required), $"{fileName} requires '{required}', which no registration provides.");
        }
    }

    [Fact]
    public void ADotnetToolDeclaresWhichDotnetItNeeds()
    {
        // A global tool is framework-dependent and .NET's default roll-forward is Minor, which
        // does not cross majors: a net8.0 tool on a machine with only the 10.x runtime does not
        // run, it errors. So "requires: dotnet" would be a lie -- the major version is part of
        // the requirement.
        var editNet = Load("Edit.NET.yml");

        Assert.Contains(editNet.Requires, r => r.StartsWith("dotnet.", StringComparison.OrdinalIgnoreCase));
    }
}
