using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Profile;

public sealed record ProfileLink(string Title, string Url, string Key = "");
public sealed record DeveloperProfile(string Author, string Tweet, string Updated, List<ProfileLink> Links);
public sealed class DeveloperProfiles(IBoxClient box, AppStorage storage)
{
    private string? folderId;
    public static string SafeName(string author) => !string.IsNullOrWhiteSpace(author) && author.Length <= 100
        ? Regex.Replace(author, "[^A-Za-z0-9_.-]+", "_") : throw new ArgumentException("Select a developer first.");
    private async Task<string> FolderAsync(CancellationToken token)
    {
        if (folderId is not null) return folderId;
        var root = await box.ListAsync(CatalogueService.RootFolder, token);
        return folderId = root.FirstOrDefault(f => f.Type == "folder" && f.Name == ".devprofile-data")?.Id
            ?? throw new IOException("The shared developer profile folder is unavailable.");
    }
    public async Task<DeveloperProfile> LoadAsync(string author, CancellationToken token)
    {
        var name = SafeName(author); var cached = await storage.ReadAsync<DeveloperProfile>("profile-" + name + ".json", token);
        var files = await box.ListAsync(await FolderAsync(token), token);
        var profile = files.FirstOrDefault(f => f.Name.Equals(name + ".json", StringComparison.OrdinalIgnoreCase));
        var linksFile = files.FirstOrDefault(f => f.Name.Equals(name + ".links.json", StringComparison.OrdinalIgnoreCase));
        var tweet = ""; var updated = ""; var links = new List<ProfileLink>();
        if (profile is not null)
        {
            using var doc = JsonDocument.Parse(await box.ReadTextAsync(profile.Id, token));
            tweet = doc.RootElement.TryGetProperty("tweet", out var text) ? text.GetString() ?? "" : "";
            updated = doc.RootElement.TryGetProperty("updated", out var date) ? date.GetString() ?? "" : "";
        }
        if (linksFile is not null)
        {
            using var doc = JsonDocument.Parse(await box.ReadTextAsync(linksFile.Id, token));
            if (doc.RootElement.TryGetProperty("links", out var values))
                foreach (var link in values.EnumerateArray())
                {
                    var url = link.TryGetProperty("url", out var address) ? address.GetString() : null;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
                    links.Add(new(link.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "", uri.AbsoluteUri));
                }
        }
        var result = new DeveloperProfile(author, tweet, updated, links);
        await storage.WriteAsync("profile-" + name + ".json", result, token);
        return result;
    }
    public async Task<DeveloperProfile?> LoadCacheAsync(string author, CancellationToken token) => await storage.ReadAsync<DeveloperProfile>("profile-" + SafeName(author) + ".json", token);
    public async Task SaveAsync(DeveloperProfile profile, CancellationToken token)
    {
        if (profile.Tweet.Length > 1000 || profile.Links.Count > 30) throw new ArgumentException("Profile message or links exceed limits.");
        foreach (var link in profile.Links)
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Profile links must use HTTP or HTTPS.");
        var name = SafeName(profile.Author); await storage.WriteAsync("profile-" + name + ".json", profile, token);
        var folder = await FolderAsync(token);
        var json = JsonSerializer.Serialize(new { author = profile.Author, tweet = profile.Tweet, updated = profile.Updated }, AppStorage.Json);
        await box.UploadAsync(folder, name + ".json", Encoding.UTF8.GetBytes(json), token);
        json = JsonSerializer.Serialize(new { author = profile.Author, links = profile.Links, updated = profile.Updated }, AppStorage.Json);
        await box.UploadAsync(folder, name + ".links.json", Encoding.UTF8.GetBytes(json), token);
    }
    public async Task<string?> LoadPhotoAsync(string author, CancellationToken token)
    {
        var name = SafeName(author); var folder = Path.Combine(storage.Root, "Photos"); Directory.CreateDirectory(folder);
        var file = (await box.ListAsync(await FolderAsync(token), token)).FirstOrDefault(f => f.Name == name + ".png");
        if (file is null) return null;
        if (file.Size is > 8 * 1024 * 1024) throw new InvalidDataException("Profile photo exceeds 8 MiB.");
        var path = Path.Combine(folder, file.Id + "-" + file.Sha1 + ".png");
        if (!File.Exists(path)) await box.DownloadAsync(file, path, null, token);
        return path;
    }
    public Task SavePhotoAsync(string author, byte[] png, CancellationToken token) => SavePhotoInternalAsync(author, png, token);
    private async Task SavePhotoInternalAsync(string author, byte[] png, CancellationToken token)
    {
        if (png.Length > 8 * 1024 * 1024 || png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new ArgumentException("Profile photo must be PNG, at most 8 MiB.");
        await box.UploadAsync(await FolderAsync(token), SafeName(author) + ".png", png, token);
    }
}
