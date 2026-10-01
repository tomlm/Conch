using Microsoft.Win32;

namespace Conch.Utilities
{
    /// <summary>
    /// Whether this Windows machine has a WSL distribution that Linux registrations can run in.
    /// </summary>
    /// <remarks>
    /// Read from the registry rather than by running <c>wsl.exe</c>: Windows ships wsl.exe as a
    /// stub on machines that have never installed WSL, so its presence proves nothing, and asking
    /// it costs a process per call at startup. WSL records each registered distribution under
    /// HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss, with the one plain <c>wsl</c> runs
    /// named by <c>DefaultDistribution</c>.
    ///
    /// The default distribution is the one asked about because it is the one Conch's commands
    /// land in -- <see cref="ShellCommand"/> runs <c>wsl --exec</c> with no <c>-d</c>. Docker
    /// Desktop's own distributions are not counted: they are registered like any other, and
    /// installing nano into one is not what anybody meant.
    ///
    /// Read once. Installing WSL needs a restart of Conch to be noticed, which is the same as
    /// for anything else that changes PATH.
    /// </remarks>
    public static class Wsl
    {
        private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

        private static readonly Lazy<bool> _isAvailable = new(Detect);

        public static bool IsAvailable => _isAvailable.Value;

        private static bool Detect()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
                if (lxss?.GetValue("DefaultDistribution") is not string id)
                {
                    return false;
                }

                using var distribution = lxss.OpenSubKey(id);
                var name = distribution?.GetValue("DistributionName") as string;

                var available = IsUsableDistribution(name);
                Log.Info("Wsl", available
                    ? $"Default WSL distribution: {name}."
                    : $"No usable default WSL distribution (found '{name ?? "none"}').");
                return available;
            }
            catch (Exception ex)
            {
                Log.Warning("Wsl", $"Could not read the WSL registration: {ex.Message}");
                return false;
            }
        }

        /// <summary>Whether a distribution with this name is one Linux apps belong in.</summary>
        public static bool IsUsableDistribution(string? name)
            => !string.IsNullOrWhiteSpace(name)
               && !name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase);
    }
}
