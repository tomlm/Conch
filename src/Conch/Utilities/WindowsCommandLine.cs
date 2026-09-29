using System.Text;

namespace Conch.Utilities
{
    /// <summary>
    /// Escapes arguments the way the Windows command line is conventionally quoted.
    /// </summary>
    /// <remarks>
    /// Needed because the PTY layer builds the command line by wrapping every argument in
    /// quotes unconditionally, and wsl.exe parses its own raw command line rather than
    /// going through CommandLineToArgvW. A quoted --exec does not match its option table,
    /// so wsl.exe stops treating it as an option, hands the whole line to the default
    /// shell, and the launch dies with "bash: line 1: --exec: command not found".
    /// Measured against the real wsl.exe:
    ///
    ///   wsl "--exec" "bash" "-lc" "exec ""$0"" ""$@""" "btop"   exit 127, not found
    ///   wsl --exec bash -lc "exec \"$0\" \"$@\"" btop           btop version: 1.3.0
    ///
    /// (" standing in for a quote, \" for a backslash-escaped one.)
    ///
    /// So Conch escapes the arguments itself and asks for the command line to be passed
    /// through verbatim. The rule is the usual one, the same MSVC and .NET's own
    /// ProcessStartInfo.ArgumentList apply: leave an argument alone when it needs nothing,
    /// otherwise quote it, double the backslashes that run into a quote or the closing
    /// quote, and escape an embedded quote with a backslash.
    /// </remarks>
    public static class WindowsCommandLine
    {
        /// <summary>
        /// Escapes <paramref name="args"/> for a verbatim Windows command line, or returns
        /// them unchanged off Windows, where arguments reach the process as a real argv.
        /// </summary>
        public static IList<string> EscapeAll(IEnumerable<string> args, HostOs os)
            => os == HostOs.Windows
                ? args.Select(Escape).ToList()
                : args.ToList();

        /// <inheritdoc cref="EscapeAll(IEnumerable{string}, HostOs)"/>
        public static IList<string> EscapeAll(IEnumerable<string> args)
            => EscapeAll(args, Host.Current);

        /// <summary>
        /// Escapes one argument so that a parser recovers exactly the string passed in.
        /// </summary>
        public static string Escape(string arg)
        {
            if (arg.Length > 0 && !arg.Any(c => c is ' ' or '\t' or '\n' or '\v' or '"'))
            {
                return arg;
            }

            var builder = new StringBuilder(arg.Length + 2);
            builder.Append('"');

            for (var i = 0; i < arg.Length; i++)
            {
                var backslashes = 0;
                while (i < arg.Length && arg[i] == '\\')
                {
                    i++;
                    backslashes++;
                }

                if (i == arg.Length)
                {
                    // A run of backslashes at the end runs into the closing quote, so each
                    // has to be doubled or the quote itself would be read as escaped.
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (arg[i] == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1).Append('"');
                }
                else
                {
                    // Backslashes not running into a quote are literal and stay as they are.
                    builder.Append('\\', backslashes).Append(arg[i]);
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
