using DinoSaurList.Core.Launch;
using DinoSaurList.Core.Profile;
using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Install;

/// <summary>Installed-version operations; imported folders remain owned by the legacy application.</summary>
public sealed class InstalledToolService(AppStorage storage, InstallationStore store, IToolLauncher launcher, IInstallIntegration? integration = null)
{
    public async Task<InstalledTool?> FindOrDefaultAsync(string id, CancellationToken token = default)
    {
        var tools = (await store.LoadAsync(token)).Where(t => t.ToolId == id).ToList();
        var active = await new UserStateService(storage).ActiveVersionsAsync(token);
        return tools.FirstOrDefault(t => t.Version == active.GetValueOrDefault(id)) ?? InstallationStore.LatestFor(tools, id);
    }
    public async Task<InstalledTool> FindAsync(string id, string? version = null, CancellationToken token = default)
    {
        var installed = (await store.LoadAsync(token)).Where(t => t.ToolId == id).ToList();
        var active = await new UserStateService(storage).ActiveVersionsAsync(token);
        var selected = version ?? active.GetValueOrDefault(id);
        return installed.FirstOrDefault(t => t.Version == selected) ?? (version is null ? InstallationStore.LatestFor(installed, id) : null)
            ?? throw new InvalidOperationException("The selected version is not installed.");
    }
    public async Task LaunchAsync(InstalledTool tool, CancellationToken token = default)
    {
        if (!(await store.LoadAsync(token)).Any(t => InstallationStore.Same(t, tool))) throw new InvalidOperationException("Version is not installed.");
        var configured = tool.Flow?.Get("execution", ToolLauncher.Platform == "macos" ? "mac" : "file", tool.Flow.Get("execution", "file")) ?? tool.ExecutionFile;
        var directory = tool.Directory;
        if (!File.Exists(SafePaths.Within(directory, string.IsNullOrWhiteSpace(configured) ? "run.cmd" : configured)))
        {
            // Older previews retained the ZIP wrapper. Repair resolution without moving user files.
            var children = Directory.GetDirectories(directory).Where(d => !Path.GetFileName(d).StartsWith('.')).ToArray();
            if (children.Length == 1 && (File.Exists(Path.Combine(children[0], configured)) || File.Exists(Path.Combine(children[0], "run.cmd")))) directory = children[0];
        }
        var execution = InstallService.ResolveExecution(directory, configured);
        await launcher.RunAsync(directory, execution, false, token);
        await new ActivityStore(storage).RecordAsync(tool.ToolId, "launch", token);
    }
    public async Task ActivateAsync(InstalledTool tool, CancellationToken token = default)
    {
        if (!(await store.LoadAsync(token)).Any(t => InstallationStore.Same(t, tool))) throw new InvalidOperationException("Version is not installed.");
        var previous = await FindOrDefaultAsync(tool.ToolId, token);
        if (tool.Flow is { } flow && flow.Flag("installation", "auto"))
        {
            var installer = flow.Get("installation", "file");
            if (string.IsNullOrEmpty(installer) || !File.Exists(SafePaths.Within(tool.Directory, installer))) throw new FileNotFoundException("The selected version's installer is unavailable.");
            var entry = new ToolEntry(tool.ToolId, tool.FolderName, tool.Name, tool.Author, flow.Get("category", "cluster"), [], flow, []);
            await (integration ?? new WindowsIntegration()).BeforeInstallAsync(entry, previous, token);
            await launcher.RunAsync(tool.Directory, installer, true, token);
        }
        await new UserStateService(storage).PinAsync(tool, token);
    }
    public async Task RemoveAsync(InstalledTool tool, CancellationToken token = default)
    {
        if (!(await store.LoadAsync(token)).Any(t => InstallationStore.Same(t, tool))) throw new InvalidOperationException("Version is not installed.");
        if (tool.Imported) { await store.RemoveAsync(tool, token); return; }
        var expected = SafePaths.Within(Path.Combine(storage.Packages, SafePaths.ToolId(tool.ToolId)), tool.Version);
        if (!Path.GetFullPath(tool.Directory).Equals(expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Refusing to remove an unmanaged directory.");
        if (!Directory.Exists(expected)) { await store.RemoveAsync(tool, token); return; }
        await (integration ?? new WindowsIntegration()).BeforeRemoveAsync(tool, token);
        var trash = expected + ".trash-" + Guid.NewGuid().ToString("N"); Directory.Move(expected, trash);
        try { await store.RemoveAsync(tool, token); }
        catch { Directory.Move(trash, expected); throw; }
        Directory.Delete(trash, true);
    }
}
