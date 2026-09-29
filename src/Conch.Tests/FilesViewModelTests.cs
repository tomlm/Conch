using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

public class FilesViewModelTests : IDisposable
{
    private readonly string _root;

    public FilesViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "conch-files-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(_root, "zebra"));
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(Path.Combine(_root, "notes.txt"), new string('x', 2048));
        File.WriteAllText(Path.Combine(_root, "a-file.md"), "hi");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static IEnumerable<string> Names(FilesViewModel vm)
        => vm.Entries.Where(e => !e.IsParent).Select(e => e.Name);

    [Fact]
    public void FoldersComeBeforeFilesAndEachIsSortedByName()
    {
        // What every file manager does, and what makes a long listing navigable by eye.
        var vm = new FilesViewModel(_root);

        Assert.Equal(new[] { "alpha", "zebra", "a-file.md", "notes.txt" }, Names(vm));
    }

    [Fact]
    public void AFolderIsMarkedWithATrailingSlash()
    {
        // Cheaper than a column, and it reads at a glance.
        var vm = new FilesViewModel(_root);

        Assert.Equal("alpha/", vm.Entries.First(e => e.Name == "alpha").DisplayName);
        Assert.Equal("notes.txt", vm.Entries.First(e => e.Name == "notes.txt").DisplayName);
    }

    [Fact]
    public void ThereIsAWayBackUp()
    {
        var vm = new FilesViewModel(_root);

        var parent = vm.Entries.FirstOrDefault(e => e.IsParent);

        Assert.NotNull(parent);
        Assert.Equal("..", parent.Name);
        Assert.True(parent.IsDirectory);
    }

    [Fact]
    public void GoingUpLandsInTheParent()
    {
        var vm = new FilesViewModel(Path.Combine(_root, "alpha"));

        vm.GoUp();

        Assert.Equal(_root, vm.CurrentPath.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void NavigatingIntoAFolderShowsIt()
    {
        var vm = new FilesViewModel(_root);

        vm.Navigate(Path.Combine(_root, "alpha"));

        Assert.Equal(Path.Combine(_root, "alpha"), vm.CurrentPath);
        Assert.Empty(Names(vm));
    }

    [Fact]
    public void AnEmptyFolderSaysSoRatherThanLookingBroken()
    {
        // An empty pane cannot distinguish "nothing here" from "could not read it", and those
        // want different reactions from the person looking at it.
        var vm = new FilesViewModel(Path.Combine(_root, "alpha"));

        Assert.Equal("This folder is empty.", vm.Problem);
    }

    [Fact]
    public void AFolderThatIsNotThereSaysSoInsteadOfThrowing()
    {
        var vm = new FilesViewModel(_root);

        vm.Navigate(Path.Combine(_root, "no-such-folder"));

        Assert.NotNull(vm.Problem);
        Assert.Equal(_root, vm.CurrentPath);
    }

    [Fact]
    public void AListingThatWorkedLeavesNoProblemBehind()
    {
        var vm = new FilesViewModel(_root);

        vm.Navigate(Path.Combine(_root, "alpha"));
        vm.Navigate(_root);

        Assert.Null(vm.Problem);
    }

    [Fact]
    public void StartingSomewhereThatDoesNotExistFallsBackToARoot()
    {
        // The stored path from a previous session, on a drive that is no longer plugged in.
        var vm = new FilesViewModel(Path.Combine(_root, "nowhere"));

        Assert.NotEmpty(vm.CurrentPath);
        Assert.True(Directory.Exists(vm.CurrentPath));
    }

    [Theory]
    [InlineData(0, "0B")]
    [InlineData(512, "512B")]
    [InlineData(2048, "2K")]
    [InlineData(1536, "1.5K")]
    [InlineData(5 * 1024 * 1024, "5M")]
    public void SizesAreWrittenForPeopleRatherThanMachines(long bytes, string expected)
    {
        var entry = new FileEntryViewModel("/x", "x", isDirectory: false, bytes, DateTime.Now);

        Assert.Equal(expected, entry.SizeText);
    }

    [Fact]
    public void AFolderShowsNoSize()
    {
        // The number would be the directory entry's own size, which is not what anyone reads
        // it as.
        var entry = new FileEntryViewModel("/x", "x", isDirectory: true, 4096, DateTime.Now);

        Assert.Equal(string.Empty, entry.SizeText);
    }

    [Fact]
    public void SomewhereToStartIsAlwaysOffered()
    {
        var vm = new FilesViewModel(_root);

        Assert.NotEmpty(vm.Roots);
    }
}
