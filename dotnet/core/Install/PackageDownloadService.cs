using DinoSaurList.Core.Box;
using DinoSaurList.Core.Registry;
namespace DinoSaurList.Core.Install;

// Downloading an archive is supported on every OS; installation remains platform-specific.
public sealed class PackageDownloadService(IBoxClient box, AppStorage storage)
{
    public async Task<string> DownloadAsync(ToolEntry tool, ToolPackage package, IProgress<DownloadProgress>? progress, CancellationToken token = default)
    {
        if (!tool.Packages.Contains(package)) throw new ArgumentException("Package does not belong to the selected tool.");
        if (!ToolVersion.PackagePattern().IsMatch(package.FileName)) throw new InvalidDataException("Invalid package name.");
        var directory = SafePaths.Within(Path.Combine(storage.Root, "Downloads", SafePaths.ToolId(tool.Id)), package.Version);
        Directory.CreateDirectory(directory);
        var target = SafePaths.Within(directory, package.FileName);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await box.DownloadAsync(new(package.FileId, package.FileName, "file", package.Size, package.Sha1), temporary, progress, token);
            File.Move(temporary, target, true);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
