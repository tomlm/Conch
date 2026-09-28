using Consolonia.Controls;
using Consolonia.PlatformSupport;

namespace Conch.Infrastructure
{
    /// <summary>
    /// A console that reports no mouse cursor of its own, so Consolonia draws one.
    /// </summary>
    /// <remarks>
    /// Consolonia decides this in ConsoleWindow.GetDefaultCursor: when the console
    /// reports SupportsMouseMove but not SupportsMouseCursor, it composites a pointer
    /// into the pixel buffer — lifting the character underneath and inverting it —
    /// rather than leaving the pointer to the terminal.
    ///
    /// CursesConsole sets SupportsMouseCursor when DISPLAY is set or when the terminal
    /// is not a tty, on the reasonable assumption that a GUI terminal draws its own
    /// pointer. That assumption does not hold for every console: kmscon, for one,
    /// presents as a pts with TERM=xterm-256color and so looks like a GUI terminal by
    /// that test, while what it actually draws is a hardware DRM cursor plane whose
    /// behaviour is nothing like a windowing pointer.
    ///
    /// Rather than guess, this leaves the detection alone and lets the decision be
    /// made explicitly with --software-cursor. The capability is cleared after
    /// PrepareConsole, because that is where CursesConsole enables mouse support and
    /// sets the flag.
    /// </remarks>
    internal sealed class SoftwareCursorConsole : CursesConsole
    {
        /// <summary>
        /// The bit that distinguishes "the terminal draws a pointer" from "it reports
        /// mouse movement".
        /// </summary>
        /// <remarks>
        /// ConsoleCapabilities nests these: SupportsMouseMove is SupportsMouseButtons
        /// plus a bit, and SupportsMouseCursor is SupportsMouseMove plus another. So
        /// clearing SupportsMouseCursor wholesale clears movement and buttons too, and
        /// the result is no mouse at all rather than a pointer we draw ourselves.
        /// Masking the difference removes only the claim about the pointer.
        /// </remarks>
        private const ConsoleCapabilities PointerIsDrawnForUs =
            ConsoleCapabilities.SupportsMouseCursor & ~ConsoleCapabilities.SupportsMouseMove;

        public override void PrepareConsole()
        {
            base.PrepareConsole();

            Capabilities &= ~PointerIsDrawnForUs;
        }
    }
}
