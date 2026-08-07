using Jellyfin.Plugin.Community.Services;

namespace Jellyfin.Plugin.Community.Tests;

public sealed class AttachmentServiceTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("0123456789abcdef0123456789abcdef.png")]
    [InlineData("0123456789abcdef0123456789abcdef.webp")]
    public void IsSafeStoredNameAcceptsGeneratedNames(string value)
    {
        Assert.True(AttachmentService.IsSafeStoredName(value));
    }

    [Theory]
    [InlineData("../0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("0123456789abcdef0123456789abcdeg.jpg")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.jpg")]
    [InlineData("0123456789abcdef0123456789abcdef.exe")]
    [InlineData("0123456789abcdef0123456789abcdef.jpg/extra")]
    [InlineData("")]
    public void IsSafeStoredNameRejectsTraversalAndUnexpectedNames(string value)
    {
        Assert.False(AttachmentService.IsSafeStoredName(value));
    }

    [Fact]
    public void SanitizeOriginalNameRemovesPathAndControlCharacters()
    {
        var sanitized = AttachmentService.SanitizeOriginalName("../folder/photo\r\nInjected.png");

        Assert.Equal("photoInjected.png", sanitized);
        Assert.DoesNotContain('\r', sanitized);
        Assert.DoesNotContain('\n', sanitized);
    }

    [Fact]
    public void SanitizeOriginalNameUsesBoundedFallback()
    {
        Assert.Equal("image", AttachmentService.SanitizeOriginalName("\r\n\t"));
        Assert.Equal(180, AttachmentService.SanitizeOriginalName(new string('a', 250) + ".png").Length);
    }
}
