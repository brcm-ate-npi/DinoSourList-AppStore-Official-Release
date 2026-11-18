using System.Text;
using System.Text.Json;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Profile;
using DinoSaurList.Core.Registry;
using Xunit;

namespace DinoSaurList.Tests;

public sealed class ProfileTests
{
    private sealed class Box : IBoxClient
    {
        public List<(string Name, byte[] Bytes)> Writes { get; } = [];
        public Task<List<BoxItem>> ListAsync(string id, CancellationToken token = default) => Task.FromResult(id == CatalogueService.RootFolder
            ? new List<BoxItem> { new("123", ".devprofile-data", "folder") }
            : [new("1", "NPI_DEV.json", "file"), new("2", "NPI_DEV.links.json", "file")]);
        public Task<string> ReadTextAsync(string id, CancellationToken token = default) => Task.FromResult(id == "1"
            ? "{\"author\":\"NPI DEV\",\"tweet\":\"Shared message\",\"updated\":\"2026-11-18\"}"
            : "{\"links\":[{\"title\":\"Issue\",\"url\":\"https://jira.test/browse/ABC-1\"},{\"title\":\"bad\",\"url\":\"file:///etc/passwd\"}]}");
        public Task DownloadAsync(BoxItem file, string target, IProgress<DownloadProgress>? progress, CancellationToken token) => throw new NotImplementedException();
        public Task UploadAsync(string folder, string name, byte[] bytes, CancellationToken token) { Assert.Equal("123", folder); Writes.Add((name, bytes)); return Task.CompletedTask; }
    }
    [Fact]
    public async Task SharedProfileUsesLegacyFilesAndPreservesCompatibleJson()
    {
        using var temp = new Temp(); var box = new Box(); var profiles = new DeveloperProfiles(box, new(temp.Path));
        var profile = await profiles.LoadAsync("NPI DEV", TestContext.Current.CancellationToken);
        Assert.Equal("Shared message", profile.Tweet); Assert.Single(profile.Links);
        await profiles.SaveAsync(profile with { Tweet = "Updated" }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "NPI_DEV.json", "NPI_DEV.links.json" }, box.Writes.Select(w => w.Name));
        using var written = JsonDocument.Parse(box.Writes[0].Bytes); Assert.Equal("Updated", written.RootElement.GetProperty("tweet").GetString());
        Assert.Equal("Updated", (await profiles.LoadCacheAsync("NPI DEV", TestContext.Current.CancellationToken))!.Tweet);
    }
    [Fact]
    public async Task SharedProfileRejectsExecutableLinksAndNonPngPhotos()
    {
        using var temp = new Temp(); var box = new Box(); var profiles = new DeveloperProfiles(box, new(temp.Path));
        await Assert.ThrowsAsync<ArgumentException>(() => profiles.SaveAsync(new("NPI DEV", "", "", [new("bad", "file:///tmp/app")]), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => profiles.SavePhotoAsync("NPI DEV", Encoding.UTF8.GetBytes("not a png"), TestContext.Current.CancellationToken));
        Assert.Empty(box.Writes);
    }
}
