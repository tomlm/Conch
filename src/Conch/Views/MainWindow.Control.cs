using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Conch.Controls;
using Conch.Services.Control;
using Conch.Services.Roles;
using Conch.Utilities;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views
{
    /// <summary>
    /// The shell's half of <c>conch &lt;verb&gt;</c>: what scripts in its terminals can ask of it.
    /// </summary>
    public partial class MainWindow : IControlShell
    {
        private ControlServer? _control;

        /// <summary>
        /// Starts listening for <c>conch</c> calls, and tells every terminal opened from now on
        /// where to find this Conch.
        /// </summary>
        /// <remarks>
        /// A failure here costs the CLI, not the shell: it is logged, and Conch carries on
        /// without a socket.
        /// </remarks>
        private void StartControl()
        {
            var path = ControlEndpoint.PathFor(Environment.ProcessId);
            var dispatcher = new ControlDispatcher(this);

            // Requests arrive on socket threads; the windows they touch belong to the UI thread.
            var server = new ControlServer(path,
                request => Dispatcher.UIThread.InvokeAsync(() => dispatcher.HandleAsync(request)));
            try
            {
                server.Start();
                _control = server;
                ShellControl.SocketPath = path;
                Closed += (_, _) => server.Dispose();
            }
            catch (Exception ex)
            {
                server.Dispose();
                Log.Warning("Control", $"conch commands will not reach this session: {ex.Message}");
            }
        }

        private IEnumerable<ManagedWindow> OpenWindows => Windows.Windows.OfType<ManagedWindow>();

        private ManagedWindow? FindWindow(string id) => ShellControl.Ids.Find(OpenWindows, id);

        IReadOnlyList<WindowInfo> IControlShell.Windows()
            => OpenWindows
                .Select(w => new WindowInfo(
                    ShellControl.Ids.IdOf(w),
                    w.Title ?? string.Empty,
                    w.WindowState switch
                    {
                        WindowState.Minimized => "minimized",
                        WindowState.Maximized => "maximized",
                        _ => "normal",
                    },
                    w.IsActive))
                .ToList();

        ControlStatus IControlShell.Status()
            => new(
                Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?.Split('+')[0] ?? "unknown",
                CommandLineOptions.Current.OwnsSession,
                ShellControl.SocketPath ?? string.Empty,
                OpenWindows.Count());

        bool IControlShell.Act(string windowId, WindowAction action)
        {
            if (FindWindow(windowId) is not { } window)
            {
                return false;
            }

            switch (action)
            {
                case WindowAction.Focus:
                    window.Activate();
                    break;
                case WindowAction.Close:
                    window.Close();
                    break;
                case WindowAction.Minimize:
                    window.WindowState = WindowState.Minimized;
                    break;
                case WindowAction.Maximize:
                    window.WindowState = WindowState.Maximized;
                    break;
                case WindowAction.Restore:
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    break;
            }

            return true;
        }

        bool IControlShell.SetTitle(string windowId, string title)
        {
            if (FindWindow(windowId) is not { } window)
            {
                return false;
            }

            window.Title = title;
            return true;
        }

        string? IControlShell.Open(string path, string? appId)
        {
            if (appId != null)
            {
                var tool = App.Tools.FirstOrDefault(t => string.Equals(t.Id, appId, StringComparison.OrdinalIgnoreCase));
                if (tool == null)
                {
                    return $"No app with id {appId}.";
                }

                OpenWithApp(tool, path);
                return null;
            }

            if (path.Contains("://", StringComparison.Ordinal))
            {
                return "Opening links is not supported yet.";
            }

            if (Directory.Exists(path))
            {
                OpenRole(ShellRoles.FileExplorer, path);
                return null;
            }

            if (!File.Exists(path))
            {
                return $"No such file: {path}";
            }

            OpenFile(path);
            return null;
        }

        Opened IControlShell.Run(IReadOnlyList<string> command, RunOptions options)
        {
            var resolved = ShellCommand.ForProcess(command[0], command.Skip(1));
            var window = Apps.Run(resolved,
                options.Title ?? string.Join(' ', command),
                options.Cols, options.Rows, options.Keep, options.Cwd);

            if (options.Maximize)
            {
                window.WindowState = WindowState.Maximized;
            }

            return new Opened(ShellControl.Ids.IdOf(window), WhenExited(window));
        }

        (Opened? Window, string? Problem) IControlShell.Launch(string appId, IReadOnlyList<string> args)
        {
            var tool = App.Tools.FirstOrDefault(t => string.Equals(t.Id, appId, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
            {
                return (null, $"No app with id {appId}.");
            }

            if (!tool.IsInstalled)
            {
                return (null, $"{tool.Name} is not installed.");
            }

            return Apps.LaunchTool(tool, args) is { } window
                ? (new Opened(ShellControl.Ids.IdOf(window), WhenExited(window)), null)
                : (null, $"{tool.Name} could not be started with those arguments.");
        }

        Task<int>? IControlShell.WhenClosed(string windowId)
        {
            if (FindWindow(windowId) is not { } window)
            {
                return null;
            }

            var closed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            // A terminal reports how its process ended; a window that never had one -- Files,
            // Preferences -- simply closed, which is success.
            window.Closed += (_, _) => closed.TrySetResult(
                window is ManagedTerminalWindow { ExitCode: { } code } ? Normalise(code) : 0);
            return closed.Task;
        }

        /// <summary>
        /// Completes when the window's process ends, with its exit code; or when the window is
        /// closed first, which ends the process and counts as failure.
        /// </summary>
        private static Task<int> WhenExited(ManagedTerminalWindow window)
        {
            var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (window.ExitCode is { } already)
            {
                exited.TrySetResult(Normalise(already));
                return exited.Task;
            }

            window.ProcessExited += (_, e) => exited.TrySetResult(e.ExitCodeKnown ? Normalise(e.ExitCode) : ControlExit.Failed);
            window.Closed += (_, _) => exited.TrySetResult(ControlExit.Failed);
            return exited.Task;
        }

        /// <summary>An exit code a process can end with: -1, "unknown", becomes failure.</summary>
        private static int Normalise(int code) => code < 0 ? ControlExit.Failed : code;
    }
}
