using Avalonia.Threading;
using Conch.Controls;
using Conch.Utilities;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Services
{
    /// <summary>
    /// Opens terminal windows for the shell: registered apps, ad-hoc command lines, plain shells,
    /// and the package commands that install and remove apps.
    /// </summary>
    public sealed class AppLauncher
    {
        private const string LogCategory = "Launch";
        internal const string DefaultFontFamily = "Cascadia Mono";

        private readonly WindowsPanel _windows;

        public AppLauncher(WindowsPanel windows)
        {
            _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        }

        /// <summary>
        /// Opens an interactive shell.
        /// </summary>
        public ManagedTerminalWindow LaunchShell()
        {
            Log.Info(LogCategory, $"Opening a shell ({ShellCommand.DefaultShell}).");
            return Open(new ResolvedCommand(ShellCommand.DefaultShell, new List<string>()), title: null, closeOnExit: true);
        }

        /// <summary>
        /// Runs a command line typed by the user, keeping the window once it finishes.
        /// </summary>
        /// <remarks>
        /// Kept, because what a typed command prints is usually the point of typing it: closed
        /// on exit, "ls -la" was a window that flashed and took the listing with it. The title
        /// says it has finished, so it does not pass for a program that is merely idle.
        /// </remarks>
        public ManagedTerminalWindow LaunchCommandLine(string commandLine)
        {
            var command = ShellCommand.ForCommandLine(commandLine);
            Log.Info(LogCategory, $"Running '{commandLine}' as: {command}");

            var title = commandLine.Trim();
            var window = Open(command, title, closeOnExit: false);
            window.ProcessExited += (s, e) =>
            {
                var code = e.ExitCodeKnown ? e.ExitCode : -1;
                window.Title = code == 0 ? $"{title} - finished" : $"{title} - exited ({code})";
            };

            return window;
        }

        /// <summary>
        /// Launches a registered app, filling its argument template with <paramref name="values"/>.
        /// </summary>
        /// <returns>The new window, or null when the arguments could not be built.</returns>
        public ManagedTerminalWindow? LaunchTool(ToolViewModel tool, IReadOnlyList<string>? values = null)
        {
            if (!ArgumentTemplate.TryBuild(tool.Args, values ?? Array.Empty<string>(), out var args, out var error))
            {
                Log.Warning(LogCategory, $"Cannot launch {tool.Id}: {error}");
                return null;
            }

            var command = ShellCommand.ForProcess(tool.Command, args, tool.RunsUnderWsl);
            Log.Info(LogCategory, $"Launching {tool.Id} as: {command}");

            var window = Open(command, tool.Name, closeOnExit: true, tool.Cols, tool.Rows);

            window.ProcessExited += (s, e) =>
            {
                var code = e.ExitCodeKnown ? e.ExitCode : -1;
                if (code == 0)
                {
                    return;
                }

                Log.Warning(LogCategory, $"{tool.Id} exited with code {code}: {command}");

                // Keep the window so whatever the app printed on its way out stays
                // readable. Closing on a failure takes the error with it, which is how a
                // missing dependency or an unusable terminal ends up looking like a window
                // that flashed and vanished for no reason.
                //
                // This runs before ManagedTerminalWindow honours CloseOnProcessExit, so
                // clearing it here is what keeps the window open.
                window.CloseOnProcessExit = false;

                // Say so in the chrome as well. A window that stays open after its app
                // quit otherwise looks like an app that is merely idle.
                window.Title = $"{tool.Name} - exited ({code})";
            };

            return window;
        }

        /// <summary>
        /// Runs a package command in a window so its output stays on screen.
        /// </summary>
        /// <remarks>
        /// The window deliberately outlives the process: an install that fails is only useful if
        /// the reason is still readable afterwards.
        /// </remarks>
        public ManagedTerminalWindow RunScript(string title, string script, bool viaWsl, Action<int>? onExit = null)
        {
            var command = ShellCommand.ForScript(script, viaWsl);
            Log.Info(LogCategory, $"{title}: {script}");

            var window = Open(command, title, closeOnExit: false, cols: 100, rows: 30);

            window.ProcessExited += (s, e) =>
            {
                var code = e.ExitCodeKnown ? e.ExitCode : -1;
                if (code == 0)
                {
                    Log.Info(LogCategory, $"{title} succeeded.");
                }
                else
                {
                    Log.Warning(LogCategory, $"{title} exited with code {code}.");
                }

                onExit?.Invoke(code);
            };

            return window;
        }

        /// <summary>
        /// Opens a window around a session that is already running, such as an install whose
        /// panel is closing.
        /// </summary>
        public ManagedTerminalWindow Adopt(Porta.Pty.IPtyConnection connection, string title)
        {
            // An empty Process is what stops the window starting a shell of its own on load.
            var window = new ManagedTerminalWindow(100, 30)
            {
                Process = string.Empty,
                FontFamily = DefaultFontFamily,
                CloseOnProcessExit = false,
                Title = title,
            };

            window.Show(_windows);
            window.AttachConnection(connection);
            Dispatcher.UIThread.Post(window.Activate, DispatcherPriority.Background);

            Log.Info(LogCategory, $"{title}: moved into its own window.");
            return window;
        }

        private ManagedTerminalWindow Open(ResolvedCommand command, string? title, bool closeOnExit, int cols = 80, int rows = 25)
        {
            var window = new ManagedTerminalWindow(cols, rows)
            {
                Process = command.Process,
                ProcessArgs = command.Args,
                FontFamily = DefaultFontFamily,
                CloseOnProcessExit = closeOnExit,
            };

            if (!string.IsNullOrWhiteSpace(title))
            {
                window.Title = title;
            }

            window.Show(_windows);

            // Activated again once the input that opened it has finished. ManagedWindow activates
            // itself on Tapped as well as on press, and Avalonia raises Tapped AFTER a button's
            // Click: so a click on INSTALL showed this window on top, and then the same click's
            // Tapped reached the app manager and put it back above it. Background priority runs
            // after the pointer event has been dispatched in full. A no-op when it is already
            // active, as it is whenever nothing took activation back.
            Dispatcher.UIThread.Post(window.Activate, DispatcherPriority.Background);

            return window;
        }
    }
}
