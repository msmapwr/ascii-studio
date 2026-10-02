using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class CommentTests
{
    [Fact(DisplayName = "line comments preserve art whitespace")]
    public void LineCommentsPreserveArtWhitespace()
    {
        Assert.True(CommentTools.Wrap("  abc\r\nxyz \n", "Python") == "#   abc\n# xyz \n# ", "Line wrapping changed whitespace");
        foreach (var language in CommentTools.Languages) Assert.True(CommentTools.Wrap("abc", language.Name).Contains("abc"), "Language missing: " + language.Name);
    }

    [Fact(DisplayName = "block comments reject ending conflicts")]
    public void BlockCommentsRejectEndingConflicts()
    {
        Assert.True(CommentTools.Wrap("abc", "CSS") == "/*\nabc\n*/", "CSS wrapper failed");
        foreach (var language in new[] { "CSS", "C++", "SQL" })
        { try { CommentTools.Wrap("*/", language, true); throw new Exception("Unsafe block accepted"); } catch (ArgumentException) { } }
        try { CommentTools.Wrap("a--b", "XML"); throw new Exception("Invalid XML accepted"); } catch (ArgumentException) { }
        try { CommentTools.Wrap("abc", "Python", true); throw new Exception("Python string treated as comment"); } catch (ArgumentException) { }
    }
}
