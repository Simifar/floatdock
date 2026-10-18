using FloatDock.Core;

namespace FloatDock.Core.Tests;

public sealed class FolderListingTests
{
    [Fact]
    public void DisplayNameReplacesBidirectionalControls()
    {
        var placeholder = ((char)0xFFFD).ToString();
        Assert.Equal("photo" + placeholder + "gpj.exe", FolderListing.DisplayName("photo" + (char)0x202E + "gpj.exe"));
        Assert.Equal("plain name.txt", FolderListing.DisplayName("plain name.txt"));
        Assert.Equal(placeholder + "a" + placeholder, FolderListing.DisplayName((char)0x2066 + "a" + (char)0x2069));
    }

    [Fact]
    public void RecentListsNewestFirstAndSkipsHiddenAndPartialItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), "floatdock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var start = DateTime.UtcNow.AddHours(-1);
            void Create(string name, int minutes, FileAttributes extra = FileAttributes.Normal)
            {
                var path = Path.Combine(directory, name);
                File.WriteAllText(path, name);
                File.SetLastWriteTimeUtc(path, start.AddMinutes(minutes));
                if (extra != FileAttributes.Normal) File.SetAttributes(path, extra);
            }
            Create("old.txt", 1);
            Create("new.txt", 30);
            Create("half.crdownload", 40);
            Create("secret.txt", 50, FileAttributes.Hidden);
            Directory.CreateDirectory(Path.Combine(directory, "folder"));
            Directory.SetLastWriteTimeUtc(Path.Combine(directory, "folder"), start.AddMinutes(10));

            var recent = FolderListing.Recent(directory, 10);
            Assert.Equal(["new.txt", "folder", "old.txt"], recent.Select(entry => entry.Name));
            Assert.True(recent[1].IsFolder);
            Assert.Single(FolderListing.Recent(directory, 1));
            Assert.Empty(FolderListing.Recent(Path.Combine(directory, "missing"), 5));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
