using System.Collections.ObjectModel;
using System.Text;
using Conch.Services;
using Conch.Utilities;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

/// <summary>What Files opens a file with.</summary>
public class FileOpenerTests
{
    private static ToolViewModel Tool(string id, bool installed = true, params string[] opens) => new()
    {
        Id = id,
        Name = id,
        Command = id,
        IsInstalled = installed,
        // A Default entry is available on every host, so these tests mean the same on the
        // Windows development machine and the Linux build.
        Platforms = new PlatformInstallDefinitions { Default = new InstallDefinition() },
        Opens = new ObservableCollection<string>(opens),
    };

    private static FileOpeners Openers(
        IEnumerable<ToolViewModel> tools,
        Dictionary<string, string>? chosen = null,
        bool text = true)
        => new(() => tools, ext => chosen != null && chosen.TryGetValue(ext, out var id) ? id : null, _ => text);

    [Fact]
    public void TheAppThatOpensATypeOpensIt()
    {
        var choice = Openers([Tool("glow", opens: ".md")]).Choose("/notes/readme.md");

        Assert.Equal(FileOpenKind.App, choice.Kind);
        Assert.Equal("glow", choice.Tool?.Id);
    }

    [Fact]
    public void ExtensionsMatchWhateverTheirCase()
    {
        Assert.Equal("glow", Openers([Tool("glow", opens: ".MD")]).Choose("README.Md").Tool?.Id);
    }

    [Fact]
    public void AnAppThatIsNotInstalledOpensNothing()
    {
        Assert.Equal(FileOpenKind.TextEditor, Openers([Tool("glow", installed: false, opens: ".md")]).Choose("a.md").Kind);
    }

    [Fact]
    public void WithSeveralTheChosenOneWins()
    {
        var tools = new[] { Tool("fx", opens: ".json"), Tool("jless", opens: ".json") };

        Assert.Equal("jless", Openers(tools, new() { [".json"] = "jless" }).Choose("a.json").Tool?.Id);
    }

    [Fact]
    public void WithSeveralAndNoChoiceTheFirstByNameWins()
    {
        var tools = new[] { Tool("jless", opens: ".json"), Tool("fx", opens: ".json") };

        Assert.Equal("fx", Openers(tools).Choose("a.json").Tool?.Id);
    }

    [Fact]
    public void AChoiceThatHasSinceBeenUninstalledFallsBack()
    {
        var tools = new[] { Tool("fx", opens: ".json"), Tool("jless", installed: false, opens: ".json") };

        Assert.Equal("fx", Openers(tools, new() { [".json"] = "jless" }).Choose("a.json").Tool?.Id);
    }

    [Fact]
    public void TextNothingClaimsGoesToTheEditor()
    {
        Assert.Equal(FileOpenKind.TextEditor, Openers([], text: true).Choose("notes.txt").Kind);
    }

    [Fact]
    public void ABinaryNothingClaimsIsAskedAbout()
    {
        // Not handed to nano, which would show it as garbage.
        Assert.Equal(FileOpenKind.Ask, Openers([], text: false).Choose("photo.raw").Kind);
    }

    [Fact]
    public void AFileWithNoExtensionIsJudgedByItsContents()
    {
        Assert.Equal(FileOpenKind.TextEditor, Openers([Tool("glow", opens: ".md")], text: true).Choose("Makefile").Kind);
    }

    [Theory]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("README", "")]
    [InlineData("/a.b/notes.MD", ".md")]
    public void TheLastExtensionIsTheOneThatCounts(string path, string expected)
    {
        Assert.Equal(expected, FileOpeners.Extension(path));
    }

    [Fact]
    public void KnownExtensionsListEachTypeWithItsCandidates()
    {
        var tools = new[] { Tool("fx", opens: ".json"), Tool("jless", opens: [".json", ".jsonl"]), Tool("glow", installed: false, opens: ".md") };

        var known = Openers(tools).KnownExtensions();

        Assert.Equal(new[] { ".json", ".jsonl" }, known.Select(k => k.Extension));
        Assert.Equal(2, known[0].Candidates.Count);
    }

    [Fact]
    public void Utf8TextIsText()
    {
        Assert.True(FileOpeners.LooksLikeText(Encoding.UTF8.GetBytes("héllo — wörld\n")));
    }

    [Fact]
    public void ANulByteMeansBinary()
    {
        Assert.False(FileOpeners.LooksLikeText(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 }));
    }

    [Fact]
    public void InvalidUtf8MeansBinary()
    {
        Assert.False(FileOpeners.LooksLikeText(new byte[] { 0x41, 0xFF, 0xFE, 0x41 }));
    }

    [Fact]
    public void ACharacterCutOffByTheSampleIsStillText()
    {
        var bytes = Encoding.UTF8.GetBytes("ab€");

        Assert.True(FileOpeners.LooksLikeText(bytes.AsSpan(0, bytes.Length - 1)));
    }

    [Fact]
    public void AnEmptyFileIsText()
    {
        Assert.True(FileOpeners.LooksLikeText(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void AFileThatCannotBeReadIsNotText()
    {
        Assert.False(FileOpeners.LooksLikeText(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"))));
    }

    [Theory]
    [InlineData(@"C:\Users\me\notes.md", "/mnt/c/Users/me/notes.md")]
    [InlineData(@"d:\My Files\a b.txt", "/mnt/d/My Files/a b.txt")]
    [InlineData(@"C:\", "/mnt/c")]
    [InlineData(@"\\wsl$\Ubuntu\home\me\a.md", "/home/me/a.md")]
    [InlineData(@"\\wsl.localhost\Debian\etc\hosts", "/etc/hosts")]
    [InlineData("/home/me/a.md", "/home/me/a.md")]
    [InlineData(@"\\server\share\a.md", @"\\server\share\a.md")]
    public void WindowsPathsBecomeWhatWslSees(string windows, string linux)
    {
        Assert.Equal(linux, WslPath.FromWindows(windows));
    }

    [Fact]
    public void PreferencesListsOnlyTypesWithAChoiceToMake()
    {
        var directory = Path.Combine(Path.GetTempPath(), "conch-filetypes-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            var app = new AppViewModel(new ShellSettings(directory).Load());
            app.Tools.Add(Tool("fx", opens: ".json"));
            app.Tools.Add(Tool("jless", opens: ".json"));
            app.Tools.Add(Tool("glow", opens: ".md"));

            var preferences = new SettingsViewModel(app,
                new Conch.Services.Roles.RoleRegistry(_ => Array.Empty<Conch.Services.Roles.IRoleProvider>(), _ => null));

            var row = Assert.Single(preferences.FileTypes);
            Assert.Equal(".json", row.DisplayName);

            row.Selected = row.Choices.Single(c => c.Id == "jless");
            Assert.Equal("jless", app.Settings.GetFileTypeChoice(".json"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileTypeChoicesSurviveARestartAndOlderFilesLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), "conch-filetypes-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), """{ "version": 2, "theme": "Modern" }""");
            var first = new ShellSettings(directory).Load();
            Assert.Null(first.GetFileTypeChoice(".json"));

            first.SetFileTypeChoice(".json", "jless");

            Assert.Equal("jless", new ShellSettings(directory).Load().GetFileTypeChoice(".JSON"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
