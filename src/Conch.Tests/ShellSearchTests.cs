using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

/// <summary>What the top bar's search box offers for what is typed, and in what order.</summary>
public class ShellSearchTests
{
    private static SearchItem Item(SearchItemKind kind, string title, string detail = "", params string[] terms)
        => new(kind, title, detail, terms, () => { });

    private static readonly SearchItem[] Fixed =
    [
        Item(SearchItemKind.Shell, "Files", "Browse folders", "explorer"),
        Item(SearchItemKind.Shell, "Terminal", "A shell", "bash", "console"),
        Item(SearchItemKind.Setting, "Network settings", "Wi-Fi and connections", "wifi"),
        Item(SearchItemKind.Setting, "Display settings", "Font size and resolution"),
        Item(SearchItemKind.Command, "Logout", "End this session"),
    ];

    private static readonly SearchItem[] Apps =
    [
        Item(SearchItemKind.App, "nano", "Simple text editor", "gnu.nano"),
        Item(SearchItemKind.App, "Neovim", "Vim-based editor", "nvim"),
        Item(SearchItemKind.App, "btop", "Resource monitor", "system monitor"),
        Item(SearchItemKind.App, "lazygit", "Terminal UI for git"),
    ];

    private static ShellSearchViewModel Search(Func<string, Action>? run = null)
        => new(() => Fixed, () => Apps, run ?? (_ => () => { }));

    private static IEnumerable<string> Titles(IEnumerable<SearchItem> items) => items.Select(i => i.Title);

    [Fact]
    public void AnEmptyQueryShowsTheShellsAppsThenEveryInstalledAppAToZ()
    {
        // What the Apps window used to show, without having to know what to type.
        Assert.Equal(new[] { "Files", "Terminal", "btop", "lazygit", "nano", "Neovim" },
            Titles(Search().Search("")));
    }

    [Fact]
    public void ANameThatStartsWithTheQueryComesFirst()
    {
        var titles = Titles(Search().Search("n")).ToList();

        // All three start with "n"; apps rank ahead of settings that match as well.
        Assert.Equal(new[] { "nano", "Neovim", "Network settings" }, titles.Take(3));
    }

    [Fact]
    public void AppsComeBeforeSettingsWhenTheyMatchAsWell()
    {
        var titles = Titles(Search().Search("ne")).ToList();

        Assert.True(titles.IndexOf("Neovim") < titles.IndexOf("Network settings"));
    }

    [Fact]
    public void AWordInsideTheNameStillFindsIt()
    {
        Assert.Equal("Display settings", Search().Search("settings").First().Title);
    }

    [Fact]
    public void DescriptionsIdsAndKeywordsFindAnAppToo()
    {
        Assert.Contains("btop", Titles(Search().Search("monitor")));
        Assert.Contains("Network settings", Titles(Search().Search("wifi")));
        Assert.Contains("Neovim", Titles(Search().Search("nvim")));
    }

    [Fact]
    public void AKeywordOutranksAWordThatOnlyAppearsInADescription()
    {
        var search = new ShellSearchViewModel(
            () => [Item(SearchItemKind.Setting, "Conch Preferences", "Default apps", "keyboard")],
            () => [Item(SearchItemKind.App, "WeeChat", "Chat client driven entirely from the keyboard")],
            _ => () => { });

        Assert.Equal("Conch Preferences", search.Search("keyb").First().Title);
    }

    [Fact]
    public void AShortQueryDoesNotMatchTheMiddleOfWords()
    {
        // "na" is nano, not Termi-na-l, and not every description with "na" somewhere in it.
        var titles = Titles(Search().Search("na")).ToList();

        Assert.Equal(new[] { "nano", "Run \"na\"" }, titles);
    }

    [Fact]
    public void ALongerQueryCanMatchInsideATitle()
    {
        Assert.Contains("lazygit", Titles(Search().Search("git")));
    }

    [Fact]
    public void ACommandLineThatMatchesNothingIsOfferedToRun()
    {
        string? ran = null;
        var results = Search(text => () => ran = text).Search("ls -la");

        var run = Assert.Single(results);
        Assert.Equal(SearchItemKind.Run, run.Kind);
        Assert.Equal("Run \"ls -la\"", run.Title);

        run.Invoke();
        Assert.Equal("ls -la", ran);
    }

    [Fact]
    public void RunIsLastWhenOtherThingsMatch()
    {
        var results = Search().Search("ter");

        Assert.Equal("Terminal", results.First().Title);
        Assert.Equal(SearchItemKind.Run, results.Last().Kind);
    }

    [Fact]
    public void AnExactNameIsNotOfferedAsACommandLineAsWell()
    {
        // "nano" is the app; running the text "nano" in a terminal would be a second,
        // worse way to the same thing.
        Assert.DoesNotContain(Search().Search("nano"), i => i.Kind == SearchItemKind.Run);
    }

    [Fact]
    public void TheFirstResultIsSelectedSoEnterRunsIt()
    {
        var search = Search();

        search.Query = "laz";

        Assert.Equal("lazygit", search.SelectedResult?.Title);
    }

    [Fact]
    public void TypingDoesNotRereadTheCatalog()
    {
        // Building the candidates walks the whole catalog; a keystroke should only compare.
        var reads = 0;
        var search = new ShellSearchViewModel(() => Fixed, () => { reads++; return Apps; }, _ => () => { });

        foreach (var query in new[] { "n", "na", "nan", "nano", "" })
        {
            search.Query = query;
        }

        Assert.Equal(1, reads);
    }

    [Fact]
    public void ReloadSeesAnAppInstalledSince()
    {
        var installed = Apps.ToList();
        var search = new ShellSearchViewModel(() => Fixed, () => installed, _ => () => { });
        Assert.DoesNotContain("htop", Titles(search.Search("htop")));

        installed.Add(Item(SearchItemKind.App, "htop", "Process viewer"));
        search.Reload();

        Assert.Equal("htop", search.Search("htop").First().Title);
    }

    private static SearchItem App(string title, string id) => Item(SearchItemKind.App, title) with { Id = id };

    [Fact]
    public void PinsComeFirstInTheOrderTheyWerePinnedAndOnlyOnce()
    {
        var apps = new[] { App("btop", "btop"), App("lazygit", "lazygit"), App("nano", "gnu.nano") };
        var search = new ShellSearchViewModel(() => Fixed, () => apps, _ => () => { }, () => ["gnu.nano", "btop"]);

        var titles = Titles(search.Search("")).ToList();

        Assert.Equal(new[] { "nano", "btop", "Files", "Terminal", "lazygit" }, titles);
    }

    [Fact]
    public void APinnedResultWinsATie()
    {
        var apps = new[] { App("nano", "gnu.nano"), App("neovim", "neovim") };
        var search = new ShellSearchViewModel(() => [], () => apps, _ => () => { }, () => ["neovim"]);

        Assert.Equal("neovim", search.Search("n").First().Title);
    }

    [Fact]
    public void PinsAreTaggedAndOfferUnpin()
    {
        var search = new ShellSearchViewModel(() => [], () => [App("btop", "btop")], _ => () => { }, () => ["btop"]);

        var btop = search.Search("").Single();

        Assert.Equal(("pinned", "Unpin"), (btop.KindLabel, btop.PinLabel));
    }

    [Fact]
    public void AnItemWithNoIdCannotBePinned()
    {
        Assert.False(Item(SearchItemKind.Command, "Logout").CanPin);
    }

    [Fact]
    public void APinForSomethingNotInstalledIsSimplyNotShown()
    {
        var search = new ShellSearchViewModel(() => [], () => [App("btop", "btop")], _ => () => { }, () => ["gone.app", "btop"]);

        Assert.Equal(new[] { "btop" }, Titles(search.Search("")));
    }

    [Fact]
    public void PinsSurviveARestartInOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "conch-pins-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new Conch.Services.ShellSettings(directory).Load();
            settings.SetPinned("btop", true);
            settings.SetPinned("gnu.nano", true);
            settings.SetPinned("btop", false);
            settings.SetPinned("btop", true);

            Assert.Equal(new[] { "gnu.nano", "btop" }, new Conch.Services.ShellSettings(directory).Load().Pinned);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ARefreshThatChangesNothingLeavesTheSelectionAlone()
    {
        // Detection reports in every second or so during a catalog refresh; the arrow keys
        // must not be undone by it.
        var search = Search();
        search.Refresh();
        search.SelectedResult = search.Results[3];

        search.Reload();
        search.Refresh();

        Assert.Same(search.Results[3], search.SelectedResult);
    }

    [Fact]
    public void AnAppArrivingKeepsTheSelectionOnTheSameItem()
    {
        var installed = Apps.ToList();
        var search = new ShellSearchViewModel(() => Fixed, () => installed, _ => () => { });
        search.Refresh();
        search.SelectedResult = search.Results.Single(r => r.Title == "nano");

        installed.Add(Item(SearchItemKind.App, "aaa-first", "Sorts to the top"));
        search.Reload();
        search.Refresh();

        Assert.Equal("nano", search.SelectedResult?.Title);
        Assert.Contains("aaa-first", Titles(search.Results));
    }

    [Fact]
    public void ANewQueryStartsAtItsBestMatch()
    {
        var search = Search();
        search.Refresh();
        search.SelectedResult = search.Results.Last();

        search.Query = "laz";

        Assert.Equal("lazygit", search.SelectedResult?.Title);
    }

    [Fact]
    public void ResultsStopAtTheLimit()
    {
        var many = Enumerable.Range(0, 200).Select(i => Item(SearchItemKind.App, $"app{i:000}")).ToArray();
        var search = new ShellSearchViewModel(() => Fixed, () => many, _ => () => { });

        Assert.Equal(ShellSearchViewModel.MaxResults, search.Search("app").Count);
        Assert.Equal(SearchItemKind.Run, search.Search("app").Last().Kind);
    }
}
