using System.Text.Json;
using DinoSaurList.Core.Registry;
using DinoSaurList.Core.Profile;

namespace DinoSaurList.Core.Install;

public sealed record LegacyPreview(List<InstalledTool> Installed, HashSet<string> Favourites, int CachedTools, string? LegacyRoot = null);
public sealed class LegacyImporter(AppStorage storage, InstallationStore installationStore)
{
    public static async Task<LegacyPreview> PreviewAsync(string legacyRoot, IReadOnlyList<ToolEntry> tools, CancellationToken token = default)
    {
        legacyRoot = Path.GetFullPath(legacyRoot);
        var installed = new List<InstalledTool>(); var favourites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var favouriteFile = Path.Combine(legacyRoot, "config-record", "favourites.json");
        if (File.Exists(favouriteFile))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(favouriteFile, token));
            if (doc.RootElement.TryGetProperty("favourites", out var list))
                foreach (var value in list.EnumerateArray()) if (value.GetString() is { } text) favourites.Add(text);
        }
        var downloads = Path.Combine(legacyRoot, "Software_Downloaded");
        if (Directory.Exists(downloads))
            foreach (var folder in Directory.EnumerateDirectories(downloads))
            {
                var directory = new DirectoryInfo(folder);
                if (directory.LinkTarget is not null) continue;
                var match = ToolVersion.InstalledPattern().Match(directory.Name);
                if (!match.Success) continue;
                var tool = tools.FirstOrDefault(t => t.Name.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase) && t.Author.Equals(match.Groups[3].Value, StringComparison.OrdinalIgnoreCase));
                if (tool is null) continue;
                var flow = tool.Flow;
                var flowPath = SafePaths.Within(Path.Combine(legacyRoot, "App_Store"), tool.FolderName + "/Flow.txt");
                if (File.Exists(flowPath)) flow = FlowDocument.Parse(await File.ReadAllTextAsync(flowPath, token));
                var target = flow.Get("execution", "file");
                if (!string.IsNullOrWhiteSpace(target)) SafePaths.Within(folder, target);
                installed.Add(new(tool.Id, tool.FolderName, tool.Name, tool.Author, match.Groups[2].Value, Path.GetFullPath(folder), target, DateTimeOffset.UtcNow, true, flow));
            }
        var cache = Path.Combine(legacyRoot, "config-record", "record.json"); var cached = 0;
        if (File.Exists(cache))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(cache, token));
            if (doc.RootElement.TryGetProperty("items", out var items)) cached = items.GetArrayLength();
        }
        return new(installed, favourites, cached, legacyRoot);
    }
    public async Task ImportAsync(LegacyPreview preview, CancellationToken token = default)
    {
        // Link to old installations without moving, overwriting or deleting them.
        foreach (var tool in preview.Installed) await installationStore.AddAsync(tool, token);
        var current = await storage.ReadAsync<HashSet<string>>("favourites.json", token) ?? [];
        current.UnionWith(preview.Favourites); await storage.WriteAsync("favourites.json", current, token);
        if (preview.LegacyRoot is { } legacyRoot)
        {
            // Preserve only known user-data files. Never import update.json, API credentials or executables.
            var config = Path.Combine(legacyRoot, "config-record");
            var backup = Path.Combine(storage.Root, "LegacyData"); Directory.CreateDirectory(backup);
            foreach (var name in new[] { "news-edited.html", "dev_profile.json", "dev_activity.json", "refresh_setting.json" })
            {
                var source = SafePaths.Within(config, name); var target = SafePaths.Within(backup, name);
                if (!File.Exists(source) || File.Exists(target)) continue;
                if (new FileInfo(source).Length > 8 * 1024 * 1024) throw new InvalidDataException("Legacy user data exceeds 8 MiB: " + name);
                File.Copy(source, target, false);
            }
            var news = Path.Combine(backup, "news-edited.html");
            if (File.Exists(news) && await storage.ReadAsync<string>("news.json", token) is null)
                await new UserStateService(storage).SaveNewsAsync(LegacyNews.PlainText(await File.ReadAllTextAsync(news, token)), token);
            var messages = Path.Combine(backup, "dev_profile.json");
            if (File.Exists(messages))
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(messages, token));
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                    var key = "profile-" + DeveloperProfiles.SafeName(entry.Name) + ".json";
                    if (await storage.ReadAsync<DeveloperProfile>(key, token) is not null) continue;
                    var tweet = entry.Value.TryGetProperty("tweet", out var text) ? text.GetString() ?? "" : "";
                    var updated = entry.Value.TryGetProperty("updated", out var date) ? date.GetString() ?? "" : "";
                    var pending = entry.Value.TryGetProperty("pending", out var flag) && flag.ValueKind == JsonValueKind.True;
                    await storage.WriteAsync(key, new DeveloperProfile(entry.Name, tweet, updated, [], pending), token);
                }
            }
            var refresh = Path.Combine(backup, "refresh_setting.json");
            if (File.Exists(refresh))
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(refresh, token));
                var mode = doc.RootElement.GetProperty("mode").GetInt32();
                var selected = doc.RootElement.TryGetProperty("selected_apps", out var list) ? list.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToList() : [];
                var state = new UserStateService(storage); var settings = await state.SettingsAsync(token);
                await state.SaveSettingsAsync(settings with { RefreshMode = mode, RefreshSelectedFolders = selected }, token);
            }
        }
    }
}
