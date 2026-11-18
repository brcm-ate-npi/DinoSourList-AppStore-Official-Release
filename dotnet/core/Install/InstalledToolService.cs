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
        if (!(await store.LoadAsync(token)).Contains(tool)) throw new InvalidOperationException("Version is not installed.");
        await launcher.RunAsync(tool.Directory, tool.ExecutionFile, false, token);
        await new ActivityStore(storage).RecordAsync(tool.ToolId, "launch", token);
    }
    public async Task RemoveAsync(InstalledTool tool, CancellationToken token = default)
    {
        if (!(await store.LoadAsync(token)).Contains(tool)) throw new InvalidOperationException("Version is not installed.");
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
