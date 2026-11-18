using System.Text.Json.Serialization;

namespace DinoSaurList.Core.Registry;

public sealed record BoxItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] long? Size = null,
    [property: JsonPropertyName("sha1")] string? Sha1 = null);

public sealed record ToolPackage(string FileId, string FileName, string Version, long? Size, string? Sha1, string Platform = "windows");
public sealed record ToolEntry(string Id, string FolderName, string Name, string Author,
    string Category, List<ToolPackage> Packages, FlowDocument Flow, List<BoxItem> Files)
{
    public ToolPackage? Latest => Packages.OrderByDescending(p => p.Version, ToolVersion.Comparer).FirstOrDefault();
    public string LatestVersion => Latest?.Version ?? "No package";
    public string Platforms => string.Join(", ", Flow.Platforms);
    public bool Supports(string platform) => Flow.Platforms.Contains("any") || Flow.Platforms.Contains(platform);
    public BoxItem? FindFile(string name) => Files.FirstOrDefault(f => f.Type == "file" && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed record CatalogueSnapshot(DateTimeOffset FetchedAt, List<ToolEntry> Tools, List<string> Warnings);
public sealed record InstalledTool(string ToolId, string FolderName, string Name, string Author,
    string Version, string Directory, string ExecutionFile, DateTimeOffset InstalledAt, bool Imported = false, FlowDocument? Flow = null);
