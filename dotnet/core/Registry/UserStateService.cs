using DinoSaurList.Core.Box;
using DinoSaurList.Core.Install;

namespace DinoSaurList.Core.Registry;

/// <summary>Device-local state shared by every host. Views never write persistence files directly.</summary>
public sealed class UserStateService(AppStorage storage)
{
    public static string UsefulLinks => BundledDocument("link.md");
    public static string BundledNews => BundledDocument("news.md");
    private static string BundledDocument(string file)
    {
        var assembly = typeof(UserStateService).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(file, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("{{APP_VERSION}}", assembly.GetName().Version?.ToString(3) ?? "0.1.0");
    }
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<UserSettings> SettingsAsync(CancellationToken token = default) => await storage.ReadAsync<UserSettings>("settings.json", token) ?? new();
    public async Task SaveSettingsAsync(UserSettings settings, CancellationToken token = default)
    {
        using var validation = new HttpClient();
        _ = new BoxManagerClient(validation, settings.BoxManagerUrl);
        if (!Uri.TryCreate(settings.UpdateFeedUrl, UriKind.Absolute, out var feed) || feed.Scheme != "https") throw new ArgumentException("Update feed must use HTTPS.");
        if (settings.Theme is not ("System" or "Dark" or "Light")) throw new ArgumentException("Theme must be System, Dark or Light.");
        if (settings.RefreshMode is < 1 or > 4) throw new ArgumentException("Refresh mode must be 1–4.");
        if (settings.RefreshMode == 2 && settings.RefreshSelectedFolders is not { Count: > 0 }) throw new ArgumentException("Select at least one refresh folder.");
        await storage.WriteAsync("settings.json", settings, token);
    }
    public async Task<HashSet<string>> FavouritesAsync(CancellationToken token = default) => new(await storage.ReadAsync<HashSet<string>>("favourites.json", token) ?? [], StringComparer.OrdinalIgnoreCase);
    public async Task<bool> SetFavouriteAsync(string folderName, bool enabled, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        await gate.WaitAsync(token);
        try
        {
            using var lease = await StateLease.AcquireAsync(storage, "state", token);
            var values = await FavouritesAsync(token);
            if (enabled) values.Add(folderName); else values.Remove(folderName);
            await storage.WriteAsync("favourites.json", values, token); return enabled;
        }
        finally { gate.Release(); }
    }
    public async Task<Dictionary<string, string>> ActiveVersionsAsync(CancellationToken token = default) => await storage.ReadAsync<Dictionary<string, string>>("active-versions.json", token) ?? [];
    public async Task PinAsync(InstalledTool tool, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            using var lease = await StateLease.AcquireAsync(storage, "state", token);
            if (!(await new InstallationStore(storage).LoadAsync(token)).Any(t => InstallationStore.Same(t, tool))) throw new InvalidOperationException("Version is not installed.");
            var active = await ActiveVersionsAsync(token); active[tool.ToolId] = tool.Version;
            await storage.WriteAsync("active-versions.json", active, token);
        }
        finally { gate.Release(); }
    }
    public async Task<string> NewsAsync(string bundledNews, CancellationToken token = default) => await storage.ReadAsync<string>("news.json", token) ?? bundledNews;
    public Task SaveNewsAsync(string news, CancellationToken token = default) => storage.WriteAsync("news.json", news, token);
    public void ResetNews() { var path = Path.Combine(storage.Root, "news.json"); if (File.Exists(path)) File.Delete(path); }
}
