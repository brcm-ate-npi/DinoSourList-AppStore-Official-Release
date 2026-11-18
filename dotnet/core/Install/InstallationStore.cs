using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Install;

public sealed class InstallationStore(AppStorage storage)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<List<InstalledTool>> LoadAsync(CancellationToken token = default) =>
        await storage.ReadAsync<List<InstalledTool>>("installed.json", token) ?? [];
    public async Task AddAsync(InstalledTool tool, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            using var lease = await StateLease.AcquireAsync(storage, "state", token);
            var tools = await LoadAsync(token);
            tools.RemoveAll(t => t.ToolId == tool.ToolId && t.Version == tool.Version);
            tools.Add(tool); await storage.WriteAsync("installed.json", tools, token);
        }
        finally { gate.Release(); }
    }
    public async Task RemoveAsync(InstalledTool tool, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { using var lease = await StateLease.AcquireAsync(storage, "state", token); var tools = await LoadAsync(token); tools.RemoveAll(t => Same(t, tool)); await storage.WriteAsync("installed.json", tools, token); }
        finally { gate.Release(); }
    }
    public static bool Same(InstalledTool left, InstalledTool right) => left.ToolId == right.ToolId && left.Version == right.Version
        && Path.GetFullPath(left.Directory).Equals(Path.GetFullPath(right.Directory), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public static InstalledTool? LatestFor(IEnumerable<InstalledTool> tools, string id) => tools.Where(t => t.ToolId == id)
        .OrderByDescending(t => t.Version, ToolVersion.Comparer).FirstOrDefault();
}
