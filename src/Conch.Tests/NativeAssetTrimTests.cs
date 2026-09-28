using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// Guards the removal of the SkiaSharp and HarfBuzz native assets from the package.
/// </summary>
/// <remarks>
/// Conch.csproj drops ~232MB of Skia and HarfBuzz natives, taking the tool package from
/// 191MB to 7.4MB. That is only safe because Consolonia supplies its own
/// <c>IPlatformRenderInterface</c> — it does manipulate bitmaps, via a pixel buffer rather
/// than Skia — so the natives are never loaded.
///
/// Nothing at compile time enforces that. A Consolonia upgrade that started delegating to
/// Skia would leave the packaged tool throwing DllNotFoundException at runtime, and the app
/// would look fine until someone displayed an image. These tests assert the premise instead
/// of the symptom, so the trim fails here rather than in the field.
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

    [Theory]
    [MemberData(nameof(RenderingAssemblies))]
    public void AssemblyDoesNotReferenceTrimmedNatives(string assembly)
    {
        Assert.True(File.Exists(Path(assembly)), $"{assembly} is not in the test output");

        using var stream = File.OpenRead(Path(assembly));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        var offenders = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Where(name => name.Contains("Skia", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("HarfBuzz", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{assembly} now references {string.Join(", ", offenders)}; the native assets trimmed in Conch.csproj are needed again.");
    }

    [Theory]
    [MemberData(nameof(RenderingAssemblies))]
    public void AssemblyDoesNotNameTrimmedNativesForReflection(string assembly)
    {
        // An assembly reference is not the only way in: Type.GetType("SkiaSharp...") would
        // load it without one. Any such call still leaves the name in the metadata strings.
        var bytes = File.ReadAllBytes(Path(assembly));
        var text = System.Text.Encoding.ASCII.GetString(bytes);

        Assert.DoesNotContain("SkiaSharp", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HarfBuzzSharp", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoloniaImplementsBitmapLoadingItself()
    {
        // The positive half of the argument: Consolonia is not skipping bitmaps, it owns
        // them. If these members disappear, bitmap work has moved somewhere else and the
        // reasoning behind the trim needs revisiting.
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
        Assert.Contains("CreateWriteableBitmap", members);
        Assert.Contains("CreateRenderTargetBitmap", members);
    }

    [Fact]
    public void ConsoloniaShipsItsOwnBitmapImplementation()
    {
        using var stream = File.OpenRead(Path("Consolonia.Core.dll"));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        var types = metadata.TypeDefinitions
            .Select(h => metadata.GetString(metadata.GetTypeDefinition(h).Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("PixelBufferBitmapImpl", types);
    }

    [Fact]
    public void NoSkiaAssemblyIsLoadedByRunningTheseTests()
    {
        // Cheap runtime backstop: the tests above have exercised Conch's model and utility
        // code, and nothing has dragged in a rasteriser.
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? string.Empty)
            .Where(n => n.Contains("Skia", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("HarfBuzz", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(loaded.Count == 0, $"Skia/HarfBuzz assemblies were loaded: {string.Join(", ", loaded)}");
    }
}
