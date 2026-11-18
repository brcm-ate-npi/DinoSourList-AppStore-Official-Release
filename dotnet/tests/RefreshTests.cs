using DinoSaurList.Core.Box;
using DinoSaurList.Core.Registry;
using Xunit;

namespace DinoSaurList.Tests;

public sealed class RefreshTests
{
    private sealed class Box : IBoxClient
    {
        public List<BoxItem> Folders { get; } = [new("1", "Tool-Author", "folder"), new("2", "Second-Author", "folder")];
        public List<string> Listed { get; } = [];
        public bool Fail { get; set; }
        public Task<List<BoxItem>> ListAsync(string folderId, CancellationToken token = default)
        {
            lock (Listed) Listed.Add(folderId);
            if (folderId == CatalogueService.RootFolder) return Task.FromResult(Folders.ToList());
            if (Fail) throw new HttpRequestException("unavailable");
            return Task.FromResult(new List<BoxItem> { new(folderId + "1", "Flow.txt", "file"), new(folderId + "2", "v1.0.0.0.zip", "file") });
        }
        public Task<string> ReadTextAsync(string fileId, CancellationToken token = default) => Task.FromResult("[Author]\nName=Author");
        public Task DownloadAsync(BoxItem file, string destination, IProgress<DownloadProgress>? progress, CancellationToken token) => throw new InvalidOperationException("No documents in fixture.");
    }
    [Fact]
    public async Task SelectedAndNewOnlyRefreshKeepUnselectedCacheAndAvoidFetchingIt()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var box = new Box(); var service = new CatalogueService(box, storage);
        var first = await service.RefreshAsync(token: TestContext.Current.CancellationToken); Assert.Equal(2, first.Tools.Count);
        box.Listed.Clear();
        var selected = await service.RefreshAsync(token: TestContext.Current.CancellationToken, options: new(2, ["Tool-Author"]));
        Assert.Equal(2, selected.Tools.Count); Assert.Contains("1", box.Listed); Assert.DoesNotContain("2", box.Listed);
        box.Listed.Clear(); box.Folders.Add(new("3", "New-Author", "folder"));
        var newOnly = await service.RefreshAsync(token: TestContext.Current.CancellationToken, options: new(4));
        Assert.Equal(3, newOnly.Tools.Count); Assert.Contains("3", box.Listed); Assert.DoesNotContain("1", box.Listed); Assert.DoesNotContain("2", box.Listed);
    }
    [Fact]
    public async Task FailedRefreshRetainsPreviousMetadataAndReportsWarnings()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var box = new Box(); var service = new CatalogueService(box, storage);
        var first = await service.RefreshAsync(token: TestContext.Current.CancellationToken); box.Fail = true;
        var failed = await service.RefreshAsync(token: TestContext.Current.CancellationToken);
        Assert.Equal(first.Tools.Select(t => t.Id), failed.Tools.Select(t => t.Id)); Assert.Equal(2, failed.Warnings.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RefreshAsync(token: TestContext.Current.CancellationToken, options: new(2)));
    }
}
