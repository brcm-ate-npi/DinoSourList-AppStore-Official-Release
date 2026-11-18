using System.Text.Json;
using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Install;

public sealed record LegacyPreview(List<InstalledTool> Installed, HashSet<string> Favourites, int CachedTools);
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
        return new(installed, favourites, cached);
    }
    public async Task ImportAsync(LegacyPreview preview, CancellationToken token = default)
    {
        // Link to old installations without moving, overwriting or deleting them.
        foreach (var tool in preview.Installed) await installationStore.AddAsync(tool, token);
        var current = await storage.ReadAsync<HashSet<string>>("favourites.json", token) ?? [];
        current.UnionWith(preview.Favourites); await storage.WriteAsync("favourites.json", current, token);
    }
}
