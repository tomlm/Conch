using System.Runtime.InteropServices;
using Conch.Utilities;
using Xunit;

namespace Conch.Tests;

public class WindowsCommandLineTests
{
    [Theory]
    [InlineData("btop")]
    [InlineData("--exec")]
    [InlineData("-lc")]
    [InlineData("/usr/bin/printf")]
    [InlineData("a;b")]
    public void PlainArgumentsAreLeftAlone(string arg)
    {
        // The whole point. The PTY layer quoted these unconditionally, and a quoted
        // --exec is not an option as far as wsl.exe is concerned -- it parses its own raw
        // command line instead of going through CommandLineToArgvW.
        Assert.Equal(arg, WindowsCommandLine.Escape(arg));
    }

    [Theory]
    [InlineData(@"a b",                   @"""a b""")]
    [InlineData(@"",                      @"""""")]
    [InlineData(@"exec ""$0"" ""$@""",        @"""exec \""$0\"" \""$@\""""")]
    [InlineData(@"a\b c",                 @"""a\b c""")]
    [InlineData(@"ends with \",           @"""ends with \\""")]
    public void ArgumentsNeedingQuotesGetThem(string arg, string expected)
    {
        Assert.Equal(expected, WindowsCommandLine.Escape(arg));
    }

    [Fact]
    public void EscapingRoundTripsThroughTheRealWindowsParser()
    {
        // The test that matters: escape the arguments, hand the line to the parser Windows
        // itself uses, and get the originals back. A quoting rule that merely looks right
        // is exactly how this class of bug survives review.
        if (!OperatingSystem.IsWindows())
        {
            // CommandLineToArgvW is the thing under test; there is no Linux equivalent to
            // check the rule against, and asserting nothing is better than asserting our
            // own reimplementation of it.
            return;
        }

        var hostile = new[]
        {
            "btop",
            "--exec",
            @"exec ""$0"" ""$@""",
            "a b",
            "c;d",
            @"a quote in the ""middle "" of it",
            @"trailing slash \",
            @"trailing slashes \\",
            @"""fully quoted""",
            @"<%s>\n",
        };

        var line = "prog " + string.Join(" ", hostile.Select(WindowsCommandLine.Escape));

        Assert.Equal(hostile, Split(line).Skip(1));
    }

    [Fact]
    public void ArgumentsAreUntouchedOffWindows()
    {
        // Nothing to escape there: the process is handed a real argv, and quoting it would
        // only be quoting the program then has to see through.
        var args = new[] { "a b", @"exec ""$0""" };

        Assert.Equal(args, WindowsCommandLine.EscapeAll(args, HostOs.Linux));
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>The parser Windows itself uses to turn a command line back into an argv.</summary>
    private static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        Assert.NotEqual(IntPtr.Zero, argv);

        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            }

            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }
}
