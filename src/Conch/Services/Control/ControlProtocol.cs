using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conch.Services.Control
{
    /// <summary>Exit codes the CLI returns, and the server sends back to choose them.</summary>
    public static class ControlExit
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int Usage = 2;
        public const int NotRunning = 3;
        public const int NoSuchWindow = 4;
    }

    /// <summary>
    /// One request from <c>conch &lt;verb&gt;</c> to the running shell, sent as one line of JSON.
    /// </summary>
    public sealed record ControlRequest
    {
        public string Verb { get; init; } = string.Empty;

        /// <summary>Positional arguments: files to open, the command to run, a title.</summary>
        public IReadOnlyList<string> Args { get; init; } = [];

        /// <summary>The window acted on: an id, <c>self</c> or <c>active</c>.</summary>
        public string? Window { get; init; }

        /// <summary>The window the caller runs in (its <c>CONCH_WINDOW</c>), which <c>self</c> means.</summary>
        public string? Caller { get; init; }

        /// <summary>The caller's working directory, where a new window starts.</summary>
        public string? Cwd { get; init; }

        public string? Title { get; init; }

        /// <summary>Columns by rows, as <c>100x30</c>.</summary>
        public string? Size { get; init; }

        /// <summary>The app id to open files with, instead of the one chosen for their type.</summary>
        public string? With { get; init; }

        public bool Wait { get; init; }

        public bool Keep { get; init; }

        public bool Maximize { get; init; }

        /// <summary>Where to move a window to, in screen cells.</summary>
        public int? X { get; init; }

        public int? Y { get; init; }

        /// <summary>grid, columns or rows, for <c>tile</c>.</summary>
        public string? Layout { get; init; }

        /// <summary>info, success, warning or error, for <c>notify</c>.</summary>
        public string? Kind { get; init; }

        /// <summary>How long a notification stays, for <c>notify</c>.</summary>
        public int? Seconds { get; init; }
    }

    /// <summary>The shell's answer, one line of JSON.</summary>
    public sealed record ControlResponse
    {
        /// <summary>The exit code the CLI should end with.</summary>
        public int Code { get; init; }

        public string? Error { get; init; }

        /// <summary>The window a request opened, for <c>run</c> and <c>launch</c>.</summary>
        public string? WindowId { get; init; }

        public IReadOnlyList<WindowInfo>? Windows { get; init; }

        public ControlStatus? Status { get; init; }

        public static ControlResponse Success(string? windowId = null) => new() { Code = ControlExit.Ok, WindowId = windowId };

        public static ControlResponse Fail(int code, string error) => new() { Code = code, Error = error };
    }

    /// <summary>A window, as <c>conch windows</c> lists it.</summary>
    /// <param name="State">normal, minimized or maximized.</param>
    public sealed record WindowInfo(string Id, string Title, string State, bool Active);

    /// <summary>What <c>conch status</c> reports.</summary>
    public sealed record ControlStatus(string Version, bool Session, string Socket, int Windows);

    /// <summary>The JSON both ends speak.</summary>
    public static class ControlJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

        public static T? Read<T>(string line) => JsonSerializer.Deserialize<T>(line, Options);
    }
}
