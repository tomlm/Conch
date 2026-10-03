using Conch.Controls;
using Xunit;

namespace Conch.Tests;

/// <summary>How the top bar's window list labels windows and decides how many fit.</summary>
public class WindowListTests
{
    [Fact]
    public void AShortTitleIsShownWhole()
    {
        Assert.Equal("nano", WindowList.Label("nano"));
    }

    [Fact]
    public void ALongTitleIsCutToTheLimitWithAnEllipsis()
    {
        var label = WindowList.Label("Software Manager and more besides");

        Assert.Equal(WindowList.MaxTitle, label.Length);
        Assert.EndsWith("…", label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AWindowWithNoTitleStillGetsALabel(string? title)
    {
        Assert.Equal("(untitled)", WindowList.Label(title));
    }

    [Fact]
    public void EverythingIsShownWhenItAllFits()
    {
        // 6 + 1 + 6 + 1 + 6 = 20 columns.
        Assert.Equal(3, WindowList.Fit([6, 6, 6], available: 20, overflowWidth: 4));
    }

    [Fact]
    public void WhenNotEverythingFitsRoomIsLeftForTheOverflowButton()
    {
        // Two buttons and their spacing take 14; the overflow button needs 4 more, which fits
        // in 19 but not alongside a third button.
        Assert.Equal(2, WindowList.Fit([6, 6, 6, 6], available: 19, overflowWidth: 4));
    }

    [Fact]
    public void WithNoRoomEverythingGoesIntoTheOverflowButton()
    {
        Assert.Equal(0, WindowList.Fit([6, 6], available: 5, overflowWidth: 4));
    }

    [Fact]
    public void NoWindowsFitTrivially()
    {
        Assert.Equal(0, WindowList.Fit([], available: 0, overflowWidth: 4));
    }
}
