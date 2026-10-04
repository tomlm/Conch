namespace Conch.Services.Control
{
    /// <summary>What a request can ask the shell to do with a window.</summary>
    public enum WindowAction
    {
        Focus,
        Close,
        Minimize,
        Maximize,
        Restore,
    }

    /// <summary>Options for <c>conch run</c>.</summary>
    public sealed record RunOptions(string? Title, int? Cols, int? Rows, bool Keep, bool Maximize, string? Cwd);

    /// <summary>A window a request opened, and when its process ends.</summary>
    /// <param name="Exited">Completes with the exit code when the window's process ends.</param>
    public sealed record Opened(string WindowId, Task<int> Exited);

    /// <summary>
    /// The shell, as the control channel sees it. MainWindow implements it; tests fake it.
    /// </summary>
    /// <remarks>
    /// Called on the UI thread. Methods that can fail return a reason rather than throwing,
    /// which the dispatcher passes back to the CLI as its error message.
    /// </remarks>
    public interface IControlShell
    {
        IReadOnlyList<WindowInfo> Windows();

        ControlStatus Status();

        /// <summary>Acts on a window; false when there is no such window.</summary>
        bool Act(string windowId, WindowAction action);

        /// <summary>Retitles a window; false when there is no such window.</summary>
        bool SetTitle(string windowId, string title);

        /// <summary>Opens a file as Files would, or with <paramref name="appId"/>; the reason when it cannot.</summary>
        string? Open(string path, string? appId);

        /// <summary>Runs a command in a new window.</summary>
        Opened Run(IReadOnlyList<string> command, RunOptions options);

        /// <summary>Starts a catalog app; null with a reason when there is no such app.</summary>
        (Opened? Window, string? Problem) Launch(string appId, IReadOnlyList<string> args);

        /// <summary>Completes with the window's exit code when it goes; null when there is no such window.</summary>
        Task<int>? WhenClosed(string windowId);
    }

    /// <summary>
    /// Turns a request into calls on the shell: works out which window is meant, checks what
    /// was asked makes sense, and waits where asked to.
    /// </summary>
    public sealed class ControlDispatcher
    {
        private readonly IControlShell _shell;

        public ControlDispatcher(IControlShell shell)
        {
            _shell = shell;
        }

        public async Task<ControlResponse> HandleAsync(ControlRequest request)
        {
            switch (request.Verb)
            {
                case "status":
                    return new ControlResponse { Status = _shell.Status() };

                case "windows":
                    return new ControlResponse { Windows = _shell.Windows() };

                case "focus":
                case "close":
                case "minimize":
                case "maximize":
                case "restore":
                {
                    var (id, problem) = Target(request);
                    if (id == null)
                    {
                        return ControlResponse.Fail(ControlExit.NoSuchWindow, problem!);
                    }

                    var action = Enum.Parse<WindowAction>(request.Verb, ignoreCase: true);
                    return _shell.Act(id, action)
                        ? ControlResponse.Success(id)
                        : ControlResponse.Fail(ControlExit.NoSuchWindow, $"No window {id}.");
                }

                case "title":
                {
                    if (request.Args.Count != 1)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Give the new title.");
                    }

                    var (id, problem) = Target(request);
                    if (id == null)
                    {
                        return ControlResponse.Fail(ControlExit.NoSuchWindow, problem!);
                    }

                    return _shell.SetTitle(id, request.Args[0])
                        ? ControlResponse.Success(id)
                        : ControlResponse.Fail(ControlExit.NoSuchWindow, $"No window {id}.");
                }

                case "open":
                {
                    if (request.Args.Count == 0)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Give a file to open.");
                    }

                    foreach (var path in request.Args)
                    {
                        if (_shell.Open(path, request.With) is { } problem)
                        {
                            return ControlResponse.Fail(ControlExit.Failed, problem);
                        }
                    }

                    return ControlResponse.Success();
                }

                case "run":
                {
                    if (request.Args.Count == 0)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Give a command to run.");
                    }

                    int? cols = null, rows = null;
                    if (request.Size != null)
                    {
                        if (!TryParseSize(request.Size, out var c, out var r))
                        {
                            return ControlResponse.Fail(ControlExit.Usage, $"Size is columns x rows, as 100x30, not {request.Size}.");
                        }

                        (cols, rows) = (c, r);
                    }

                    var opened = _shell.Run(request.Args,
                        new RunOptions(request.Title, cols, rows, request.Keep, request.Maximize, request.Cwd));
                    return await Finish(opened, request.Wait);
                }

                case "launch":
                {
                    if (request.Args.Count == 0)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Give the id of an app to launch.");
                    }

                    var (opened, problem) = _shell.Launch(request.Args[0], request.Args.Skip(1).ToList());
                    return opened == null
                        ? ControlResponse.Fail(ControlExit.Failed, problem ?? $"Cannot launch {request.Args[0]}.")
                        : await Finish(opened, request.Wait);
                }

                case "wait":
                {
                    var (id, problem) = Target(request);
                    if (id == null)
                    {
                        return ControlResponse.Fail(ControlExit.NoSuchWindow, problem!);
                    }

                    var closed = _shell.WhenClosed(id);
                    return closed == null
                        ? ControlResponse.Fail(ControlExit.NoSuchWindow, $"No window {id}.")
                        : new ControlResponse { Code = await closed, WindowId = id };
                }

                default:
                    return ControlResponse.Fail(ControlExit.Usage, $"Unknown command {request.Verb}.");
            }
        }

        /// <summary>Waits for the window's process when asked, so its exit code becomes the CLI's.</summary>
        private static async Task<ControlResponse> Finish(Opened opened, bool wait)
            => wait
                ? new ControlResponse { Code = await opened.Exited, WindowId = opened.WindowId }
                : ControlResponse.Success(opened.WindowId);

        /// <summary>
        /// The id of the window a request means: an id as given, the caller's own for
        /// <c>self</c> (the default), or the active one.
        /// </summary>
        private (string? Id, string? Problem) Target(ControlRequest request)
        {
            var target = string.IsNullOrEmpty(request.Window) ? "self" : request.Window;

            if (string.Equals(target, "self", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(request.Caller)
                    ? (null, "Not running in a Conch window; name one, as w3 or active.")
                    : (request.Caller, null);
            }

            if (string.Equals(target, "active", StringComparison.OrdinalIgnoreCase))
            {
                var active = _shell.Windows().FirstOrDefault(w => w.Active);
                return active == null ? (null, "No window is active.") : (active.Id, null);
            }

            return (target, null);
        }

        /// <summary>Parses <c>100x30</c>.</summary>
        public static bool TryParseSize(string text, out int cols, out int rows)
        {
            cols = rows = 0;
            var parts = text.ToLowerInvariant().Split('x');
            return parts.Length == 2
                && int.TryParse(parts[0], out cols) && int.TryParse(parts[1], out rows)
                && cols is > 0 and <= 1000 && rows is > 0 and <= 1000;
        }
    }
}
