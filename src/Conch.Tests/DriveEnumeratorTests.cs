using Conch.Services;
using Conch.Utilities;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// The mount-table filter, tested against a real mount table.
/// </summary>
/// <remarks>
/// The fixture is /proc/mounts captured verbatim from an ordinary WSL host: 53 lines, of which
/// four are places a person would call a drive. That ratio is why this class exists, and a
/// synthetic fixture would not have it -- the noise is specific and strange in ways nobody
/// would invent: fifteen snap loopbacks, an overlay of the kernel modules, a 9p mount of the
/// Windows drives, and WSLg's own filesystems buried two levels under /mnt.
/// </remarks>
public class DriveEnumeratorTests
{
    private static string ProcMounts =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "proc-mounts-wsl.txt"));

    [Fact]
    public void TheFixtureIsStillTheRealThing()
    {
        // If this shrinks, someone has trimmed the fixture and the test below stops meaning
        // anything.
        Assert.Equal(53, ProcMounts.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void FiftyThreeMountsBecomeTheFourWorthShowing()
    {
        // The exact answer, which the parser can be held to because it is pure: the root
        // filesystem, and the three Windows drives WSL mounts over 9p -- which are genuinely
        // where that user's files live.
        var roots = DriveEnumerator.FromProcMounts(ProcMounts);

        Assert.Equal(
            new[] { "/", "/mnt/c", "/mnt/h", "/mnt/s" },
            roots.Select(r => r.Path).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void SnapLoopbacksAreAllDropped()
    {
        // Fifteen of them on this host, one per installed application. Offering them as drives
        // would bury the one real disk.
        var roots = DriveEnumerator.FromProcMounts(ProcMounts);

        Assert.DoesNotContain(roots, r => r.Path.StartsWith("/snap", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/proc")]
    [InlineData("/sys")]
    [InlineData("/dev/pts")]
    [InlineData("/run")]
    [InlineData("/sys/fs/cgroup")]
    [InlineData("/dev/shm")]
    public void KernelStateIsNotADrive(string mountPoint)
    {
        Assert.DoesNotContain(DriveEnumerator.FromProcMounts(ProcMounts), r => r.Path == mountPoint);
    }

    [Theory]
    [InlineData("/dev/sda1 /snap ext4 rw 0 0", "/snap")]
    [InlineData("/dev/sdb1 /mnt/wslg/distro ext4 rw 0 0", "/mnt/wslg/distro")]
    [InlineData("drivers /usr/lib/wsl/drivers 9p ro 0 0", "/usr/lib/wsl/drivers")]
    public void ARealFilesystemInThePlumbingIsStillNotADrive(string line, string mountPoint)
    {
        // Each of these passes the device and type tests and is a genuine filesystem. None is
        // a drive -- they are parts of the system's own plumbing that happen to be mounted
        // separately, and all three are in the captured table. Being real is not the test.
        Assert.DoesNotContain(DriveEnumerator.FromProcMounts(line + "\n"), r => r.Path == mountPoint);
    }

    [Fact]
    public void AMountDirectlyUnderAMediaRootIsADrive()
    {
        Assert.Contains(
            DriveEnumerator.FromProcMounts("/dev/sdc1 /media/usb vfat rw 0 0\n"),
            r => r.Path == "/media/usb");
    }

    [Fact]
    public void ANetworkShareIsKeptEvenThoughItHasNoDevice()
    {
        // The /dev/ test is what removes the pseudo-filesystems, but a network share would be
        // caught by it too: its "device" is a host and a path. The types are allowed back
        // explicitly, and this is the case that would silently disappear otherwise.
        Assert.Contains(
            DriveEnumerator.FromProcMounts("server:/export /mnt/share nfs4 rw 0 0\n"),
            r => r.Path == "/mnt/share");
    }

    [Fact]
    public void ADiskIsKeptByItsDeviceRatherThanItsType()
    {
        // Deciding by type alone does not work: overlay and 9p appear with and without real
        // backing depending on the host. A device under /dev is what a disk actually looks like.
        Assert.Contains(
            DriveEnumerator.FromProcMounts("/dev/sda1 / ext4 rw 0 0\n"),
            r => r.Path == "/");
    }

    [Fact]
    public void TheSameMountPointIsNotListedTwice()
    {
        // A bind mount, or a filesystem remounted with different options. It is one place to
        // browse either way.
        var mounts = "/dev/sda1 /mnt/data ext4 rw 0 0\n/dev/sdb1 /mnt/data ext4 ro 0 0\n";

        Assert.Single(DriveEnumerator.FromProcMounts(mounts));
    }

    [Fact]
    public void AMountPointWithASpaceIsReadCorrectly()
    {
        // /proc/mounts escapes a space as \040. Taking the field literally would produce a path
        // that does not exist, and the drive would silently vanish from the list.
        Assert.Contains(
            DriveEnumerator.FromProcMounts("/dev/sdz1 /mnt/my\\040drive ext4 rw 0 0\n"),
            r => r.Path == "/mnt/my drive");
    }

    [Fact]
    public void AFixtureThatHasBeenThroughAWindowsCheckoutStillParses()
    {
        // .gitattributes marks the fixture directory -text so this cannot happen, but the
        // failure it prevents is silent and total: with CRLF, every line's filesystem type
        // becomes "ext4\r", nothing matches, and the drive list is simply empty. Belt and
        // braces on something with no symptom other than an empty list.
        var withCrLf = ProcMounts.Replace("\n", "\r\n");

        Assert.Equal(
            DriveEnumerator.FromProcMounts(ProcMounts).Select(r => r.Path),
            DriveEnumerator.FromProcMounts(withCrLf).Select(r => r.Path));
    }

    [Fact]
    public void GarbageInTheMountTableIsSkippedRatherThanThrowing()
    {
        Assert.Empty(DriveEnumerator.FromProcMounts("\n\nnot enough fields\n/dev/sda1\n"));
    }

    [Fact]
    public void HomeComesFirst()
    {
        // Where nearly every session starts, and the only root a user reliably owns.
        var roots = DriveEnumerator.Enumerate(Host.Current);

        Assert.NotEmpty(roots);
        Assert.Equal("Home", roots[0].Label);
    }

    [Fact]
    public void EveryPlatformEnumeratesWithoutThrowing()
    {
        foreach (var os in new[] { HostOs.Windows, HostOs.Linux, HostOs.MacOS })
        {
            Assert.Null(Record.Exception(() => DriveEnumerator.Enumerate(os)));
        }
    }
}
