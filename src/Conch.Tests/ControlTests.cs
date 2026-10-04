using Conch.Cli;
using Conch.Services.Control;
using Xunit;

namespace Conch.Tests;

/// <summary>Reading `conch` command lines.</summary>
public class ConchCliTests
{
    private static readonly string Cwd = Path.GetTempPath();

    private static ControlRequest Request(params string[] args)
    {
        var parse = ConchCli.Parse(args, caller: "w2", Cwd);
        Assert.Null(parse.Error);
        return parse.Request!;
    }

    private static string Error(params string[] args) => ConchCli.Parse(args, "w2", Cwd).Error!;

    [Theory]
    [InlineData(new[] { "--session" }, false)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "run", "htop" }, true)]
    [InlineData(new[] { "--json", "windows" }, true)]
    [InlineData(new[] { "--help" }, true)]
    public void OnlyAVerbMakesItACliCall(string[] args, bool expected)
    {
        // Starting the shell takes flags only; a verb means a script is talking to one.
        Assert.Equal(expected, ConchCli.IsCommand(args));
    }

    [Fact]
    public void RunTakesItsOptionsThenTheCommandWhole()
    {
        var request = Request("run", "--title", "Logs", "--size", "100x30", "--wait", "tail", "-f", "--lines", "5", "x.log");

        Assert.Equal("Logs", request.Title);
        Assert.Equal("100x30", request.Size);
        Assert.True(request.Wait);
        // The command's own options are its own.
        Assert.Equal(new[] { "tail", "-f", "--lines", "5", "x.log" }, request.Args);
    }

    [Fact]
    public void DoubleDashEndsOptions()
    {
        Assert.Equal(new[] { "--title" }, Request("run", "--", "--title").Args);
    }

    [Fact]
    public void OpenResolvesPathsAgainstTheCallersDirectoryButNotLinks()
    {
        var request = Request("open", "notes.md", "https://example.com/a");

        Assert.Equal(Path.Combine(Cwd, "notes.md"), request.Args[0]);
        Assert.Equal("https://example.com/a", request.Args[1]);
    }

    [Fact]
    public void WindowCommandsDefaultToTheCallersOwnWindow()
    {
        var request = Request("close");

        Assert.Null(request.Window);
        Assert.Equal("w2", request.Caller);
    }

    [Fact]
    public void TitleTakesAnOptionalWindowThenTheText()
    {
        Assert.Equal(("w5", "Build"), (Request("title", "w5", "Build").Window, Request("title", "w5", "Build").Args[0]));
        Assert.Null(Request("title", "Build").Window);
    }

    [Fact]
    public void GlobalOptionsComeBeforeTheVerb()
    {
        var parse = ConchCli.Parse(["--socket", "/tmp/x.sock", "--json", "windows"], null, Cwd);

        Assert.Equal("/tmp/x.sock", parse.Socket);
        Assert.True(parse.Json);
        Assert.Null(parse.Request!.Caller);
    }

    [Fact]
    public void MistakesAreSaid()
    {
        Assert.Contains("Unknown command", Error("frobnicate"));
        Assert.Contains("no option", Error("windows", "--bogus"));
        Assert.Contains("one window", Error("focus", "w1", "w2"));
        Assert.Contains("--title needs", Error("run", "--title"));
    }

    [Fact]
    public void NoVerbIsHelp()
    {
        Assert.True(ConchCli.Parse(["--json"], null, Cwd).Help);
        Assert.True(ConchCli.Parse(["help"], null, Cwd).Help);
    }
}

/// <summary>Finding the running Conch.</summary>
public class ControlEndpointTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "conch-ep-" + Guid.NewGuid().ToString("n"));

    public ControlEndpointTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AGivenSocketWinsThenTheEnvironmentThenTheOnlyOneRunning()
    {
        Assert.Equal("given", ControlEndpoint.Choose("given", "env", ["a"]).Socket);
        Assert.Equal("env", ControlEndpoint.Choose(null, "env", ["a"]).Socket);
        Assert.Equal("a", ControlEndpoint.Choose(null, null, ["a"]).Socket);
    }

    [Fact]
    public void NoneRunningAndSeveralRunningAreBothSaid()
    {
        Assert.Equal(ControlExit.NotRunning, ControlEndpoint.Choose(null, null, []).Code);
        Assert.Equal(ControlExit.Usage, ControlEndpoint.Choose(null, null, ["a", "b"]).Code);
    }

    [Fact]
    public void ASocketLeftByADeadConchIsRemoved()
    {
        File.WriteAllText(ControlEndpoint.PathFor(111, _dir), "");
        File.WriteAllText(ControlEndpoint.PathFor(222, _dir), "");

        var running = ControlEndpoint.Running(_dir, pid => pid == 222);

        Assert.Equal(new[] { ControlEndpoint.PathFor(222, _dir) }, running);
        Assert.False(File.Exists(ControlEndpoint.PathFor(111, _dir)));
    }
}

/// <summary>What the shell does with a request.</summary>
public class ControlDispatcherTests
{
    private sealed class FakeShell : IControlShell
    {
        public List<WindowInfo> List { get; } = [new("w1", "nano", "normal", false), new("w2", "btop", "normal", true)];
        public List<(string, WindowAction)> Acted { get; } = [];
        public List<string> Opened { get; } = [];
        public TaskCompletionSource<int> Exit { get; } = new();
        public (IReadOnlyList<string> Command, RunOptions Options)? Ran;

        public IReadOnlyList<WindowInfo> Windows() => List;
        public ControlStatus Status() => new("1.0", false, "/s", List.Count);

        public bool Act(string windowId, WindowAction action)
        {
            if (List.All(w => w.Id != windowId)) return false;
            Acted.Add((windowId, action));
            return true;
        }

        public bool SetTitle(string windowId, string title) => List.Any(w => w.Id == windowId);

        public string? Open(string path, string? appId)
        {
            if (path.EndsWith("missing")) return "No such file";
            Opened.Add(path);
            return null;
        }

        public Opened Run(IReadOnlyList<string> command, RunOptions options)
        {
            Ran = (command, options);
            return new Opened("w9", Exit.Task);
        }

        public (Opened? Window, string? Problem) Launch(string appId, IReadOnlyList<string> args)
            => appId == "nano" ? (new Opened("w8", Exit.Task), null) : (null, "No app");

        public Task<int>? WhenClosed(string windowId) => List.Any(w => w.Id == windowId) ? Exit.Task : null;
    }

    private readonly FakeShell _shell = new();

    private Task<ControlResponse> Handle(ControlRequest request) => new ControlDispatcher(_shell).HandleAsync(request);

    [Fact]
    public async Task SelfIsTheCallersWindow()
    {
        var response = await Handle(new ControlRequest { Verb = "minimize", Caller = "w1" });

        Assert.Equal(ControlExit.Ok, response.Code);
        Assert.Equal(("w1", WindowAction.Minimize), _shell.Acted.Single());
    }

    [Fact]
    public async Task ActiveIsTheActiveWindow()
    {
        await Handle(new ControlRequest { Verb = "close", Window = "active" });

        Assert.Equal(("w2", WindowAction.Close), _shell.Acted.Single());
    }

    [Fact]
    public async Task SelfOutsideConchIsRefused()
    {
        var response = await Handle(new ControlRequest { Verb = "focus" });

        Assert.Equal(ControlExit.NoSuchWindow, response.Code);
        Assert.Contains("Not running in a Conch window", response.Error);
    }

    [Fact]
    public async Task AWindowThatIsGoneIsSaid()
    {
        Assert.Equal(ControlExit.NoSuchWindow, (await Handle(new ControlRequest { Verb = "focus", Window = "w7" })).Code);
    }

    [Fact]
    public async Task RunReturnsTheNewWindowAtOnceWithoutWait()
    {
        var response = await Handle(new ControlRequest { Verb = "run", Args = ["htop"], Size = "120x40", Cwd = "/tmp" });

        Assert.Equal("w9", response.WindowId);
        Assert.Equal(ControlExit.Ok, response.Code);
        Assert.Equal((120, 40, "/tmp"), (_shell.Ran!.Value.Options.Cols, _shell.Ran.Value.Options.Rows, _shell.Ran.Value.Options.Cwd));
    }

    [Fact]
    public async Task RunWithWaitEndsWithTheCommandsExitCode()
    {
        var pending = Handle(new ControlRequest { Verb = "run", Args = ["make"], Wait = true });
        Assert.False(pending.IsCompleted);

        _shell.Exit.SetResult(7);

        Assert.Equal(7, (await pending).Code);
    }

    [Fact]
    public async Task ABadSizeIsAUsageError()
    {
        Assert.Equal(ControlExit.Usage, (await Handle(new ControlRequest { Verb = "run", Args = ["x"], Size = "big" })).Code);
    }

    [Fact]
    public async Task OpenStopsAtTheFirstFileItCannotOpen()
    {
        var response = await Handle(new ControlRequest { Verb = "open", Args = ["/a", "/b.missing", "/c"] });

        Assert.Equal(ControlExit.Failed, response.Code);
        Assert.Equal(new[] { "/a" }, _shell.Opened);
    }

    [Fact]
    public async Task LaunchingAnUnknownAppFails()
    {
        Assert.Equal(ControlExit.Failed, (await Handle(new ControlRequest { Verb = "launch", Args = ["nope"] })).Code);
        Assert.Equal("w8", (await Handle(new ControlRequest { Verb = "launch", Args = ["nano"] })).WindowId);
    }

    [Fact]
    public async Task WaitEndsWhenTheWindowCloses()
    {
        var pending = Handle(new ControlRequest { Verb = "wait", Window = "w1" });
        _shell.Exit.SetResult(0);

        Assert.Equal(ControlExit.Ok, (await pending).Code);
    }

    [Theory]
    [InlineData("100x30", true)]
    [InlineData("100X30", true)]
    [InlineData("0x30", false)]
    [InlineData("100", false)]
    [InlineData("axb", false)]
    public void SizesAreColumnsByRows(string text, bool valid)
    {
        Assert.Equal(valid, ControlDispatcher.TryParseSize(text, out _, out _));
    }

    [Fact]
    public void WindowIdsAreStableAndNeverReused()
    {
        var ids = new WindowIds();
        object a = new(), b = new();

        Assert.Equal("w1", ids.IdOf(a));
        Assert.Equal("w2", ids.IdOf(b));
        Assert.Equal("w1", ids.IdOf(a));
        Assert.Same(b, ids.Find(new[] { a, b }, "W2"));
    }
}

/// <summary>A request over a real socket, as the CLI and the shell exchange it.</summary>
public class ControlSocketTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ARequestGoesInAndItsAnswerComesBack()
    {
        var path = ControlEndpoint.PathFor(4242, _dir);
        var exit = new TaskCompletionSource<int>();
        using var server = new ControlServer(path, async request =>
            request.Wait
                ? new ControlResponse { Code = await exit.Task, WindowId = "w3" }
                : new ControlResponse { Windows = [new("w1", request.Verb, "normal", true)] });
        server.Start();

        var listed = await Task.Run(() => ConchCli.Send(path, new ControlRequest { Verb = "windows" }));
        Assert.Equal("windows", listed!.Windows!.Single().Title);

        // A waiting request holds its connection until the answer exists.
        var waiting = Task.Run(() => ConchCli.Send(path, new ControlRequest { Verb = "run", Args = ["x"], Wait = true }));
        await Task.Delay(200);
        Assert.False(waiting.IsCompleted);
        exit.SetResult(5);
        Assert.Equal(5, (await waiting)!.Code);
    }

    [Fact]
    public void TheSocketIsRemovedWhenTheShellStops()
    {
        var path = ControlEndpoint.PathFor(4343, _dir);
        var server = new ControlServer(path, _ => Task.FromResult(new ControlResponse()));
        server.Start();
        Assert.True(File.Exists(path));

        server.Dispose();

        Assert.False(File.Exists(path));
    }
}
