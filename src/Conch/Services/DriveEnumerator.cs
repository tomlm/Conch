using Conch.Utilities;

namespace Conch.Services
{
    /// <summary>A place the file browser can start from.</summary>
    /// <param name="Path">Where it is.</param>
    /// <param name="Label">What to call it.</param>
    public sealed record DriveRoot(string Path, string Label);

    /// <summary>
    /// The drives and mount points worth offering as starting points.
    /// </summary>
    /// <remarks>
    /// The platforms are not remotely alike here, which is the whole reason this exists.
    /// Windows reports four real volumes on a developer machine and needs no filtering at all.
    /// The same call on Linux reflects /proc/mounts, which on an ordinary WSL host returns 53
    /// entries of which exactly one is a disk anyone would want to browse -- the rest being
    /// fifteen snap loopbacks plus proc, sysfs, devpts, cgroup2, tmpfs, overlay and friends.
    /// An unfiltered list is not a shorter version of the right answer; it is unusable.
    /// </remarks>
    public static class DriveEnumerator
    {
        /// <summary>
        /// Filesystems that exist to expose kernel state rather than to store files.
        /// </summary>
        private static readonly HashSet<string> PseudoFilesystems = new(StringComparer.Ordinal)
        {
            "autofs", "binfmt_misc", "bpf", "cgroup", "cgroup2", "configfs", "debugfs",
            "devpts", "devtmpfs", "efivarfs", "fusectl", "hugetlbfs", "mqueue", "nsfs",
            "overlay", "proc", "pstore", "ramfs", "rootfs", "securityfs", "squashfs",
            "sysfs", "tmpfs", "tracefs",
        };

        /// <summary>
        /// Filesystems that are real storage but have no device under /dev.
        /// </summary>
        /// <remarks>
        /// The <c>/dev/</c> test is what removes the pseudo-filesystems, and a network share
        /// would be caught by it too -- its "device" is a host and path. So they are allowed
        /// back explicitly. 9p is here for WSL, where the Windows drives are mounted under
        /// /mnt and are genuinely the ones you want.
        /// </remarks>
        private static readonly HashSet<string> NetworkFilesystems = new(StringComparer.Ordinal)
        {
            "9p", "afs", "cifs", "fuse.sshfs", "nfs", "nfs4", "smbfs", "smb3",
        };

        /// <summary>Where the file browser should offer to start, on this machine.</summary>
        public static IReadOnlyList<DriveRoot> Enumerate() => Enumerate(Host.Current);

        /// <inheritdoc cref="Enumerate()"/>
        public static IReadOnlyList<DriveRoot> Enumerate(HostOs os)
        {
            var roots = os switch
            {
                HostOs.Windows => FromDriveInfo(),
                // Existence is checked here rather than in the parser: a stale or unreachable
                // mount should not be offered, but that is a fact about this machine, not about
                // reading a mount table -- and folding it in would make the parser answer
                // differently depending on where it ran.
                HostOs.Linux => FromProcMounts(ReadProcMounts())
                    .Where(r => Directory.Exists(r.Path))
                    .ToList(),
                _ => FromVolumes(),
            };

            return WithHome(roots);
        }

        /// <summary>
        /// Windows needs no filtering: DriveInfo reports the volumes and nothing else.
        /// </summary>
        private static IReadOnlyList<DriveRoot> FromDriveInfo()
        {
            var roots = new List<DriveRoot>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                // IsReady is the check that matters: an empty card reader or a disconnected
                // network drive is still a DriveInfo, and asking it for a label throws.
                if (!SafeIsReady(drive))
                {
                    continue;
                }

                string label;
                try
                {
                    label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name
                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
                }
                catch (IOException)
                {
                    label = drive.Name;
                }

                roots.Add(new DriveRoot(drive.Name, label));
            }

            return roots;
        }

        private static bool SafeIsReady(DriveInfo drive)
        {
            try
            {
                return drive.IsReady;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Picks the real filesystems out of a mount table.
        /// </summary>
        /// <remarks>
        /// Takes the contents rather than reading the file, so the filter can be tested against
        /// a captured mount table instead of whatever the build machine happens to have mounted.
        ///
        /// A line is kept when its device is under <c>/dev/</c> -- which is what a disk looks
        /// like -- or when its type is a network filesystem, which is real storage reached
        /// another way. Everything else goes. Deciding by type alone would not work: overlay
        /// and 9p both appear with and without real backing depending on the host.
        ///
        /// Pure, deliberately. Whether a mount point still exists is a fact about the machine
        /// rather than about the table, and checking it here would make the same input produce
        /// different answers on different hosts -- which is exactly what a filter like this
        /// needs to be pinned against.
        /// </remarks>
        public static IReadOnlyList<DriveRoot> FromProcMounts(string contents)
        {
            var roots = new List<DriveRoot>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in contents.Split('\n'))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 3)
                {
                    continue;
                }

                var device = fields[0];
                var mountPoint = Unescape(fields[1]);
                var type = fields[2];

                if (PseudoFilesystems.Contains(type) || type.StartsWith("fuse.snap", StringComparison.Ordinal))
                {
                    continue;
                }

                var isDisk = device.StartsWith("/dev/", StringComparison.Ordinal);
                if (!isDisk && !NetworkFilesystems.Contains(type))
                {
                    continue;
                }

                if (!IsSomewhereWorthOffering(mountPoint))
                {
                    continue;
                }

                // The same directory can be mounted more than once -- a bind mount, or a
                // filesystem remounted with different options -- and it is one place to browse
                // either way.
                if (!seen.Add(mountPoint))
                {
                    continue;
                }

                roots.Add(new DriveRoot(mountPoint, mountPoint == "/" ? "/" : mountPoint));
            }

            return roots;
        }

        /// <summary>
        /// Whether a mount point is somewhere a person would think of as a drive.
        /// </summary>
        /// <remarks>
        /// Being a real filesystem is not enough, which the captured mount table makes plain:
        /// /snap, /mnt/wslg/distro and /usr/lib/wsl/drivers all pass the device and type tests
        /// and none of them is a drive. They are parts of the system's own plumbing that happen
        /// to be mounted separately.
        ///
        /// So the rule is about place rather than kind, which is also how file managers behave:
        /// the root filesystem, and whatever sits directly under the conventional places for
        /// mounting media -- /mnt/c, /media/usb. One level, not any depth: /mnt/wslg/distro is
        /// WSL's business, not the user's.
        /// </remarks>
        private static bool IsSomewhereWorthOffering(string mountPoint)
        {
            if (mountPoint == "/")
            {
                return true;
            }

            foreach (var parent in MediaRoots)
            {
                if (!mountPoint.StartsWith(parent + "/", StringComparison.Ordinal))
                {
                    continue;
                }

                // Directly under it, so /mnt/c counts and /mnt/wslg/distro does not.
                return !mountPoint.AsSpan(parent.Length + 1).Contains('/');
            }

            return false;
        }

        /// <summary>
        /// Where removable and secondary storage conventionally appears.
        /// </summary>
        /// <remarks>
        /// /run/media/&lt;user&gt; is where udisks mounts things on a modern desktop, so it is
        /// listed with the user segment already in place by the caller's expansion below.
        /// </remarks>
        private static readonly string[] MediaRoots =
        [
            "/mnt",
            "/media",
            "/Volumes",
            "/run/media/" + Environment.UserName,
        ];

        /// <summary>
        /// /proc/mounts escapes spaces and a few other characters as octal.
        /// </summary>
        private static string Unescape(string value)
            => value.Contains('\\')
                ? value.Replace("\\040", " ").Replace("\\011", "\t").Replace("\\134", "\\")
                : value;

        private static string ReadProcMounts()
        {
            try
            {
                return File.ReadAllText("/proc/mounts");
            }
            catch (Exception ex)
            {
                Log.Warning("Files", $"Could not read /proc/mounts: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>macOS keeps everything but the boot volume under /Volumes.</summary>
        private static IReadOnlyList<DriveRoot> FromVolumes()
        {
            var roots = new List<DriveRoot> { new("/", "/") };

            try
            {
                foreach (var volume in Directory.EnumerateDirectories("/Volumes"))
                {
                    roots.Add(new DriveRoot(volume, Path.GetFileName(volume)));
                }
            }
            catch (Exception)
            {
                // No /Volumes is normal anywhere but macOS.
            }

            return roots;
        }

        /// <summary>
        /// Puts home first.
        /// </summary>
        /// <remarks>
        /// It is where nearly every session starts and the only root a user reliably owns, so
        /// it leads rather than sitting under a drive letter someone has to know to expand.
        /// </remarks>
        private static IReadOnlyList<DriveRoot> WithHome(IReadOnlyList<DriveRoot> roots)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home) || !Directory.Exists(home))
            {
                return roots;
            }

            var ordered = new List<DriveRoot> { new(home, "Home") };
            ordered.AddRange(roots.Where(r => !string.Equals(r.Path, home, StringComparison.Ordinal)));
            return ordered;
        }
    }
}
