using Conch.Services;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

public class ConsoleThemeTests
{
    [Theory]
    [MemberData(nameof(OfferedThemes))]
    public void EveryThemeSettingsOffersCanActuallyBeBuilt(string name)
    {
        // Settings stores a name, and startup turns it back into a theme. A name with nothing
        // behind it is a setting that appears to work, persists, and then silently does
        // nothing -- which is worse than not offering it.
        Assert.NotNull(ConsoleThemes.Create(name));
    }

    public static TheoryData<string> OfferedThemes()
    {
        var data = new TheoryData<string>();
        foreach (var name in SettingsViewModel.ThemeNames)
        {
            data.Add(name);
        }
        return data;
    }

    [Theory]
    [InlineData("ModernDark")]
    [InlineData("TurboVisionDark")]
    public void TheThemesConsoloniaShipsBrokenAreNotOffered(string name)
    {
        // Both exist as classes in Consolonia.Themes with no XAML behind them, so constructing
        // one throws "No precompiled XAML found". This test exists because the names look
        // perfectly ordinary next to the ones that work -- it was offering them that found the
        // problem -- and because a new Consolonia release may fix them, at which point this
        // fails and says so.
        Assert.DoesNotContain(name, SettingsViewModel.ThemeNames);
        Assert.Null(ConsoleThemes.Create(name));
    }

    [Fact]
    public void AnUnknownThemeNameResolvesToNothingRatherThanThrowing()
    {
        // An older build reading a newer settings file, or a hand edit. Startup keeps the
        // default instead of failing to start.
        Assert.Null(ConsoleThemes.Create("SomethingFromTheFuture"));
        Assert.Null(ConsoleThemes.Create(null));
        Assert.Null(ConsoleThemes.Create(""));
    }
}
