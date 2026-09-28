using Conch.Utilities;
using Xunit;

namespace Conch.Tests;

/// <summary>
/// Covers the <c>%1</c> / <c>%1?</c> convention used by the registrations' <c>args</c> field.
/// </summary>
public class ArgumentTemplateTests
{
    private static List<string> Build(string? template, string input)
    {
        var ok = ArgumentTemplate.TryBuild(template, ArgumentTemplate.Tokenize(input), out var args, out var error);
        Assert.True(ok, error);
        return args;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyTemplateNeedsNoPrompt(string? template)
    {
        Assert.False(ArgumentTemplate.RequiresPrompt(template));
        Assert.Empty(ArgumentTemplate.GetParameters(template));
    }

    [Theory]
    [InlineData("-n")]
    [InlineData("--flag value")]
    public void LiteralTemplateNeedsNoPrompt(string template)
    {
        Assert.False(ArgumentTemplate.RequiresPrompt(template));
    }

    [Theory]
    [InlineData("%1")]
    [InlineData("%1?")]
    [InlineData("%1? %2?")]
    [InlineData("-n %1?")]
    public void PlaceholderTemplateNeedsAPrompt(string template)
    {
        Assert.True(ArgumentTemplate.RequiresPrompt(template));
    }

    [Fact]
    public void LiteralTokensArePassedThrough()
    {
        Assert.Equal(new[] { "-n", "f.txt" }, Build("-n %1?", "f.txt"));
    }

    [Fact]
    public void EmptyOptionalPlaceholderDisappears()
    {
        Assert.Empty(Build("%1?", ""));
    }

    [Fact]
    public void FilledOptionalPlaceholderIsSubstituted()
    {
        Assert.Equal(new[] { "notes.txt" }, Build("%1?", "notes.txt"));
    }

    [Fact]
    public void QuotedValueWithSpacesStaysOneArgument()
    {
        // Substituting into tokens rather than into the string is what keeps this from
        // splitting itself back apart.
        Assert.Equal(new[] { "my notes.txt" }, Build("%1?", "\"my notes.txt\""));
    }

    [Fact]
    public void ValuesBeyondThePlaceholdersAreAppended()
    {
        // A registration declaring one optional file should still open several.
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, Build("%1?", "a.txt b.txt c.txt"));
    }

    [Fact]
    public void UnfilledOptionalPlaceholdersCollapse()
    {
        Assert.Equal(new[] { "a", "b" }, Build("%1? %2? %3? %4? %5?", "a b"));
    }

    [Fact]
    public void PlaceholdersMaySubstituteOutOfOrder()
    {
        Assert.Equal(new[] { "second", "first" }, Build("%2? %1?", "first second"));
    }

    [Fact]
    public void EmbeddedPlaceholderKeepsItsLiteralText()
    {
        Assert.Equal(new[] { "--file=f.txt" }, Build("--file=%1?", "f.txt"));
    }

    [Fact]
    public void EmbeddedPlaceholderTakesItsTokenWhenEmpty()
    {
        // --file= on its own is not a usable argument.
        Assert.Empty(Build("--file=%1?", ""));
    }

    [Fact]
    public void MissingRequiredPlaceholderIsRejected()
    {
        var ok = ArgumentTemplate.TryBuild("%1", Array.Empty<string>(), out _, out var error);

        Assert.False(ok);
        Assert.Contains("%1", error);
    }

    [Fact]
    public void SuppliedRequiredPlaceholderIsAccepted()
    {
        Assert.Equal(new[] { "x" }, Build("%1", "x"));
    }

    [Fact]
    public void RequiredAndOptionalPlaceholdersMix()
    {
        Assert.Equal(new[] { "a" }, Build("%1 %2?", "a"));
        Assert.Equal(new[] { "a", "b" }, Build("%1 %2?", "a b"));
    }

    [Fact]
    public void APlaceholderIsRequiredUnlessEveryOccurrenceIsOptional()
    {
        var parameters = ArgumentTemplate.GetParameters("%1? %1");

        var first = Assert.Single(parameters);
        Assert.False(first.IsOptional);
    }

    // ------------------------------------------------------------- tokenizing ----

    [Fact]
    public void TokenizeSplitsOnWhitespace()
    {
        Assert.Equal(new[] { "a", "b", "c" }, ArgumentTemplate.Tokenize("a  b\tc"));
    }

    [Fact]
    public void TokenizeHonoursQuotes()
    {
        Assert.Equal(new[] { "a b", "c" }, ArgumentTemplate.Tokenize("\"a b\" c"));
    }

    [Fact]
    public void TokenizeKeepsAnExplicitEmptyArgument()
    {
        Assert.Equal(new[] { "" }, ArgumentTemplate.Tokenize("\"\""));
    }

    [Fact]
    public void TokenizeOfEmptyStringYieldsNothing()
    {
        Assert.Empty(ArgumentTemplate.Tokenize("   "));
    }
}
