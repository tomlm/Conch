using Conch.Utilities;

namespace Conch.Services
{
    /// <summary>
    /// Ending the session, and ending the machine's day.
    /// </summary>
    public static class SessionActions
    {
        /// <summary>
        /// The command that powers the machine off, or null where Conch has no business
        /// trying.
        /// </summary>
        public static string? PowerOffCommand(HostOs os) => os switch
        {
            HostOs.Linux => Systemctl("poweroff"),
            HostOs.Windows => "shutdown /s /t 0",
            _ => null,
        };

        /// <summary>The command that restarts the machine, or null.</summary>
        public static string? RestartCommand(HostOs os) => os switch
        {
            HostOs.Linux => Systemctl("reboot"),
            HostOs.Windows => "shutdown /r /t 0",
            _ => null,
        };

        /// <summary>
        /// Plainly first, and through sudo only if that is refused.
        /// </summary>
        /// <remarks>
        /// systemd asks polkit whether a non-root user may power the machine down, and the
        /// answer depends on the machine. Where polkitd is installed -- Cursix, since
        /// NetworkManager needs it, and most desktop systems -- logind authorises a local seat
        /// user and the direct call simply works, with nothing to type. Where it is absent the
        /// call fails authentication, and sudo is the way through for an account in the sudo
        /// group.
        ///
        /// Asking in that order means the common case costs no password prompt and the
        /// uncommon one still works. Trying sudo first would prompt every time on a machine
        /// that never needed it, which on an appliance is a password between the user and
        /// turning their computer off.
        ///
        /// Either way this runs in a visible terminal window rather than in the background,
        /// because sudo may ask for a password and a prompt nobody can see is a hang.
        /// </remarks>
        private static string Systemctl(string verb) => $"systemctl {verb} || sudo systemctl {verb}";
    }
}
