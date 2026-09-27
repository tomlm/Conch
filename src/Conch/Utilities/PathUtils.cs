using System;
using System.IO;
using System.Runtime.InteropServices;
using static System.Net.Mime.MediaTypeNames;

namespace Conduit.Utilities
{
    internal static class PathUtils
    {
        public static bool IsOnPath(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return false;

            processName = processName.Trim();

            if (processName.Contains(Path.DirectorySeparatorChar) || processName.Contains(Path.AltDirectorySeparatorChar))
                return File.Exists(processName);

            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var extensions = GetExecutableExtensions();
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                foreach (var ext in extensions)
                {
                    var candidate = Path.Combine(dir, processName + ext);
                    if (File.Exists(candidate))
                        return true;
                }
            }

            return false;
        }

        public static string? ResolveOnPath(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return null;

            processName = processName.Trim();

            if (processName.Contains(Path.DirectorySeparatorChar) || processName.Contains(Path.AltDirectorySeparatorChar))
                return File.Exists(processName) ? Path.GetFullPath(processName) : null;

            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var extensions = GetExecutableExtensions();
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                var candidate = Path.Combine(dir, processName);
                if (File.Exists(candidate))
                    return candidate;

                foreach (var ext in extensions)
                {
                    candidate = Path.Combine(dir, processName + ext);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        private static string[] GetExecutableExtensions()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return [""];

            var pathext = Environment.GetEnvironmentVariable("PATHEXT");
            if (string.IsNullOrWhiteSpace(pathext))
                return [".exe", ".cmd", ".bat", ".com"];

            return pathext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
