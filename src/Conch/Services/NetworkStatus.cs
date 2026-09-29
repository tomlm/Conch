using System.Net.NetworkInformation;
using Conch.Utilities;

namespace Conch.Services
{
    /// <summary>
    /// Whether this machine has a network, cheaply enough to ask repeatedly.
    /// </summary>
    /// <remarks>
    /// Cheap enough not to spawn a process or block on DNS, but not cheap enough to call on
    /// the UI thread: measured at 30.7ms per sample through NetworkInterface on Windows, which
    /// is about two frames of a console redraw. Hence <see cref="IsUpAsync"/>, which is what
    /// the shell actually uses.
    /// </remarks>
    public static class NetworkStatus
    {
        /// <summary>Linux answers from sysfs, everything else from the BCL.</summary>
        public static bool IsUp(HostOs os) => os == HostOs.Linux ? IsUpFromSysfs() : IsUpFromBcl();

        /// <inheritdoc cref="IsUp(HostOs)"/>
        public static bool IsUp() => IsUp(Host.Current);

        /// <summary>
        /// Samples off the UI thread.
        /// </summary>
        /// <remarks>
        /// Off-thread because of the measurement above: enumerating network interfaces is tens
        /// of milliseconds on Windows, and a status indicator is not worth stalling the desktop
        /// for. The Linux path is far cheaper, but it runs the same way rather than branching
        /// on cost -- the caller should not have to know which platform is expensive today.
        /// </remarks>
        public static Task<bool> IsUpAsync() => Task.Run(() => IsUp());

        /// <summary>
        /// Reads <c>/sys/class/net/*/operstate</c>, which is the kernel's own answer.
        /// </summary>
        /// <remarks>
        /// Plain file reads of a few bytes each, with no process to spawn -- which matters on
        /// Conchix, where the alternative would be shelling out to networkctl several times a
        /// minute for the life of the session.
        ///
        /// Loopback is skipped: it is always up and would make the indicator meaningless.
        /// </remarks>
        private static bool IsUpFromSysfs()
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories("/sys/class/net"))
                {
                    var name = Path.GetFileName(directory);
                    if (name == "lo")
                    {
                        continue;
                    }

                    var stateFile = Path.Combine(directory, "operstate");
                    if (File.Exists(stateFile) &&
                        File.ReadAllText(stateFile).Trim() == "up")
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // An unreadable sysfs is not worth a warning every five seconds.
                Log.Info("Network", $"Could not read interface state: {ex.Message}");
            }

            return false;
        }

        private static bool IsUpFromBcl()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().Any(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel);
            }
            catch (NetworkInformationException ex)
            {
                Log.Info("Network", $"Could not read interface state: {ex.Message}");
                return false;
            }
        }
    }
}
