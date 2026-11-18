using System.Text.Json;

namespace DinoSaurList.Core.Registry;

public sealed class AppStorage
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Root { get; }
    public string Packages => Path.Combine(Root, "Installed");
    public AppStorage(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DinoSaurList"));
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Packages);
    }
    public async Task<T?> ReadAsync<T>(string file, CancellationToken token = default)
    {
        var path = Path.Combine(Root, file);
        if (!File.Exists(path)) return default;
        try { await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(stream, Json, token); }
        catch (JsonException e) { throw new InvalidDataException($"Saved {file} is invalid; original file was preserved.", e); }
    }
    public async Task WriteAsync<T>(string file, T value, CancellationToken token = default)
    {
        var path = Path.Combine(Root, file); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, value, Json, token);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record UserSettings(string BoxManagerUrl = "https://npi-service.kor.broadcom.net/api/box-manager/",
    string UpdateFeedUrl = DinoSaurList.Core.Updates.AppUpdateService.DefaultFeed, string Theme = "System",
    string Language = "en", string ProfileName = "", string ProfileMessage = "", string JiraUrl = "", int RefreshMode = 1, List<string>? RefreshSelectedFolders = null);
