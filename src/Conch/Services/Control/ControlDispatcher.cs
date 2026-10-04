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

    /// <summary>How a notification looks.</summary>
    public enum NotifyKind
    {
        Info,
        Success,
        Warning,
        Error,
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

        /// <summary>Opens a file or link as Files would, or with <paramref name="appId"/>; the reason when it cannot.</summary>
        Task<string?> Open(string path, string? appId);

        /// <summary>Runs a command in a new window.</summary>
        Opened Run(IReadOnlyList<string> command, RunOptions options);

        /// <summary>Starts a catalog app; null with a reason when there is no such app.</summary>
        (Opened? Window, string? Problem) Launch(string appId, IReadOnlyList<string> args);

        /// <summary>Completes with the window's exit code when it goes; null when there is no such window.</summary>
        Task<int>? WhenClosed(string windowId);

        /// <summary>Moves and sizes a window, in screen cells; false when there is no such window.</summary>
        bool Move(string windowId, int? x, int? y, int? width, int? height);

        /// <summary>
        /// Arranges windows over the desktop: those named, or every one not minimized. The
        /// reason when one of the named windows does not exist.
        /// </summary>
        string? Tile(IReadOnlyList<string>? windowIds, TileLayout layout);

        /// <summary>Shows a notification; clicking it brings <paramref name="fromWindow"/> forward.</summary>
        void Notify(string? title, string message, NotifyKind kind, TimeSpan duration, string? fromWindow);

        /// <summary>Marks a window as wanting attention until it is focused; false when there is no such window.</summary>
        bool Attention(string windowId);

        /// <summary>Asks yes or no; completes with the answer.</summary>
        Task<bool> Confirm(string title, string question);

        /// <summary>Asks for a line of text; completes with it, or null when cancelled.</summary>
        Task<string?> Input(string title, string prompt, string? initial);

        /// <summary>Lets the user choose a file or folder; completes with its path, or null when cancelled.</summary>
        Task<string?> Pick(bool folder, string? startAt);
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
                        if (await _shell.Open(path, request.With) is { } problem)
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

                case "move":
                {
                    var (id, problem) = Target(request);
                    if (id == null)
                    {
                        return ControlResponse.Fail(ControlExit.NoSuchWindow, problem!);
                    }

                    int? width = null, height = null;
                    if (request.Size != null)
                    {
                        if (!TryParseSize(request.Size, out var w, out var h))
                        {
                            return ControlResponse.Fail(ControlExit.Usage, $"Size is width x height, as 80x24, not {request.Size}.");
                        }

                        (width, height) = (w, h);
                    }

                    if (request.X == null && request.Y == null && width == null)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Give --x, --y or --size.");
                    }

                    return _shell.Move(id, request.X, request.Y, width, height)
                        ? ControlResponse.Success(id)
                        : ControlResponse.Fail(ControlExit.NoSuchWindow, $"No window {id}.");
                }

                case "tile":
                {
                    if (!Enum.TryParse<TileLayout>(request.Layout ?? "grid", ignoreCase: true, out var layout))
                    {
                        return ControlResponse.Fail(ControlExit.Usage, $"Layout is grid, columns or rows, not {request.Layout}.");
                    }

                    var ids = request.Args.Count == 0
                        ? null
                        : request.Args.Select(a => Target(request with { Window = a }).Id ?? a).ToList();
                    return _shell.Tile(ids, layout) is { } problem
                        ? ControlResponse.Fail(ControlExit.NoSuchWindow, problem)
                        : ControlResponse.Success();
                }

                case "notify":
                {
                    if (request.Args.Count != 1)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Give the message, quoted.");
                    }

                    if (!Enum.TryParse<NotifyKind>(request.Kind ?? "info", ignoreCase: true, out var kind))
                    {
                        return ControlResponse.Fail(ControlExit.Usage, $"Type is info, success, warning or error, not {request.Kind}.");
                    }

                    if (request.Seconds is <= 0 or > 3600)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Seconds is between 1 and 3600.");
                    }

                    _shell.Notify(request.Title, request.Args[0], kind,
                        TimeSpan.FromSeconds(request.Seconds ?? 5), request.Caller);
                    return ControlResponse.Success();
                }

                case "attention":
                {
                    var (id, problem) = Target(request);
                    if (id == null)
                    {
                        return ControlResponse.Fail(ControlExit.NoSuchWindow, problem!);
                    }

                    return _shell.Attention(id)
                        ? ControlResponse.Success(id)
                        : ControlResponse.Fail(ControlExit.NoSuchWindow, $"No window {id}.");
                }

                case "confirm":
                {
                    if (request.Args.Count != 1)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Give the question, quoted.");
                    }

                    // No is not a failure of the command, but a script needs it as an exit code:
                    // `if conch confirm "Deploy?"; then ...`.
                    return await _shell.Confirm(request.Title ?? "Confirm", request.Args[0])
                        ? ControlResponse.Success()
                        : new ControlResponse { Code = ControlExit.Failed };
                }

                case "input":
                {
                    if (request.Args.Count != 1)
                    {
                        return ControlResponse.Fail(ControlExit.Usage, "Give the prompt, quoted.");
                    }

                    var answer = await _shell.Input(request.Title ?? "Input", request.Args[0], request.Default);
                    return answer == null
                        ? new ControlResponse { Code = ControlExit.Failed }
                        : new ControlResponse { Text = answer };
                }

                case "pick":
                {
                    var path = await _shell.Pick(request.Folder, request.Start ?? request.Cwd);
                    return path == null
                        ? new ControlResponse { Code = ControlExit.Failed }
                        : new ControlResponse { Text = path };
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
