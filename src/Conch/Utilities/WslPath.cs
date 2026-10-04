namespace Conch.Utilities
{
    /// <summary>
    /// Turns a Windows path into the path a Linux app running under WSL sees.
    /// </summary>
    /// <remarks>
    /// Files on Windows hands out Windows paths, and a Linux app given <c>C:\notes\a.md</c>
    /// looks for a file by that name in its working directory and finds nothing. Drives are
    /// under /mnt (WSL's default automount root), and a <c>\\wsl$\Distro\...</c> or
    /// <c>\\wsl.localhost\Distro\...</c> path is already a Linux path behind a prefix.
    /// </remarks>
    public static class WslPath
    {
        /// <summary>The Linux path for <paramref name="path"/>; anything else is returned as it was.</summary>
        public static string FromWindows(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] == '/')
            {
                return path;
            }

            // C:\a\b or C:/a/b
            if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
            {
                var rest = path.Length > 2 ? path[2..].Replace('\\', '/').TrimStart('/') : string.Empty;
                var drive = "/mnt/" + char.ToLowerInvariant(path[0]);
                return rest.Length == 0 ? drive : drive + "/" + rest;
            }

            foreach (var prefix in new[] { @"\\wsl$\", @"\\wsl.localhost\" })
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // \\wsl$\Ubuntu\home\me -> /home/me: drop the distro name.
                    var afterPrefix = path[prefix.Length..];
                    var slash = afterPrefix.IndexOf('\\');
                    return slash < 0 ? "/" : afterPrefix[slash..].Replace('\\', '/');
                }
            }

            return path;
        }
    }
}
