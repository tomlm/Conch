using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// Guards what Conch.csproj trims from the package, and what it must keep.
/// </summary>
/// <remarks>
/// Avalonia drags ~232MB of Skia and HarfBuzz natives for every RID. Conch keeps libSkiaSharp
/// for the five platforms it runs on, because Program.cs calls UseSkia so Consolonia can decode
/// app screenshots, and drops everything else. Each half rests on a premise nothing at compile
/// time enforces:
///
/// - HarfBuzz is never loaded because Consolonia replaces Avalonia's text shaper with its own.
///   A Consolonia upgrade that stopped doing so would throw DllNotFoundException in the field.
/// - Skia is loaded, so the trim must leave its natives for every platform Conch ships to, or
///   the app starts and then fails the first time someone opens an app's details.
///
/// These assert the premises rather than the symptoms, so a broken trim fails here.
///
/// A runtime test would be the stronger check, but Consolonia's DummyConsole still sets
/// Console.TreatControlCAsInput in its base constructor, which throws under a test runner
/// with no console handle, and behaves differently per platform. Static checks are
/// deterministic and run identically everywhere.
/// </remarks>
public class NativeAssetTrimTests
{
    private static readonly string[] ConsoloniaAssemblies =
    [
        "Consolonia.Core.dll",
        "Consolonia.dll",
        "Consolonia.Controls.dll",
        "Consolonia.Themes.dll",
        "Consolonia.PlatformSupport.dll",
        "Consolonia.ManagedWindows.dll",
    ];

    public static TheoryData<string> RenderingAssemblies()
    {
        var data = new TheoryData<string>();
        foreach (var name in ConsoloniaAssemblies.Append("Conch.dll"))
        {
            data.Add(name);
        }
        return data;
    }

    private static string Path(string assembly) => System.IO.Path.Combine(AppContext.BaseDirectory, assembly);

    private static IReadOnlyList<string> References(string assembly)
    {
        using var stream = File.OpenRead(Path(assembly));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        return metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .ToList();
    }

    // ---------------------------------------------------------------- HarfBuzz: trimmed ----

    [Theory]
    [MemberData(nameof(RenderingAssemblies))]
    public void NothingConchDrawsWithReferencesHarfBuzz(string assembly)
    {
        Assert.True(File.Exists(Path(assembly)), $"{assembly} is not in the test output");

        var offenders = References(assembly)
            .Where(name => name.Contains("HarfBuzz", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{assembly} now references {string.Join(", ", offenders)}; the HarfBuzz natives trimmed in Conch.csproj are needed again.");
    }

    [Theory]
    [MemberData(nameof(RenderingAssemblies))]
    public void NothingConchDrawsWithNamesHarfBuzzForReflection(string assembly)
    {
        // An assembly reference is not the only way in: Type.GetType("HarfBuzzSharp...") would
        // load it without one. Any such call still leaves the name in the metadata strings.
        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path(assembly)));

        Assert.DoesNotContain("HarfBuzzSharp", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoloniaShapesTextItself()
    {
        // Why HarfBuzz can go: Consolonia binds its own ITextShaperImpl over Avalonia's.
        var types = TypeNames("Consolonia.Core.dll");

        Assert.Contains("TextShaper", types);
    }

    [Fact]
    public void NoHarfBuzzAssemblyIsLoadedByRunningTheseTests()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? string.Empty)
            .Where(n => n.Contains("HarfBuzz", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(loaded.Count == 0, $"HarfBuzz assemblies were loaded: {string.Join(", ", loaded)}");
    }

    // ------------------------------------------------------------------- Skia: kept ----

    [Fact]
    public void ConchLoadsSkiaOnPurpose()
    {
        // Program.cs calls UseSkia, which is what makes Consolonia's bitmap fallback exist.
        // If this reference disappears, UseSkia went with it and the kept natives are dead
        // weight -- or screenshots silently stopped working.
        Assert.Contains("Avalonia.Skia", References("Conch.dll"));
    }

    [Fact]
    public void ConsoloniaHandsBitmapDecodingToItsFallback()
    {
        // The other half of why Skia is needed: Consolonia defines the loading members but
        // delegates PNG/JPEG/GIF to the renderer underneath it. If it ever decodes them itself,
        // the Skia natives can be trimmed again.
        using var stream = File.OpenRead(Path("Consolonia.Core.dll"));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        var renderInterface = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .SingleOrDefault(t => metadata.GetString(t.Name) == "ConsoloniaRenderInterface");

        Assert.False(renderInterface.Equals(default(TypeDefinition)),
            "Consolonia.Core no longer defines ConsoloniaRenderInterface");

        var members = renderInterface.GetMethods()
            .Select(h => metadata.GetString(metadata.GetMethodDefinition(h).Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("LoadBitmap", members);
    }

    [Theory]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    [InlineData("osx")]
    public void TheTrimKeepsSkiaForEveryPlatformConchRunsOn(string rid)
    {
        // Read from the csproj rather than a built package: the test project does not pack,
        // and the list is the single place the decision is made.
        var csproj = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Conch", "Conch.csproj"));
        Assert.True(File.Exists(csproj), $"cannot find {csproj}");

        var kept = XDocument.Load(csproj).Descendants("KeptSkiaRuntimes").Single().Value;

        Assert.Contains($";{rid};", kept);
    }

    private static HashSet<string> TypeNames(string assembly)
    {
        using var stream = File.OpenRead(Path(assembly));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        return metadata.TypeDefinitions
            .Select(h => metadata.GetString(metadata.GetTypeDefinition(h).Name))
            .ToHashSet(StringComparer.Ordinal);
    }
}
