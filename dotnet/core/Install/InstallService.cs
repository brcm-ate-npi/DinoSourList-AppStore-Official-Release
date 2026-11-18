using System.IO.Compression;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Launch;
using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Install;

public sealed class InstallService(IBoxClient box, AppStorage storage, InstallationStore installed, IToolLauncher launcher, IInstallIntegration? integration = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<InstalledTool> InstallAsync(ToolEntry tool, ToolPackage package, IProgress<DownloadProgress>? progress, CancellationToken token = default)
    {
        if (!tool.Supports(ToolLauncher.Platform) || (package.Platform != "any" && package.Platform != ToolLauncher.Platform))
            throw new PlatformNotSupportedException("This package does not support the current operating system.");
        if (!tool.Packages.Contains(package)) throw new ArgumentException("Package does not belong to the selected tool.");
        if (!ToolVersion.PackagePattern().IsMatch(package.FileName)) throw new InvalidDataException("Invalid package name.");
        var toolRoot = Path.Combine(storage.Packages, SafePaths.ToolId(tool.Id));
        var stage = Path.Combine(toolRoot, ".stage-" + Guid.NewGuid().ToString("N"));
        var zip = stage + ".zip";
        var target = SafePaths.Within(toolRoot, package.Version);
        await gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(toolRoot);
            using var installationLock = new FileStream(Path.Combine(toolRoot, ".install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (Directory.Exists(target)) throw new IOException("This version is already installed; choose Open or another version.");
            await box.DownloadAsync(new(package.FileId, package.FileName, "file", package.Size, package.Sha1), zip, progress, token);
            await ExtractAsync(zip, stage, token);
            var execution = tool.Flow.Get("execution", ToolLauncher.Platform == "macos" ? "mac" : "file", tool.Flow.Get("execution", "file"));
            if (string.IsNullOrEmpty(execution) || !File.Exists(SafePaths.Within(stage, execution)))
                execution = new[] { "run.cmd", "setup.cmd", "run.sh" }.FirstOrDefault(f => File.Exists(Path.Combine(stage, f))) ?? "";
            var installer = tool.Flow.Get("installation", "file");
            if (tool.Flow.Flag("installation", "auto") && (string.IsNullOrEmpty(installer) || !File.Exists(SafePaths.Within(stage, installer))))
                throw new InvalidDataException("Automatic installation is configured but its installer is missing.");
            var icon = tool.FindFile(tool.Flow.Get("icon", "name"));
            if (tool.Flow.Flag("icon", "flag") && icon is not null && !File.Exists(SafePaths.Within(stage, icon.Name)))
                await box.DownloadAsync(icon, SafePaths.Within(stage, icon.Name), null, token);
            token.ThrowIfCancellationRequested();
            var previous = await new InstalledToolService(storage, installed, launcher).FindOrDefaultAsync(tool.Id, token);
            Directory.Move(stage, target);
            var hooks = integration ?? new WindowsIntegration();
            var record = new InstalledTool(tool.Id, tool.FolderName, tool.Name, tool.Author, package.Version, target, execution, DateTimeOffset.UtcNow, Flow: tool.Flow);
            var registered = false;
            try
            {
                await hooks.BeforeInstallAsync(tool, previous, token);
                // Run from the final directory: installers may persist their working directory.
                if (tool.Flow.Flag("installation", "auto")) await launcher.RunAsync(target, installer, true, token);
                await hooks.AfterInstallAsync(tool, target, execution, token);
                await installed.AddAsync(record, token); registered = true;
                await new UserStateService(storage).PinAsync(record, token);
            }
            catch
            {
                if (registered) await installed.RemoveAsync(record, CancellationToken.None);
                Directory.Move(target, stage); throw;
            }
            return record;
        }
        finally
        {
            try
            {
                if (File.Exists(zip)) File.Delete(zip);
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
            }
            finally { gate.Release(); }
        }
    }
    public static async Task ExtractAsync(string zip, string destination, CancellationToken token = default)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 100000) throw new InvalidDataException("Package contains too many entries.");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            total = checked(total + entry.Length);
            if (total > 20L * 1024 * 1024 * 1024 || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("Package exceeds limits or contains symbolic links.");
            var path = SafePaths.Within(destination, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            await input.CopyToAsync(output, token);
            if (!OperatingSystem.IsWindows())
            {
                var mode = (entry.ExternalAttributes >> 16) & 0x1ff;
                if (mode != 0) File.SetUnixFileMode(path, (UnixFileMode)mode);
            }
        }
    }
}
