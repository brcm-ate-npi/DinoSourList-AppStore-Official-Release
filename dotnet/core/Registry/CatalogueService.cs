using System.Collections.Concurrent;
using DinoSaurList.Core.Box;

namespace DinoSaurList.Core.Registry;

public sealed record RefreshOptions(int Mode = 3, IReadOnlyList<string>? SelectedFolders = null);

public sealed class CatalogueService(IBoxClient box, AppStorage storage)
{
    public const string RootFolder = "368728134757";
    public Task<CatalogueSnapshot?> LoadCacheAsync(CancellationToken token = default) => storage.ReadAsync<CatalogueSnapshot>("catalogue.json", token);
    public async Task<CatalogueSnapshot> RefreshAsync(IProgress<string>? progress = null, CancellationToken token = default, RefreshOptions? options = null)
    {
        options ??= new();
        if (options.Mode is < 1 or > 4) throw new ArgumentException("Refresh mode must be 1–4.");
        if (options.Mode == 2 && options.SelectedFolders is not { Count: > 0 }) throw new ArgumentException("Select at least one tool for refresh mode 2.");
        var previous = await LoadCacheAsync(token);
        var folders = (await box.ListAsync(RootFolder, token)).Where(x => x.Type == "folder" && !x.Name.StartsWith('.')).ToArray();
        if (folders.Length > 300) throw new InvalidDataException("Catalogue exceeds the 300-tool limit.");
        var selected = options.SelectedFolders?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var candidates = folders.Where(f => options.Mode switch
        {
            2 => selected.Contains(f.Name),
            4 => previous?.Tools.All(t => t.Id != f.Id) ?? true,
            _ => true
        }).ToArray();
        var tools = new ConcurrentBag<ToolEntry>(); var warnings = new ConcurrentBag<string>();
        if (options.Mode is 2 or 4 && previous is not null)
            foreach (var cached in previous.Tools.Where(t => candidates.All(f => f.Id != t.Id))) tools.Add(cached);
        await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (folder, ct) =>
        {
            try
            {
                progress?.Report($"Reading {folder.Name}");
                var files = await FilesAsync(folder.Id, ct);
                var flowFile = files.FirstOrDefault(f => f.Name.Equals("Flow.txt", StringComparison.OrdinalIgnoreCase));
                var flow = FlowDocument.Parse(flowFile is null ? "" : await box.ReadTextAsync(flowFile.Id, ct));
                var entry = BuildEntry(folder, files, flow);
                tools.Add(entry);
                if (options.Mode is 1 or 2 or 4)
                {
                    var docs = new DocumentationService(box, storage);
                    await docs.ReadmeAsync(entry, ct);
                    if (entry.FindFile(flow.Get("guide", "file")) is not null) await docs.GuideAsync(entry, ct);
                    await docs.IconAsync(entry, ct);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is HttpRequestException or IOException or System.Text.Json.JsonException)
            {
                warnings.Add($"{folder.Name}: unavailable; {e.GetType().Name}");
                var cached = previous?.Tools.FirstOrDefault(t => t.Id == folder.Id);
                if (cached is not null && !tools.Any(t => t.Id == folder.Id)) tools.Add(cached);
            }
        });
        if (candidates.Length > 0 && tools.IsEmpty) throw new IOException("No tool folders could be read; the previous catalogue was preserved.");
        var result = new CatalogueSnapshot(DateTimeOffset.UtcNow, tools.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(), warnings.ToList());
        await storage.WriteAsync("catalogue.json", result, token);
        return result;
    }
    private async Task<List<BoxItem>> FilesAsync(string rootId, CancellationToken token)
    {
        var files = new List<BoxItem>(); var visited = new HashSet<string>();
        async Task Visit(string folderId, string prefix, int depth)
        {
            if (!visited.Add(folderId)) return;
            if (visited.Count > 100) throw new InvalidDataException("Tool metadata exceeds 100 folders.");
            foreach (var item in await box.ListAsync(folderId, token))
            {
                if (item.Type == "file") files.Add(item with { Name = prefix + item.Name });
                else if (item.Type == "folder" && depth < 2) await Visit(item.Id, prefix + item.Name + "/", depth + 1);
            }
        }
        await Visit(rootId, "", 0); return files;
    }
    public static ToolEntry BuildEntry(BoxItem folder, List<BoxItem> files, FlowDocument flow)
    {
        var split = folder.Name.LastIndexOf('-');
        var name = split > 0 ? folder.Name[..split] : folder.Name;
        var author = flow.Get("author", "name", split > 0 ? folder.Name[(split + 1)..] : "Unknown");
        var packages = new List<ToolPackage>();
        foreach (var file in files.Where(f => f.Type == "file"))
        {
            var match = ToolVersion.PackagePattern().Match(file.Name);
            if (!match.Success) continue;
            var platform = match.Groups[2].Value.ToLowerInvariant();
            platform = platform.StartsWith("osx") || platform.StartsWith("mac") ? "macos" : platform.StartsWith("linux") ? "linux" : platform == "any" ? "any" : flow.Platforms.FirstOrDefault() ?? "windows";
            packages.Add(new(file.Id, file.Name, match.Groups[1].Value, file.Size, file.Sha1, platform));
        }
        return new(folder.Id, folder.Name, name, author, flow.Get("category", "cluster", "MISC"),
            packages.OrderByDescending(p => p.Version, ToolVersion.Comparer).ToList(), flow, files);
    }
}
