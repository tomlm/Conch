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
        /// Through sudo, because logind will not authorise it otherwise.
        /// </summary>
        /// <remarks>
        /// systemd asks polkit whether a non-root user may power the machine down, and polkit
        /// is not installed on Conchix: its image is built by mmdebstrap, which installs no
        /// Recommends, and polkitd is a Recommends of systemd rather than a dependency. So a
        /// bare `systemctl poweroff` from the session account fails authentication.
        ///
        /// The session account is in the sudo group, so sudo is the way through. It may ask
        /// for a password, which is why these run in a visible terminal window rather than
        /// silently in the background -- a prompt nobody can see is a hang.
        ///
        /// Installing polkitd in the Conchix image would let logind authorise a local seat
        /// user directly and make the sudo hop unnecessary. That belongs in the other repo.
        /// </remarks>
        private static string Systemctl(string verb) => $"sudo systemctl {verb}";
    }
}
