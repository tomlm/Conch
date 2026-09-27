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
        private const string DefaultFontFamily = "Cascadia Mono";

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
        /// Runs a command line typed by the user.
        /// </summary>
        public ManagedTerminalWindow LaunchCommandLine(string commandLine)
        {
            var command = ShellCommand.ForCommandLine(commandLine);
            Log.Info(LogCategory, $"Running '{commandLine}' as: {command}");
            return Open(command, title: command.Process, closeOnExit: true);
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

            return Open(command, tool.Name, closeOnExit: true, tool.Width, tool.Height);
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

            var window = Open(command, title, closeOnExit: false, width: 100, height: 30);

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

        private ManagedTerminalWindow Open(ResolvedCommand command, string? title, bool closeOnExit, int width = 80, int height = 25)
        {
            var window = new ManagedTerminalWindow
            {
                Process = command.Process,
                ProcessArgs = command.Args,
                Width = width,
                Height = height,
                FontFamily = DefaultFontFamily,
                CloseOnProcessExit = closeOnExit,
            };

            if (!string.IsNullOrWhiteSpace(title))
            {
                window.Title = title;
            }

            window.Show(_windows);
            return window;
        }
    }
}
