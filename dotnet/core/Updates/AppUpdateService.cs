using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace DinoSaurList.Core.Updates;

// Release assets only. No branch polling, source checkout, Python updater or embedded token.
public interface IApplicationUpdater
{
    bool RestartReady { get; }
    Task<string> CheckAsync(string feed);
    Task<string> PrepareAutomaticUpdateAsync(string feed, Action<int> progress, CancellationToken token = default);
    void Apply();
}
public sealed class AppUpdateService : IApplicationUpdater
{
    public const string DefaultFeed = "https://npi-service.kor.broadcom.net/dinosaurlist/";
    public const string GithubRepository = "https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release";
    private readonly SemaphoreSlim gate = new(1, 1);
    private UpdateManager? manager;
    private UpdateInfo? available;
    public bool RestartReady => manager?.UpdatePendingRestart is not null;
    public string? AvailableVersion => available?.TargetFullRelease.Version.ToString();
    public static string Version => typeof(AppUpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    public static IUpdateSource Source(string feed)
    {
        if (!Uri.TryCreate(feed, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Update feed must use HTTPS without credentials.");
        return feed.TrimEnd('/').Equals(GithubRepository, StringComparison.OrdinalIgnoreCase)
            ? new GithubSource(GithubRepository, accessToken: null, prerelease: Version.Contains('-'))
            : new SimpleWebSource(feed.TrimEnd('/'));
    }
    private async Task<string> CheckCoreAsync(string feed, CancellationToken token = default)
    {
        available = null;
        manager = new UpdateManager(Source(feed));
        if (!manager.IsInstalled) return "Portable/development mode: download the installer to enable automatic updates.";
        try { available = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(12), token); }
        catch when (!token.IsCancellationRequested && feed.TrimEnd('/').Equals(DefaultFeed.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            // Public release assets need no PAT. Stable builds ignore prereleases and keep their packaged OS channel.
            manager = new UpdateManager(Source(GithubRepository));
            available = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(12), token);
        }
        return available is null ? "You have the latest version." : $"Version {available.TargetFullRelease.Version} is available.";
    }
    public async Task<string> CheckAsync(string feed)
    {
        await gate.WaitAsync();
        try { return await CheckCoreAsync(feed); }
        finally { gate.Release(); }
    }
    private async Task<string> DownloadCoreAsync(Action<int> progress, CancellationToken token)
    {
        if (manager is null) throw new InvalidOperationException("Check for updates first.");
        if (available is null) return "No update is available.";
        await manager.DownloadUpdatesAsync(available, progress, token);
        return "Update downloaded. It will be installed automatically at the next start, or choose Restart to update now.";
    }
    public async Task<string> DownloadAsync(Action<int> progress, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { return await DownloadCoreAsync(progress, token); }
        finally { gate.Release(); }
    }
    public async Task<string> PrepareAutomaticUpdateAsync(string feed, Action<int> progress, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var status = await CheckCoreAsync(feed, token);
            return available is null ? status : await DownloadCoreAsync(progress, token);
        }
        finally { gate.Release(); }
    }
    public void Apply()
    {
        // Never race an in-flight checksum/download with application shutdown.
        if (!gate.Wait(0)) throw new InvalidOperationException("An update operation is in progress.");
        try
        {
            var pending = manager?.UpdatePendingRestart ?? throw new InvalidOperationException("Download an update first.");
            manager!.ApplyUpdatesAndRestart(pending);
        }
        finally { gate.Release(); }
    }
}
