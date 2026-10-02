using Avalonia.Controls;
using Avalonia.Media;
using Conch.Services;
using Conch.Utilities;
using Iciclecreek.Terminal;

namespace Conch.Controls
{
    /// <summary>
    /// A terminal docked inside another window, for a task that window started: an install, an
    /// uninstall. Runs one command at a time and reports how it ended.
    /// </summary>
    /// <remarks>
    /// A real terminal rather than a read-only log, because these commands ask things: sudo wants
    /// a password, apt can stop at a question, nmcli asks for a passphrase. The panel takes focus
    /// when a command starts so the answer can simply be typed.
    ///
    /// A fresh TerminalControl per command. The control launches its process when it loads and
    /// offers no relaunch, so swapping in a new one is how a multi-step install runs each step in
    /// the same place. The previous step's output goes with it, which is fine: a plan stops at the
    /// first failure, so the output left showing is always the one worth reading.
    /// </remarks>
    public sealed class TaskTerminalPanel : ContentControl
    {
        private TerminalControl? _terminal;
        private TaskCompletionSource<int?>? _exit;

        /// <summary>True while a command is running here.</summary>
        public bool IsRunning => _exit is { Task.IsCompleted: false };

        /// <summary>True once anything has run, so there is output to show again.</summary>
        public bool HasOutput => _terminal != null;

        /// <summary>
        /// Runs <paramref name="command"/> and completes with its exit code, or null when the code
        /// could not be read (the process ended, but how is unknown).
        /// </summary>
        public Task<int?> RunAsync(ResolvedCommand command)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("A command is already running in this panel.");
            }

            var exit = _exit = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var terminal = new TerminalControl
            {
                Options = new XTerm.Options.TerminalOptions(),
                FontFamily = new FontFamily(AppLauncher.DefaultFontFamily),

                // The same escaping ManagedTerminalWindow applies, for the same reason: on Windows
                // these go through wsl.exe, which parses its own raw command line and stops
                // recognising its options once the PTY layer has quoted them. See the notes there.
                VerbatimCommandLine = OperatingSystem.IsWindows(),
                Process = command.Process,
                ProcessArgs = WindowsCommandLine.EscapeAll(command.Args),
            };

            terminal.ProcessExited += (_, e) => exit.TrySetResult(e.ExitCodeKnown ? e.ExitCode : null);
            terminal.Loaded += (_, _) => terminal.Focus();

            Replace(terminal);
            return exit.Task;
        }

        /// <summary>
        /// Moves a running command out of this panel, into whatever <paramref name="adopt"/> gives
        /// it, and lets the task waiting on it finish when it finishes there.
        /// </summary>
        /// <returns>False when nothing was running, so there was nothing to move.</returns>
        /// <remarks>
        /// For a panel whose window is closing. Killing an install halfway can leave a package
        /// manager mid-transaction, and refusing to close is worse than moving the work somewhere
        /// it can carry on.
        /// </remarks>
        public bool HandOver(Func<Porta.Pty.IPtyConnection, ManagedTerminalWindow> adopt)
        {
            if (!IsRunning || _terminal == null || _exit == null)
            {
                return false;
            }

            var connection = _terminal.DetachConnection();
            if (connection == null)
            {
                return false;
            }

            var exit = _exit;
            var window = adopt(connection);
            window.ProcessExited += (_, e) => exit.TrySetResult(e.ExitCodeKnown ? e.ExitCode : null);
            return true;
        }

        private void Replace(TerminalControl terminal)
        {
            var previous = _terminal;
            _terminal = terminal;
            Content = terminal;

            // Its process has ended -- RunAsync refuses while one is running -- so this only
            // releases the emulator and its buffers.
            previous?.Dispose();
        }
    }
}
