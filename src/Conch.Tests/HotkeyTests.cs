using Avalonia.Input;
using Conch.Services;
using Xunit;

namespace Conch.Tests;

/// <summary>The shell's key bindings: defaults, what a press triggers, and clashes.</summary>
public class HotkeyTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "conch-hotkeys-" + Guid.NewGuid().ToString("n"));

    public HotkeyTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static HotkeyMap Map(Dictionary<string, string?>? stored = null)
        => new(id => stored != null && stored.TryGetValue(id, out var g) ? g : null);

    [Fact]
    public void EveryDefaultParsesAndNoTwoCollide()
    {
        // A default that will not parse is an action with no key; two equal defaults leave one
        // of them unreachable.
        var gestures = Hotkeys.Actions.SelectMany(a => Hotkeys.ParseList(a.Default)).ToList();
        var written = Hotkeys.Actions.Sum(a => a.Default.Split(',').Length);

        Assert.Equal(written, gestures.Count);
        Assert.Equal(gestures.Count, gestures.Select(g => (g.Key, g.KeyModifiers)).Distinct().Count());
    }

    [Fact]
    public void TheDefaultsAvoidTheSuperKey()
    {
        // Windows Terminal and desktops keep Super for themselves, so a Super default would be
        // a hotkey that never arrives.
        Assert.All(Hotkeys.Actions.SelectMany(a => Hotkeys.ParseList(a.Default)), g =>
            Assert.False(g.KeyModifiers.HasFlag(KeyModifiers.Meta)));
    }

    [Fact]
    public void TheDefaultsAvoidAltF4()
    {
        // Windows closes the whole terminal on Alt+F4 before Conch ever sees it.
        Assert.DoesNotContain(Hotkeys.Actions, a => a.Default == "Alt+F4");
    }

    [Fact]
    public void APressTriggersItsAction()
    {
        Assert.Equal(Hotkeys.FocusSearch, Map().ActionFor(Key.F2, KeyModifiers.Alt));
        Assert.Equal(Hotkeys.FocusSearch, Map().ActionFor(Key.Space, KeyModifiers.Control));
        Assert.Equal(Hotkeys.NewTerminal, Map().ActionFor(Key.T, KeyModifiers.Control | KeyModifiers.Alt));
    }

    [Fact]
    public void APressWithOtherModifiersIsNotTheSameCombination()
    {
        Assert.Null(Map().ActionFor(Key.F2, KeyModifiers.Alt | KeyModifiers.Shift));
        Assert.Null(Map().ActionFor(Key.F2, KeyModifiers.None));
    }

    [Fact]
    public void AStoredBindingReplacesTheDefault()
    {
        var map = Map(new() { [Hotkeys.FocusSearch] = "Ctrl+F12" });

        Assert.Equal(Hotkeys.FocusSearch, map.ActionFor(Key.F12, KeyModifiers.Control));
        Assert.Null(map.ActionFor(Key.F2, KeyModifiers.Alt));
        Assert.Null(map.ActionFor(Key.Space, KeyModifiers.Control));
    }

    [Fact]
    public void AClearedBindingTriggersNothing()
    {
        // Empty is "the user cleared it", which must not fall back to the default.
        var map = Map(new() { [Hotkeys.MaximizeWindow] = "" });

        Assert.Empty(map.GesturesFor(Hotkeys.MaximizeWindow));
        Assert.Null(map.ActionFor(Key.F10, KeyModifiers.Control));
    }

    [Fact]
    public void BindingAnActionToAnotherActionsKeysNamesTheClash()
    {
        var clash = Map().ConflictFor(Hotkeys.Files, Hotkeys.Parse("Ctrl+F10")!);

        Assert.Equal(Hotkeys.MaximizeWindow, clash?.Id);
    }

    [Fact]
    public void RebindingAnActionToItsOwnKeysIsNotAClash()
    {
        Assert.Null(Map().ConflictFor(Hotkeys.MaximizeWindow, Hotkeys.Parse("Ctrl+F10")!));
    }

    [Theory]
    [InlineData("Ctrl+F4")]
    [InlineData("Ctrl+F6")]
    [InlineData("Ctrl+Tab")]
    [InlineData("Alt+OemMinus")]
    public void TheWindowManagersKeysAreReserved(string text)
    {
        // Its key bindings run before any handler sees the press, so binding one of these
        // would never reach the shell.
        Assert.NotNull(Hotkeys.ReservedFor(Hotkeys.Parse(text)!));
    }

    [Fact]
    public void NoDefaultIsAWindowManagerKey()
    {
        Assert.All(Hotkeys.Actions.SelectMany(a => Hotkeys.ParseList(a.Default)), g => Assert.Null(Hotkeys.ReservedFor(g)));
    }

    [Fact]
    public void AStoredListBindsEveryCombinationInIt()
    {
        var map = Map(new() { [Hotkeys.Files] = "Ctrl+Alt+E, Alt+F3" });

        Assert.Equal(Hotkeys.Files, map.ActionFor(Key.E, KeyModifiers.Control | KeyModifiers.Alt));
        Assert.Equal(Hotkeys.Files, map.ActionFor(Key.F3, KeyModifiers.Alt));
    }

    [Fact]
    public void ListsRoundTrip()
    {
        Assert.Equal("Alt+F2, Ctrl+Space", Hotkeys.FormatList(Hotkeys.ParseList("Alt+F2,Ctrl+Space")));
        Assert.Empty(Hotkeys.ParseList(""));
    }

    [Fact]
    public void SuperCombinationsCanBeBound()
    {
        var gesture = Hotkeys.Parse("Super+A");

        Assert.NotNull(gesture);
        Assert.Equal(KeyModifiers.Meta, gesture!.KeyModifiers);
        Assert.Equal("Super+A", Hotkeys.Format(gesture));
    }

    [Theory]
    [InlineData("Ctrl+Alt+T")]
    [InlineData("Alt+F2")]
    [InlineData("Ctrl+Shift+F4")]
    [InlineData("Ctrl+Space")]
    [InlineData("Alt+1")]
    public void FormattingIsWhatWasParsed(string text)
    {
        // Stored text round-trips, so settings.json reads the way Preferences shows it.
        Assert.Equal(text, Hotkeys.Format(Hotkeys.Parse(text)));
    }

    [Fact]
    public void EveryDefaultIsStoredInItsCanonicalForm()
    {
        Assert.All(Hotkeys.Actions, a => Assert.Equal(a.Default, Hotkeys.FormatList(Hotkeys.ParseList(a.Default))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Alt+NotAKey")]
    public void TextThatIsNotACombinationParsesToNothing(string? text)
    {
        Assert.Null(Hotkeys.Parse(text));
    }

    [Fact]
    public void AModifierAloneIsNotACombination()
    {
        // Recording a binding sees Alt go down before F2; that first press must not be kept.
        Assert.Null(Hotkeys.FromKeyPress(Key.LeftAlt, KeyModifiers.Alt));
        Assert.NotNull(Hotkeys.FromKeyPress(Key.F2, KeyModifiers.Alt));
    }

    [Fact]
    public void BindingsAndStatusAsTextSurviveARestart()
    {
        var first = new ShellSettings(_directory).Load();
        first.SetHotkey(Hotkeys.FocusSearch, "Ctrl+Space");
        first.SetHotkey(Hotkeys.MaximizeWindow, "");
        first.StatusAsText = true;

        var second = new ShellSettings(_directory).Load();

        Assert.Equal("Ctrl+Space", second.GetHotkey(Hotkeys.FocusSearch));
        Assert.Equal("", second.GetHotkey(Hotkeys.MaximizeWindow));
        Assert.Null(second.GetHotkey(Hotkeys.Files));
        Assert.True(second.StatusAsText);
    }

    [Fact]
    public void AVersionOneFileLoadsWithTheNewDefaults()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"),
            """{ "version": 1, "theme": "TurboVision", "roles": { "file-explorer": "ranger" } }""");

        var settings = new ShellSettings(_directory).Load();

        Assert.Equal("TurboVision", settings.Theme);
        Assert.False(settings.StatusAsText);
        Assert.Null(settings.GetHotkey(Hotkeys.FocusSearch));
    }
}
