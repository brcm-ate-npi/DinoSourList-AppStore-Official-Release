using System.Net;
using System.Security.Cryptography;
using System.Text;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Registry;
using Xunit;

namespace DinoSaurList.Tests;

public sealed class DownloadTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(action(request));
    }
    [Fact]
    public async Task FolderListingPaginatesAndRetainsChecksums()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(r =>
        {
            requests++;
            Assert.DoesNotContain("token", r.RequestUri!.Query);
            var body = requests == 1 ? "{\"items\":[{\"id\":\"1\",\"name\":\"v1.0.zip\",\"type\":\"file\",\"sha1\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}],\"next_marker\":\"next\"}" : "{\"items\":[],\"next_marker\":null}";
            if (requests == 2) Assert.Contains("marker=next", r.RequestUri.Query);
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }));
        var client = new BoxManagerClient(http, "https://example.test/api/box-manager");
        var files = await client.ListAsync("123", TestContext.Current.CancellationToken); Assert.Single(files); Assert.Equal(new string('a', 40), files[0].Sha1); Assert.Equal(2, requests);
    }
    [Fact]
    public async Task CorruptPackageIsRemovedAndCorrectBytesAreAccepted()
    {
        using var temp = new Temp(); var bytes = Encoding.UTF8.GetBytes("known package bytes");
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var box = new BoxManagerClient(http, "https://example.test/"); var path = Path.Combine(temp.Path, "test.zip");
        var file = new BoxItem("1", "test.zip", "file", bytes.Length, new string('0', 40));
        await Assert.ThrowsAsync<InvalidDataException>(() => box.DownloadAsync(file, path, null, TestContext.Current.CancellationToken)); Assert.False(File.Exists(path));
        await box.DownloadAsync(file with { Sha1 = Convert.ToHexString(SHA1.HashData(bytes)) }, path, null, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task FailedCreateNewDoesNotDeleteAnExistingDestination()
    {
        using var temp = new Temp(); var bytes = Encoding.UTF8.GetBytes("new download");
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var box = new BoxManagerClient(http, "https://example.test/"); var path = Path.Combine(temp.Path, "existing.zip");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => box.DownloadAsync(new("1", "test.zip", "file", bytes.Length, Convert.ToHexString(SHA1.HashData(bytes))), path, null, TestContext.Current.CancellationToken));
        Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task UnverifiedOrTruncatedDownloadFailsClosed()
    {
        using var temp = new Temp(); var bytes = new byte[] { 1, 2, 3 };
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var box = new BoxManagerClient(http, "https://example.test/"); var path = Path.Combine(temp.Path, "test.zip");
        await Assert.ThrowsAsync<InvalidDataException>(() => box.DownloadAsync(new("1", "test.zip", "file"), path, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => box.DownloadAsync(new("1", "test.zip", "file", 10, Convert.ToHexString(SHA1.HashData(bytes))), path, null, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path));
    }
    [Theory]
    [InlineData("http://example.test/")]
    [InlineData("file:///tmp")]
    public void ServerTransportCannotLeakToInsecureRemoteScheme(string url)
    {
        using var http = new HttpClient(); Assert.Throws<ArgumentException>(() => new BoxManagerClient(http, url));
    }
}
