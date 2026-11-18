using DinoSaurList.Core.Box;
using DinoSaurList.Core.Install;

namespace DinoSaurList.Core.Registry;

public sealed class DocumentationService(IBoxClient box, AppStorage storage)
{
    public async Task<string?> IconAsync(ToolEntry tool, CancellationToken token = default)
    {
        var file = tool.FindFile(tool.Flow.Get("icon", "name", "icon.ico"));
        if (!tool.Flow.Flag("icon", "flag") || file?.Sha1 is not { Length: 40 } hash || !hash.All(char.IsAsciiHexDigit)) return null;
        if (file.Size is > 8 * 1024 * 1024) throw new InvalidDataException("Tool icon exceeds 8 MiB.");
        var folder = Path.Combine(storage.Root, "Icons"); Directory.CreateDirectory(folder);
        var target = SafePaths.Within(folder, SafePaths.ToolId(file.Id) + "-" + hash + Path.GetExtension(file.Name));
        if (!File.Exists(target)) await box.DownloadAsync(file, target, null, token);
        return target;
    }
    public async Task<string> GuideAsync(ToolEntry tool, CancellationToken token = default)
    {
        var file = tool.FindFile(tool.Flow.Get("guide", "file")) ?? throw new FileNotFoundException("No guide is listed for this tool.");
        var folder = Path.Combine(storage.Root, "Guides", SafePaths.ToolId(tool.Id)); Directory.CreateDirectory(folder);
        // Include the revision hash so a changed guide never reuses stale content.
        var revision = file.Sha1 is { Length: 40 } hash && hash.All(char.IsAsciiHexDigit) ? hash : throw new InvalidDataException("Guide has no valid checksum.");
        var target = SafePaths.Within(folder, revision + "-" + file.Name);
        if (!File.Exists(target)) await box.DownloadAsync(file, target, null, token);
        return target;
    }
    public async Task<string> ReadmeAsync(ToolEntry tool, CancellationToken token = default)
    {
        var file = tool.FindFile(tool.Flow.Get("readme", "file", "README.md")) ?? tool.FindFile("README.md");
        if (file is null) return "# " + tool.Name + "\n\nNo README is listed for this tool.";
        var folder = Path.Combine(storage.Root, "Documents"); Directory.CreateDirectory(folder);
        var key = new string((file.Id + "-" + file.Sha1).Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
        var path = Path.Combine(folder, key + ".md");
        if (File.Exists(path)) return await File.ReadAllTextAsync(path, token);
        var text = await box.ReadTextAsync(file.Id, token);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, text, token); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        // Documentation is re-downloadable. Keep a bounded cache, including obsolete versions.
        foreach (var old in new DirectoryInfo(folder).EnumerateFiles("*.md").OrderByDescending(f => f.LastWriteTimeUtc).Skip(500)) old.Delete();
        return text;
    }
}
