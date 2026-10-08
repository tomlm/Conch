namespace Conch.Utilities
{
    /// <summary>
    /// The flags Conch itself understands, parsed out of the command line.
    /// </summary>
    /// <remarks>
    /// Deliberately tiny, and deliberately non-consuming: the arguments still go on to
    /// Avalonia afterwards, so a flag meant for the toolkit is not swallowed here.
    /// </remarks>
    public sealed record CommandLineOptions(bool OwnsSession)
    {
        /// <summary>
        /// Conch is the session rather than one program among many.
        /// </summary>
        /// <remarks>
        /// Set by Cursix through CONCH_ARGS in /etc/default/conch, which conch@.service reads
        /// as its EnvironmentFile and the package wrapper expands. It gates shutting the
        /// machine down and restarting it, so that a misclick on a development box -- where
        /// Conch is an ordinary application and the machine is being worked on -- cannot
        /// reboot it. Logging out does not need the flag: leaving is always allowed.
        /// </remarks>
        public const string SessionFlag = "--session";

        public static CommandLineOptions Parse(IEnumerable<string>? args)
        {
            var ownsSession = args?.Any(
                a => string.Equals(a, SessionFlag, StringComparison.OrdinalIgnoreCase)) ?? false;

            return new CommandLineOptions(ownsSession);
        }

        /// <summary>What this process was started with. Set once, at startup.</summary>
        public static CommandLineOptions Current { get; private set; } = new(OwnsSession: false);

        internal static void SetCurrent(CommandLineOptions options) => Current = options;
    }
}
