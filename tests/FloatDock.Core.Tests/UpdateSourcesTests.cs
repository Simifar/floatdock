using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class UpdateSourcesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMeansNoUpdateCheck(string? text)
    {
        Assert.Equal(UpdateFeedKind.None, UpdateSources.Classify(text, out var feed));
        Assert.Equal(string.Empty, feed);
    }

    [Fact]
    public void AnHttpsAddressIsAccepted()
    {
        Assert.Equal(UpdateFeedKind.Https, UpdateSources.Classify("  https://example.com/floatdock/releases  ", out var feed));
        Assert.Equal("https://example.com/floatdock/releases", feed);
    }

    [Theory]
    [InlineData("http://example.com/releases")]
    [InlineData("ftp://example.com/releases")]
    [InlineData("file:///C:/releases")]
    [InlineData("https://user:secret@example.com/releases")]
    [InlineData("https://")]
    [InlineData("releases")]
    [InlineData(@"..\releases")]
    [InlineData("C:releases")]
    [InlineData("https://example.com/\u0007bell")]
    public void UnsafeOrUnclearSourcesAreRefused(string text)
    {
        Assert.Equal(UpdateFeedKind.Invalid, UpdateSources.Classify(text, out var feed));
        Assert.Equal(string.Empty, feed);
    }

    [Theory]
    [InlineData(@"C:\releases")]
    [InlineData(@"D:\a folder\with spaces\")]
    [InlineData(@"\\server\share\floatdock")]
    public void AnAbsoluteFolderIsAccepted(string text)
    {
        Assert.Equal(UpdateFeedKind.Folder, UpdateSources.Classify(text, out var feed));
        Assert.Equal(Path.GetFullPath(text), feed);
    }

    [Theory]
    [InlineData("https://github.com/Simifar/floatdock", "https://github.com/Simifar/floatdock")]
    [InlineData("https://github.com/Simifar/floatdock/", "https://github.com/Simifar/floatdock")]
    [InlineData("https://GitHub.com/Simifar/floatdock.git", "https://github.com/Simifar/floatdock")]
    public void ARepositoryAddressMeansItsReleases(string feed, string expected) => Assert.Equal(expected, UpdateSources.GitHubRepository(feed));

    [Theory]
    [InlineData("https://github.com/Simifar")]
    [InlineData("https://github.com/Simifar/floatdock/releases")]
    [InlineData("https://github.com/Simifar/floatdock?tab=readme")]
    [InlineData("https://example.com/Simifar/floatdock")]
    [InlineData("https://gist.github.com/Simifar/floatdock")]
    [InlineData(@"C:\releases")]
    public void OtherAddressesAreNotRepositories(string feed) => Assert.Null(UpdateSources.GitHubRepository(feed));}
