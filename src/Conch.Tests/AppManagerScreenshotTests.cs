using Avalonia;
using Avalonia.Media;
using Conch.Services;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// What the app manager's details pane shows in place of a screenshot, and when.
/// </summary>
/// <remarks>
/// The network and the decoder are both replaced: these are about the decisions -- which
/// picture belongs to which selection, and what the pane says when there is none -- not about
/// Skia or HTTP.
/// </remarks>
public class AppManagerScreenshotTests
{
    private sealed class FakeImage : IImage, IDisposable
    {
        public FakeImage(string tag) => Tag = tag;

        public string Tag { get; }
        public bool Disposed { get; private set; }
        public Size Size => new(10, 10);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) { }

        public void Dispose() => Disposed = true;
    }

    private static ToolViewModel Tool(string id, string screenshot = "") => new()
    {
        Id = id,
        Name = id,
        Command = id,
        Screenshot = screenshot,
    };

    private static AppManagerViewModel Manager(
        Func<string, CancellationToken, Task<byte[]?>> fetch,
        Func<byte[], IImage>? decode = null)
        => new(new AppViewModel(new ShellSettings(Path.GetTempPath())),
            fetch,
            decode ?? (bytes => new FakeImage(System.Text.Encoding.UTF8.GetString(bytes))));

    private static Task<byte[]?> Bytes(string text) => Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task AnAppWithoutAScreenshotSaysSo()
    {
        var manager = Manager((_, _) => Bytes("x"));

        manager.SelectedTool = Tool("plain");
        await manager.ScreenshotLoad;

        Assert.Null(manager.ScreenshotImage);
        Assert.Equal("No screenshot.", manager.ScreenshotStatus);
    }

    [Fact]
    public async Task TheSelectedAppsScreenshotIsFetchedAndShown()
    {
        string? asked = null;
        var manager = Manager((url, _) => { asked = url; return Bytes("lazygit"); });

        manager.SelectedTool = Tool("lazygit", "https://example.org/lazygit.png");
        await manager.ScreenshotLoad;

        Assert.Equal("https://example.org/lazygit.png", asked);
        Assert.Equal("lazygit", Assert.IsType<FakeImage>(manager.ScreenshotImage).Tag);
        Assert.Equal(string.Empty, manager.ScreenshotStatus);
    }

    [Fact]
    public async Task AScreenshotThatCannotBeFetchedSaysSo()
    {
        var manager = Manager((_, _) => Task.FromResult<byte[]?>(null));

        manager.SelectedTool = Tool("gone", "https://example.org/404.png");
        await manager.ScreenshotLoad;

        Assert.Null(manager.ScreenshotImage);
        Assert.Equal("The screenshot could not be fetched.", manager.ScreenshotStatus);
    }

    [Fact]
    public async Task AScreenshotThatCannotBeDecodedSaysSo()
    {
        var manager = Manager((_, _) => Bytes("not a png"), _ => throw new InvalidOperationException("bad header"));

        manager.SelectedTool = Tool("broken", "https://example.org/broken.png");
        await manager.ScreenshotLoad;

        Assert.Null(manager.ScreenshotImage);
        Assert.Equal("The screenshot could not be shown.", manager.ScreenshotStatus);
    }

    [Fact]
    public async Task ALateScreenshotForAnAppNoLongerSelectedIsNotShown()
    {
        // Moving down the list quickly: the first app's picture arrives after the second's.
        var slow = new TaskCompletionSource<byte[]?>();
        var manager = Manager((url, _) => url.Contains("slow") ? slow.Task : Bytes("fast"));

        manager.SelectedTool = Tool("slow", "https://example.org/slow.png");
        manager.SelectedTool = Tool("fast", "https://example.org/fast.png");
        await manager.ScreenshotLoad;

        slow.SetResult(System.Text.Encoding.UTF8.GetBytes("slow"));
        await Task.Yield();

        Assert.Equal("fast", Assert.IsType<FakeImage>(manager.ScreenshotImage).Tag);
    }

    [Fact]
    public async Task TheReplacedPictureIsReleased()
    {
        var manager = Manager((url, _) => Bytes(url));

        manager.SelectedTool = Tool("one", "https://example.org/1.png");
        await manager.ScreenshotLoad;
        var first = Assert.IsType<FakeImage>(manager.ScreenshotImage);

        manager.SelectedTool = Tool("two", "https://example.org/2.png");
        await manager.ScreenshotLoad;

        Assert.True(first.Disposed);
    }

    [Theory]
    [InlineData("https://example.org/a.png", true)]
    [InlineData("http://example.org/a.png", true)]
    [InlineData("", false)]
    [InlineData("about:blank", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("not a url", false)]
    public void OnlyAWebAddressCountsAsAScreenshot(string url, bool expected)
    {
        // A registration comes from a feed; it must not be able to point the manager at a
        // local file.
        Assert.Equal(expected, Tool("t", url).HasScreenshot);
    }

    [Fact]
    public void EachUrlHasItsOwnCacheFile()
    {
        var dir = Path.GetTempPath();

        Assert.NotEqual(
            ScreenshotCache.CachePathFor("https://example.org/a.png", dir),
            ScreenshotCache.CachePathFor("https://example.org/b.png", dir));
        Assert.Equal(
            ScreenshotCache.CachePathFor("https://example.org/a.png", dir),
            ScreenshotCache.CachePathFor("https://example.org/a.png", dir));
    }
}
