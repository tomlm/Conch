using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Conch.Services.Control;

namespace Conch.Cli
{
    /// <summary>The result of reading a command line.</summary>
    /// <param name="Request">What to send, or null for help or a usage error.</param>
    /// <param name="Error">Why the command line makes no sense.</param>
    public sealed record CliParse(ControlRequest? Request, string? Socket, bool Json, bool Help, string? Error);

    /// <summary>
    /// <c>conch &lt;verb&gt;</c>: scripts and terminals talking to the running Conch.
    /// </summary>
    /// <remarks>
    /// The same binary as the shell, branched before Avalonia starts, so a call costs .NET
    /// startup and one round trip on a local socket. Everything that decides anything happens
    /// in the shell; this only reads the command line, sends it, and prints the answer.
    /// </remarks>
    public static class ConchCli
    {
        public static readonly IReadOnlySet<string> Verbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "status", "windows", "open", "run", "launch", "wait", "title",
            "focus", "close", "minimize", "maximize", "restore", "help",
            "move", "tile", "notify", "attention", "confirm", "input", "pick",
        };

        private static readonly string[] WindowVerbs = ["focus", "close", "minimize", "maximize", "restore", "wait", "move", "attention"];

        public const string Usage = """
            Usage: conch [--socket PATH] [--json] <command> [options]

            Windows are named by id (w3, from `conch windows`), self (the window you are in; the
            default) or active.

              open [--with APP] FILE|URL...      open with the app for its type, as Files does
              run [options] [--] COMMAND...      run a command in a new window
                    --title T  --size 100x30  --maximize
                    --keep     keep the window when the command ends
                    --wait     wait for it to end; exit with its exit code
              launch [--wait] APP [ARGS...]      start a catalog app by id (nano, btop...)
              windows                            list windows
              focus|close|minimize|maximize|restore [WINDOW]
              title [WINDOW] TEXT                rename a window
              wait [WINDOW]                      wait for a window to close; exit with its code
              move [WINDOW] [--x N] [--y N] [--size 80x24]   place a window, in screen cells
              tile [WINDOW...] [--grid|--columns|--rows]     arrange windows (default: all shown)
              notify [--title T] [--type info|success|warning|error] [--seconds N] MESSAGE
              attention [WINDOW]                 mark a window in the window list until focused
              confirm [--title T] QUESTION       ask yes or no; exit 0 for yes, 1 for no
              input [--title T] [--default V] PROMPT   ask for text; prints it, exit 1 if cancelled
              pick [--folder] [--start DIR]      choose a file or folder; prints its path
              status                             version, session and socket

            Exit codes: 0 ok, 1 failed, 2 usage, 3 Conch not running, 4 no such window.
            """;

        /// <summary>
        /// True when <paramref name="args"/> is a CLI call rather than the shell being started,
        /// which takes only flags such as --session.
        /// </summary>
        public static bool IsCommand(IReadOnlyList<string> args)
        {
            for (var i = 0; i < args.Count; i++)
            {
                switch (args[i])
                {
                    case "--socket":
                        i++;
                        continue;
                    case "--json" or "--session":
                        continue;
                    case "--help" or "-h":
                        return true;
                    default:
                        return Verbs.Contains(args[i]);
                }
            }

            return false;
        }

        /// <summary>Reads a command line into a request.</summary>
        /// <param name="caller">The caller's window id, from its environment.</param>
        /// <param name="cwd">The caller's working directory.</param>
        public static CliParse Parse(IReadOnlyList<string> args, string? caller, string cwd)
        {
            string? socket = null;
            var json = false;
            var i = 0;

            // Options before the verb belong to the CLI itself.
            for (; i < args.Count; i++)
            {
                if (args[i] == "--socket" && i + 1 < args.Count)
                {
                    socket = args[++i];
                }
                else if (args[i] == "--json")
                {
                    json = true;
                }
                else if (args[i] == "--session")
                {
                    // The shell's own flag, which a wrapper may put in front of everything.
                }
                else
                {
                    break;
                }
            }

            if (i >= args.Count || args[i] is "help" or "--help" or "-h")
            {
                return new CliParse(null, socket, json, Help: true, null);
            }

            var verb = args[i++];
            if (!Verbs.Contains(verb))
            {
                return Fail($"Unknown command {verb}.");
            }

            string? title = null, size = null, with = null, layout = null, kind = null;
            int? x = null, y = null, seconds = null;
            string? initial = null, start = null;
            var folder = false;
            bool wait = false, keep = false, maximize = false;
            var positional = new List<string>();

            // run and launch take a command whose own options must not be read as ours: theirs
            // start at the first word that is not one of ours, or after --.
            var commandFollows = verb is "run" or "launch";

            for (; i < args.Count; i++)
            {
                var arg = args[i];

                if (arg == "--")
                {
                    positional.AddRange(args.Skip(i + 1));
                    break;
                }

                if (commandFollows && positional.Count > 0)
                {
                    positional.Add(arg);
                    continue;
                }

                switch (arg)
                {
                    case "--json": json = true; continue;
                    case "--wait" when verb is "run" or "launch": wait = true; continue;
                    case "--keep" when verb == "run": keep = true; continue;
                    case "--maximize" when verb == "run": maximize = true; continue;
                    case "--grid" or "--columns" or "--rows" when verb == "tile":
                        layout = arg[2..];
                        continue;
                    case "--x" or "--y" or "--seconds" when verb is "move" or "notify":
                    {
                        if (++i >= args.Count || !int.TryParse(args[i], out var number))
                        {
                            return Fail($"{arg} needs a number.");
                        }

                        switch (arg)
                        {
                            case "--x" when verb == "move": x = number; break;
                            case "--y" when verb == "move": y = number; break;
                            case "--seconds" when verb == "notify": seconds = number; break;
                            default: return Fail($"{verb} has no option {arg}.");
                        }

                        continue;
                    }
                    case "--type" when verb == "notify":
                        if (++i >= args.Count) return Fail("--type needs a value.");
                        kind = args[i];
                        continue;
                    case "--size" when verb == "move":
                        if (++i >= args.Count) return Fail("--size needs a value, as 80x24.");
                        size = args[i];
                        continue;
                    case "--folder" when verb == "pick":
                        folder = true;
                        continue;
                    case "--start" when verb == "pick":
                        if (++i >= args.Count) return Fail("--start needs a folder.");
                        start = Path.GetFullPath(args[i], cwd);
                        continue;
                    case "--default" when verb == "input":
                        if (++i >= args.Count) return Fail("--default needs a value.");
                        initial = args[i];
                        continue;
                    case "--title" when verb is "notify" or "confirm" or "input":
                        if (++i >= args.Count) return Fail("--title needs a value.");
                        title = args[i];
                        continue;
                    case "--title" when verb == "run":
                        if (++i >= args.Count) return Fail("--title needs a value.");
                        title = args[i];
                        continue;
                    case "--size" when verb == "run":
                        if (++i >= args.Count) return Fail("--size needs a value, as 100x30.");
                        size = args[i];
                        continue;
                    case "--with" when verb == "open":
                        if (++i >= args.Count) return Fail("--with needs an app id.");
                        with = args[i];
                        continue;
                }

                if (arg.StartsWith("--", StringComparison.Ordinal) && !commandFollows)
                {
                    return Fail($"{verb} has no option {arg}.");
                }

                positional.Add(arg);
            }

            string? window = null;
            if (WindowVerbs.Contains(verb))
            {
                if (positional.Count > 1)
                {
                    return Fail($"{verb} takes one window.");
                }

                window = positional.FirstOrDefault();
                positional.Clear();
            }
            else if (verb == "title")
            {
                if (positional.Count is 0 or > 2)
                {
                    return Fail("Usage: conch title [WINDOW] TEXT");
                }

                if (positional.Count == 2)
                {
                    window = positional[0];
                    positional.RemoveAt(0);
                }
            }
            else if (verb is "status" or "windows" && positional.Count > 0)
            {
                return Fail($"{verb} takes no arguments.");
            }
            else if (verb is "notify" or "confirm" or "input" && positional.Count != 1)
            {
                return Fail($"Usage: conch {verb} [options] TEXT (quote text with spaces)");
            }
            else if (verb == "pick" && positional.Count > 0)
            {
                return Fail("pick takes no arguments; use --start DIR.");
            }

            if (verb == "open")
            {
                // Resolved here, where the caller's directory is known; links pass through.
                positional = positional
                    .Select(p => p.Contains("://", StringComparison.Ordinal) ? p : Path.GetFullPath(p, cwd))
                    .ToList();
            }

            var request = new ControlRequest
            {
                Verb = verb,
                Args = positional,
                Window = window,
                Caller = string.IsNullOrEmpty(caller) ? null : caller,
                Cwd = cwd,
                Title = title,
                Size = size,
                With = with,
                Wait = wait,
                Keep = keep,
                Maximize = maximize,
                X = x,
                Y = y,
                Layout = layout,
                Kind = kind,
                Seconds = seconds,
                Default = initial,
                Folder = folder,
                Start = start,
            };

            return new CliParse(request, socket, json, Help: false, null);

            CliParse Fail(string error) => new(null, socket, json, Help: false, error);
        }

        /// <summary>Runs a CLI call; the process exit code.</summary>
        public static int Run(string[] args)
        {
            var parse = Parse(args,
                Environment.GetEnvironmentVariable(ControlEndpoint.WindowVariable),
                Environment.CurrentDirectory);

            if (parse.Help)
            {
                Console.WriteLine(Usage);
                return ControlExit.Ok;
            }

            if (parse.Error != null)
            {
                Console.Error.WriteLine($"conch: {parse.Error}");
                return ControlExit.Usage;
            }

            var (socket, problem, code) = ControlEndpoint.Choose(
                parse.Socket,
                Environment.GetEnvironmentVariable(ControlEndpoint.SocketVariable),
                ControlEndpoint.Running());

            if (socket == null)
            {
                Console.Error.WriteLine($"conch: {problem}");
                return code;
            }

            ControlResponse? response;
            try
            {
                response = Send(socket, parse.Request!);
            }
            catch (SocketException)
            {
                Console.Error.WriteLine("conch: Conch is not running.");
                return ControlExit.NotRunning;
            }

            if (response == null)
            {
                Console.Error.WriteLine("conch: no answer from Conch.");
                return ControlExit.Failed;
            }

            Print(parse.Request!, response, parse.Json);
            return response.Code;
        }

        /// <summary>Sends one request and reads its answer, however long that takes.</summary>
        public static ControlResponse? Send(string socketPath, ControlRequest request)
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));

            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);

            writer.WriteLine(ControlJson.Write(request));
            var line = reader.ReadLine();
            return line == null ? null : ControlJson.Read<ControlResponse>(line);
        }

        private static void Print(ControlRequest request, ControlResponse response, bool json)
        {
            var verb = request.Verb;
            if (response.Error != null)
            {
                Console.Error.WriteLine($"conch: {response.Error}");
                return;
            }

            if (json)
            {
                object? payload = verb switch
                {
                    "windows" => response.Windows,
                    "status" => response.Status,
                    "input" or "pick" => response.Text == null ? null : new { text = response.Text },
                    _ => response.WindowId == null || request.Wait ? null : new { window = response.WindowId },
                };

                if (payload != null)
                {
                    Console.WriteLine(JsonSerializer.Serialize(payload, ControlJson.Options));
                }

                return;
            }

            switch (verb)
            {
                case "windows":
                    foreach (var w in response.Windows ?? [])
                    {
                        Console.WriteLine($"{w.Id,-5} {(w.Active ? "*" : " ")} {w.State,-9} {w.Title}");
                    }
                    break;

                case "status" when response.Status is { } s:
                    Console.WriteLine($"Conch {s.Version}, {(s.Session ? "the session" : "an application")}, {s.Windows} window(s)");
                    Console.WriteLine($"socket {s.Socket}");
                    break;

                case "input" or "pick" when response.Text != null:
                    Console.WriteLine(response.Text);
                    break;

                // The new window's id, so a script can act on it: id=$(conch run -- htop)
                case "run" or "launch" when response.WindowId != null && !request.Wait:
                    Console.WriteLine(response.WindowId);
                    break;
            }
        }
    }
}
