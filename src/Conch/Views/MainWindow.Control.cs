using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
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
        private WindowNotificationManager? _notifications;

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
            // Made now, not on the first notification: the manager joins the window's adorner
            // layer when its template is applied, and a notification shown before that is lost.
            _notifications = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 3,

                // Below the top bar, not over its clock and indicators.
                Margin = new Thickness(0, 1, 0, 0),
            };

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

        bool IControlShell.Move(string windowId, int? x, int? y, int? width, int? height)
        {
            if (FindWindow(windowId) is not { } window)
            {
                return false;
            }

            Place(window, x, y, width, height);
            return true;
        }

        string? IControlShell.Tile(IReadOnlyList<string>? windowIds, TileLayout layout)
        {
            List<ManagedWindow> windows;
            if (windowIds == null)
            {
                windows = OpenWindows.Where(w => w.WindowState != WindowState.Minimized).ToList();
            }
            else
            {
                windows = [];
                foreach (var id in windowIds)
                {
                    if (FindWindow(id) is not { } window)
                    {
                        return $"No window {id}.";
                    }

                    windows.Add(window);
                }
            }

            var cells = Tiling.Layout(windows.Count, (int)Windows.Bounds.Width, (int)Windows.Bounds.Height, layout);
            for (var i = 0; i < cells.Count; i++)
            {
                Place(windows[i], cells[i].X, cells[i].Y, cells[i].Width, cells[i].Height);
            }

            return null;
        }

        void IControlShell.Notify(string? title, string message, NotifyKind kind, TimeSpan duration, string? fromWindow)
        {
            _notifications?.Show(new Notification(
                title ?? "Conch",
                message,
                kind switch
                {
                    NotifyKind.Success => NotificationType.Success,
                    NotifyKind.Warning => NotificationType.Warning,
                    NotifyKind.Error => NotificationType.Error,
                    _ => NotificationType.Information,
                },
                duration,
                // Clicking a notification goes to whatever sent it: the build that finished.
                onClick: () =>
                {
                    if (fromWindow != null && FindWindow(fromWindow) is { } window)
                    {
                        window.WindowState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
                        window.Activate();
                    }
                }));
        }

        bool IControlShell.Attention(string windowId)
        {
            if (FindWindow(windowId) is not { } window)
            {
                return false;
            }

            WindowList.SetAttention(window);
            return true;
        }

        Task<bool> IControlShell.Confirm(string title, string question)
            => ReturningFocus(() => Confirm(title, question));

        Task<string?> IControlShell.Input(string title, string prompt, string? initial)
            => ReturningFocus(() => new InputDialog(title, prompt, initial).ShowDialog<string?>(this));

        Task<string?> IControlShell.Pick(bool folder, string? startAt)
            => ReturningFocus(() => FilesDialog.ForPicking(startAt, folder).ShowDialog<string?>(this));

        /// <summary>
        /// Shows a dialog, then gives the keyboard back to the window that was in use.
        /// </summary>
        /// <remarks>
        /// A dialog a script opens is a step in that script, and the terminal it runs in is
        /// where typing goes next. Left alone, focus went nowhere when the dialog closed, and
        /// the next keys were lost.
        /// </remarks>
        private async Task<T> ReturningFocus<T>(Func<Task<T>> show)
        {
            var before = Windows.ActiveWindow;
            try
            {
                return await show();
            }
            finally
            {
                if (before != null && Windows.Windows.Contains(before))
                {
                    before.Activate();
                    before.FocusContent();
                }
            }
        }

        /// <summary>
        /// Puts a window at a place and size in screen cells, leaving out what is not given.
        /// </summary>
        /// <remarks>
        /// Restored first, since a maximized window ignores where it is put; and a terminal sizes
        /// itself to its grid until told otherwise, which would undo a size given here.
        /// </remarks>
        private static void Place(ManagedWindow window, int? x, int? y, int? width, int? height)
        {
            if (window.WindowState != WindowState.Normal)
            {
                window.WindowState = WindowState.Normal;
            }

            if (width != null || height != null)
            {
                window.SizeToContent = SizeToContent.Manual;
                if (width != null)
                {
                    window.Width = width.Value;
                }

                if (height != null)
                {
                    window.Height = height.Value;
                }
            }

            if (x != null || y != null)
            {
                window.Position = new PixelPoint(x ?? window.Position.X, y ?? window.Position.Y);
            }
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
