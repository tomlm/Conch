using Conch.Services;
using Xunit;

namespace Conch.Tests;

public class ShellSettingsTests : IDisposable
{
    private readonly string _directory;

    public ShellSettingsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "conch-settings-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void ChoicesSurviveARestart()
    {
        var first = new ShellSettings(_directory).Load();
        first.SetRoleChoice("file-explorer", "ranger");
        first.Theme = "TurboVision";

        var second = new ShellSettings(_directory).Load();

        Assert.Equal("ranger", second.GetRoleChoice("file-explorer"));
        Assert.Equal("TurboVision", second.Theme);
    }

    [Fact]
    public void NoFileMeansDefaultsRatherThanAFailure()
    {
        // The state every first run is in.
        var settings = new ShellSettings(_directory).Load();

        Assert.Null(settings.Theme);
        Assert.Null(settings.GetRoleChoice("file-explorer"));
        Assert.False(File.Exists(SettingsPath), "loading should not create a file before anything is set");
    }

    [Fact]
    public void ClearingAChoiceReturnsTheRoleToItsDefault()
    {
        var settings = new ShellSettings(_directory).Load();
        settings.SetRoleChoice("text-editor", "vim");

        settings.SetRoleChoice("text-editor", null);

        Assert.Null(new ShellSettings(_directory).Load().GetRoleChoice("text-editor"));
    }

    [Fact]
    public void UnreadableSettingsAreKeptRatherThanOverwritten()
    {
        // Settings are small, but they are the user's. Recovering from a parse error by
        // destroying the file leaves nothing to look at and no way back -- and the usual
        // causes, a half-written file or a hand edit with a trailing comma, are recoverable
        // by hand if the bytes still exist.
        const string spoiled = "{ this is not json";
        File.WriteAllText(SettingsPath, spoiled);

        var settings = new ShellSettings(_directory).Load();

        Assert.Null(settings.Theme);
        Assert.Equal(spoiled, File.ReadAllText(SettingsPath + ".bad"));
    }

    [Fact]
    public void SettingsFromANewerBuildAreNotDiscarded()
    {
        // A newer Conch may write keys this build has never heard of. Reading must not throw,
        // and what this build does understand has to survive alongside them.
        File.WriteAllText(SettingsPath, """
            {
              "version": 1,
              "theme": "ModernDark",
              "roles": { "file-explorer": "ranger" },
              "something-from-the-future": { "nested": true }
            }
            """);

        var settings = new ShellSettings(_directory).Load();

        Assert.Equal("ModernDark", settings.Theme);
        Assert.Equal("ranger", settings.GetRoleChoice("file-explorer"));
        Assert.False(File.Exists(SettingsPath + ".bad"), "an unknown key is not a corrupt file");
    }

    [Fact]
    public void RoleNamesAreMatchedWithoutRegardToCase()
    {
        var settings = new ShellSettings(_directory).Load();
        settings.SetRoleChoice("File-Explorer", "nnn");

        Assert.Equal("nnn", settings.GetRoleChoice("file-explorer"));
    }
}
