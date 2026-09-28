using Consolonia.Controls;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// Covers the capability arithmetic behind <c>--software-cursor</c>.
/// </summary>
/// <remarks>
/// Consolonia draws the mouse pointer itself only when the console reports
/// SupportsMouseMove without SupportsMouseCursor, so Conch clears the latter to ask
/// for a software pointer. The trap is that these flags nest — SupportsMouseMove
/// includes SupportsMouseButtons, and SupportsMouseCursor includes SupportsMouseMove
/// — so clearing SupportsMouseCursor as a whole removes movement and buttons with it
/// and leaves no mouse at all. These pin the distinction, and would fail if the enum
/// layout changed upstream.
/// </remarks>
public class ConsoleCapabilityMaskTests
{
    /// <summary>The bit that claims the terminal draws a pointer, and nothing else.</summary>
    private const ConsoleCapabilities PointerIsDrawnForUs =
        ConsoleCapabilities.SupportsMouseCursor & ~ConsoleCapabilities.SupportsMouseMove;

    [Fact]
    public void TheLayoutIsWhatTheMaskingAssumes()
    {
        // The referenced Consolonia package defines these as nested composites rather
        // than independent bits. If that ever changes, the mask below is wrong.
        Assert.Equal(1, (int)ConsoleCapabilities.SupportsMouseButtons);
        Assert.Equal(3, (int)ConsoleCapabilities.SupportsMouseMove);
        Assert.Equal(7, (int)ConsoleCapabilities.SupportsMouseCursor);
        Assert.Equal(4, (int)PointerIsDrawnForUs);
    }

    [Fact]
    public void TheCapabilitiesNest()
    {
        Assert.True(ConsoleCapabilities.SupportsMouseMove.HasFlag(ConsoleCapabilities.SupportsMouseButtons));
        Assert.True(ConsoleCapabilities.SupportsMouseCursor.HasFlag(ConsoleCapabilities.SupportsMouseMove));
    }

    [Fact]
    public void MaskingThePointerClaimKeepsMovementAndButtons()
    {
        var capabilities = ConsoleCapabilities.SupportsMouseCursor;

        var masked = capabilities & ~PointerIsDrawnForUs;

        Assert.False(masked.HasFlag(ConsoleCapabilities.SupportsMouseCursor));
        Assert.True(masked.HasFlag(ConsoleCapabilities.SupportsMouseMove));
        Assert.True(masked.HasFlag(ConsoleCapabilities.SupportsMouseButtons));
    }

    [Fact]
    public void ClearingTheWholeCompositeWouldDisableTheMouse()
    {
        // Documents the bug this guards against rather than asserting desired behaviour:
        // the obvious-looking mask takes movement and buttons down with it, and
        // Consolonia then draws no pointer at all instead of drawing its own.
        var capabilities = ConsoleCapabilities.SupportsMouseCursor;

        var overZealous = capabilities & ~ConsoleCapabilities.SupportsMouseCursor;

        Assert.False(overZealous.HasFlag(ConsoleCapabilities.SupportsMouseMove));
        Assert.False(overZealous.HasFlag(ConsoleCapabilities.SupportsMouseButtons));
    }

    [Fact]
    public void MaskingLeavesUnrelatedCapabilitiesAlone()
    {
        var capabilities = ConsoleCapabilities.SupportsMouseCursor
                           | ConsoleCapabilities.SupportsAltSolo
                           | ConsoleCapabilities.SupportsComplexEmoji;

        var masked = capabilities & ~PointerIsDrawnForUs;

        Assert.True(masked.HasFlag(ConsoleCapabilities.SupportsAltSolo));
        Assert.True(masked.HasFlag(ConsoleCapabilities.SupportsComplexEmoji));
    }
}
