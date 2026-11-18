using System.Collections.Concurrent;
using DinoSaurList.Core.Box;

namespace DinoSaurList.Core.Registry;

public sealed class CatalogueService(IBoxClient box, AppStorage storage)
{
    public const string RootFolder = "368728134757";
    public Task<CatalogueSnapshot?> LoadCacheAsync(CancellationToken token = default) => storage.ReadAsync<CatalogueSnapshot>("catalogue.json", token);
    public async Task<CatalogueSnapshot> RefreshAsync(IProgress<string>? progress = null, CancellationToken token = default)
    {
        var previous = await LoadCacheAsync(token);
        var folders = (await box.ListAsync(RootFolder, token)).Where(x => x.Type == "folder" && !x.Name.StartsWith('.')).ToArray();
        if (folders.Length > 300) throw new InvalidDataException("Catalogue exceeds the 300-tool limit.");
        var tools = new ConcurrentBag<ToolEntry>(); var warnings = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(folders, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (folder, ct) =>
        {
            try
            {
                progress?.Report($"Reading {folder.Name}");
                var files = await box.ListAsync(folder.Id, ct);
                var flowFile = files.FirstOrDefault(f => f.Name.Equals("Flow.txt", StringComparison.OrdinalIgnoreCase));
                var flow = FlowDocument.Parse(flowFile is null ? "" : await box.ReadTextAsync(flowFile.Id, ct));
                tools.Add(BuildEntry(folder, files, flow));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is HttpRequestException or IOException or System.Text.Json.JsonException)
            {
                warnings.Add($"{folder.Name}: unavailable; {e.GetType().Name}");
                var cached = previous?.Tools.FirstOrDefault(t => t.Id == folder.Id);
                if (cached is not null) tools.Add(cached);
            }
        });
        if (folders.Length > 0 && tools.IsEmpty) throw new IOException("No tool folders could be read; the previous catalogue was preserved.");
        var result = new CatalogueSnapshot(DateTimeOffset.UtcNow, tools.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(), warnings.ToList());
        await storage.WriteAsync("catalogue.json", result, token);
        return result;
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
