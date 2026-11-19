using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Profile;

public sealed record Activity(DateTimeOffset Time, string ToolId, string Kind);
public sealed class ActivityStore(AppStorage storage)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task RecordAsync(string toolId, string kind, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var entries = await LoadAsync(token);
            entries.Add(new(DateTimeOffset.UtcNow, toolId, kind));
            entries = entries.Where(e => e.Time > DateTimeOffset.UtcNow.AddDays(-365)).TakeLast(10000).ToList();
            await storage.WriteAsync("activity.json", entries, token);
        }
        finally { gate.Release(); }
    }
    public async Task<List<Activity>> LoadAsync(CancellationToken token = default) => await storage.ReadAsync<List<Activity>>("activity.json", token) ?? [];
}
