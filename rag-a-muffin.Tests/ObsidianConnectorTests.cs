using Microsoft.Extensions.Logging.Abstractions;
using RagAMuffin.Services;
using RagAMuffin.Services.Connectors;

namespace RagAMuffin.Tests;

public class ObsidianConnectorTests
{
    // Test the private ParseNote method via reflection — easier than spinning up a full vault
    private static (string title, DateTime? date, List<string> tags, string body) ParseNote(string raw, string filePath)
    {
        var method = typeof(ObsidianConnector).GetMethod("ParseNote",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = method.Invoke(null, [raw, filePath])!;
        dynamic r  = result;
        return (r.Item1, r.Item2, r.Item3, r.Item4);
    }

    [Fact]
    public void ParseNote_ExtractsTitleFromFrontmatter()
    {
        var raw = "---\ntitle: My Custom Title\n---\n\nBody text here.";
        var (title, _, _, body) = ParseNote(raw, "/vault/untitled.md");
        Assert.Equal("My Custom Title", title);
        Assert.Contains("Body text here", body);
    }

    [Fact]
    public void ParseNote_FallsBackToFilename_WhenNoFrontmatter()
    {
        var (title, _, _, _) = ParseNote("Just plain text.", "/vault/my-note.md");
        Assert.Equal("my-note", title);
    }

    [Fact]
    public void ParseNote_ResolvesWikilinks()
    {
        var raw = "See [[Another Note]] for details.";
        var (_, _, _, body) = ParseNote(raw, "/vault/note.md");
        Assert.Contains("Another Note", body);
        Assert.DoesNotContain("[[", body);
    }

    [Fact]
    public void ParseNote_ResolvesWikilinksWithAlias()
    {
        var raw = "Read [[Source Note|this]]";
        var (_, _, _, body) = ParseNote(raw, "/vault/note.md");
        Assert.Contains("Source Note", body);
        Assert.DoesNotContain("[[", body);
    }

    [Fact]
    public void ParseNote_ParsesDateFromFrontmatter()
    {
        var raw = "---\ndate: 2024-03-15\n---\n\nContent";
        var (_, date, _, _) = ParseNote(raw, "/vault/note.md");
        Assert.NotNull(date);
        Assert.Equal(2024, date!.Value.Year);
        Assert.Equal(3, date.Value.Month);
        Assert.Equal(15, date.Value.Day);
    }

    [Fact]
    public void ParseNote_StripsFrontmatterFromBody()
    {
        var raw = "---\ntitle: T\n---\n\nActual content only.";
        var (_, _, _, body) = ParseNote(raw, "/vault/note.md");
        Assert.DoesNotContain("---", body);
        Assert.DoesNotContain("title:", body);
        Assert.Contains("Actual content only", body);
    }

    [Fact]
    public void ParseNote_RemovesMarkdownImages()
    {
        var raw = "Text before. ![Alt text](image.png) Text after.";
        var (_, _, _, body) = ParseNote(raw, "/vault/note.md");
        Assert.DoesNotContain("![", body);
        Assert.Contains("Text before", body);
        Assert.Contains("Text after", body);
    }
}
