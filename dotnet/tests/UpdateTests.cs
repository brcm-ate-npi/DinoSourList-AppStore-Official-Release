using DinoSaurList.Core.Updates;
using Velopack.Sources;
using Xunit;

namespace DinoSaurList.Tests;

public sealed class UpdateTests
{
    [Fact]
    public void ReleaseSourcesAcceptHttpsAndUsePublicGithubOnlyForTheConfiguredRepository()
    {
        Assert.IsType<SimpleWebSource>(AppUpdateService.Source(AppUpdateService.DefaultFeed));
        Assert.IsType<GithubSource>(AppUpdateService.Source(AppUpdateService.GithubRepository + "/"));
        Assert.IsType<SimpleWebSource>(AppUpdateService.Source("https://updates.example.test/feed/"));
        foreach (var unsafeFeed in new[] { "http://updates.example.test/", "file:///tmp/updates", "https://user:password@example.test/", "../main" })
            Assert.Throws<ArgumentException>(() => AppUpdateService.Source(unsafeFeed));
    }
    [Fact]
    public void ApplyRefusesToTerminateWithoutACompletedVerifiedDownload()
    {
        Assert.Throws<InvalidOperationException>(() => new AppUpdateService().Apply());
    }
}
