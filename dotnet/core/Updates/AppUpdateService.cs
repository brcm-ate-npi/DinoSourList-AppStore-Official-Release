using Velopack;
using Velopack.Sources;

namespace DinoSaurList.Core.Updates;

// Same lifecycle as NPISpectreConverter; server feed only, with no GithubSource/PAT branch.
public sealed class AppUpdateService
{
    private UpdateManager? manager;
    private UpdateInfo? available;
    public static string Version => typeof(AppUpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public async Task<string> CheckAsync(string feed)
    {
        if (!Uri.TryCreate(feed, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new ArgumentException("Update feed must use HTTPS.");
        manager = new UpdateManager(new SimpleWebSource(feed.TrimEnd('/')));
        if (!manager.IsInstalled) return "Portable/development mode: download the installer to enable automatic updates.";
        available = await manager.CheckForUpdatesAsync();
        return available is null ? "You have the latest version." : $"Version {available.TargetFullRelease.Version} is available. Choose Download update to prepare it.";
    }
    public async Task<string> DownloadAsync(Action<int> progress)
    {
        if (manager is null || available is null) throw new InvalidOperationException("Check for updates first.");
        await manager.DownloadUpdatesAsync(available, progress);
        return "Update downloaded. Choose Restart to update when your work is saved.";
    }
    public void Apply()
    {
        if (manager is null || available is null) throw new InvalidOperationException("Check for updates first.");
        manager.ApplyUpdatesAndRestart(available);
    }
}
