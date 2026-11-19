using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Install;
using DinoSaurList.Core.Launch;
using DinoSaurList.Core.Registry;
using Xunit;
namespace DinoSaurList.Tests;

public sealed class UserFeedbackTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request));
    }
    [Fact]
    public async Task NewsUsesMainDocumentVersionCachesOfflineAndPreservesLocalEdits()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var state = new UserStateService(storage);
        Assert.Contains("5.0.0.0", UserStateService.BundledNews);
        using var http = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("version.json") ? "{\"version\":\"5.1.0.0\"}" : "# Release {{APP_VERSION}}\nNew features from main") }));
        var service = new NewsService(http, storage);
        Assert.Contains("5.1.0.0", await service.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Contains("New features from main", await service.CachedAsync(TestContext.Current.CancellationToken));
        await state.SaveNewsAsync("Personal note", TestContext.Current.CancellationToken);
        Assert.Equal("Personal note", await state.NewsAsync(await service.CachedAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
        state.ResetNews(); Assert.Contains("5.1.0.0", await state.NewsAsync(await service.CachedAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task WindowsArchiveDownloadsOnEveryOsWithoutInstallingAndCanBeDownloadedAgain()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var bytes = "archive bytes"u8.ToArray();
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var package = new ToolPackage("2", "v1.0.0.0.zip", "1.0.0.0", bytes.Length, Convert.ToHexString(SHA1.HashData(bytes)), "windows");
        var tool = new ToolEntry("1", "Tool-Author", "Tool", "Author", "MISC", [package], FlowDocument.Parse("[Platform]\nsupported=windows"), []);
        var download = new PackageDownloadService(new BoxManagerClient(http, "https://example.test/"), storage);
        var path = await download.DownloadAsync(tool, package, null, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(await new InstallationStore(storage).LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(path, await download.DownloadAsync(tool, package, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => download.DownloadAsync(tool, package with { FileId = "other" }, null, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task SlowShellStartDoesNotBlockTheCallingThread()
    {
        using var temp = new Temp(); File.WriteAllText(Path.Combine(temp.Path, "run.sh"), "test");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var caller = Environment.CurrentManagedThreadId; var worker = caller;
        var launcher = new ToolLauncher(_ => { worker = Environment.CurrentManagedThreadId; entered.Set(); release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); return Process.GetCurrentProcess(); });
        var task = launcher.RunAsync(temp.Path, "run.sh", false, TestContext.Current.CancellationToken);
        try { Assert.True(entered.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken)); Assert.NotEqual(caller, worker); Assert.False(task.IsCompleted); }
        finally { release.Set(); }
        await task;
    }
}
