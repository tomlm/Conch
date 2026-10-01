using System.Diagnostics;

namespace Conch.Utilities
{
    /// <summary>
    /// Starts a program Conch talks to but the person never sees: a detect probe, an nmcli call.
    /// </summary>
    /// <remarks>
    /// All three standard streams are redirected, input included, and that is the whole point of
    /// this class. On Unix, .NET treats a child that shares any stream with the console as one
    /// that "uses the terminal", and for as long as it runs puts the terminal back into the mode
    /// it was in before the app started: echo on, line buffered. Conch had turned echo off to read
    /// the mouse itself, so for the life of every such child the terminal echoed the mouse --
    /// <c>^[[&lt;35;103;10M</c> printed over the Network window's tabs for each movement while
    /// its three nmcli listings ran. Seen in WSL with nmcli 1.46.
    ///
    /// Input is also closed at once, so a child that reads it gets end-of-file rather than the
    /// keystrokes meant for Conch, or a wait that never ends.
    /// </remarks>
    public static class BackgroundProcess
    {
        public static ProcessStartInfo StartInfo(string fileName, IEnumerable<string> args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return startInfo;
        }

        /// <summary>Starts it, with nothing to read on its input.</summary>
        public static Process? Start(ProcessStartInfo startInfo)
        {
            var process = Process.Start(startInfo);
            process?.StandardInput.Close();
            return process;
        }
    }
}
