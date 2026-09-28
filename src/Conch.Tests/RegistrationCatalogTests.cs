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
}
