using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Box;

public interface IBoxClient
{
    Task<List<BoxItem>> ListAsync(string folderId, CancellationToken token = default);
    Task<string> ReadTextAsync(string fileId, CancellationToken token = default);
    Task DownloadAsync(BoxItem file, string destination, IProgress<DownloadProgress>? progress, CancellationToken token);
    Task UploadAsync(string folderId, string name, byte[] bytes, CancellationToken token) => throw new NotSupportedException("Uploads are unavailable.");
}
public sealed record DownloadProgress(long Bytes, long? Total)
{
    public double Percent => Total is > 0 ? Math.Clamp(100.0 * Bytes / Total.Value, 0, 100) : 0;
}

public sealed class BoxManagerClient : IBoxClient
{
    private readonly HttpClient http;
    public BoxManagerClient(HttpClient http, string baseUrl)
    {
        var uri = new Uri(baseUrl.TrimEnd('/') + "/");
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new ArgumentException("Box Manager must use HTTPS (HTTP is allowed only on loopback).");
        this.http = http;
        http.BaseAddress = uri;
    }
    private static string Id(string value) => value.Length is > 0 and <= 30 && value.All(char.IsAsciiDigit) ? value : throw new ArgumentException("Invalid Box ID.");
    public async Task<List<BoxItem>> ListAsync(string folderId, CancellationToken token = default)
    {
        var result = new List<BoxItem>(); string? marker = null;
        var seen = new HashSet<string>();
        do
        {
            var path = "api/v1/box/folders/" + Id(folderId) + "/items" + (marker is null ? "" : "?marker=" + Uri.EscapeDataString(marker));
            using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<FolderPage>(AppStorage.Json, token) ?? throw new InvalidDataException("Empty Box folder response.");
            result.AddRange(page.Items);
            if (result.Count > 10000) throw new InvalidDataException("Box folder exceeds 10,000 items.");
            marker = page.NextMarker;
            if (marker is not null && !seen.Add(marker)) throw new InvalidDataException("Box pagination repeated a marker.");
        } while (!string.IsNullOrEmpty(marker));
        return result;
    }
    public async Task<string> ReadTextAsync(string fileId, CancellationToken token = default)
    {
        using var response = await http.GetAsync("api/v1/box/download/" + Id(fileId), HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 2 * 1024 * 1024) throw new InvalidDataException("Document exceeds 2 MiB.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream(); var chunk = new byte[65536]; int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Document exceeds 2 MiB.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\ufeff');
    }
    public async Task DownloadAsync(BoxItem file, string destination, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        if (file.Sha1 is not { Length: 40 } || !file.Sha1.All(Uri.IsHexDigit))
            throw new InvalidDataException("Package checksum is unavailable; refresh the catalogue before installing.");
        using var response = await http.GetAsync("api/v1/box/download/" + Id(file.Id), HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = file.Size ?? response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var created = false;
        try
        {
            long count = 0; var bytes = new byte[131072]; int read;
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, bytes.Length, true))
            {
                created = true;
                while ((read = await input.ReadAsync(bytes, token)) > 0)
                {
                    count += read;
                    if (count > 20L * 1024 * 1024 * 1024 || (total.HasValue && count > total.Value)) throw new InvalidDataException("Package size exceeds expected limit.");
                    hash.AppendData(bytes, 0, read); await output.WriteAsync(bytes.AsMemory(0, read), token);
                    progress?.Report(new(count, total));
                }
            }
            if (total.HasValue && count != total.Value) throw new InvalidDataException("Package download was incomplete.");
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package checksum did not match Box.");
        }
        catch { if (created && File.Exists(destination)) File.Delete(destination); throw; }
    }
    public async Task UploadAsync(string folderId, string name, byte[] bytes, CancellationToken token)
    {
        if (bytes.Length > 8 * 1024 * 1024 || string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\')) throw new ArgumentException("Invalid profile file.");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(Id(folderId)), "folder_id");
        content.Add(new ByteArrayContent(bytes), "file", name);
        using var response = await http.PostAsync("api/v1/box/upload", content, token); response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var job = result.RootElement.GetProperty("job_id").GetString() ?? throw new InvalidDataException("Upload job was not returned.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(2));
        while (true)
        {
            using var status = await http.GetAsync("api/v1/box/upload/status/" + Uri.EscapeDataString(job), deadline.Token); status.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync(deadline.Token));
            var state = body.RootElement.GetProperty("status").GetString();
            if (state == "done") return;
            if (state == "error") throw new IOException("Box could not save the profile. The local copy was preserved.");
            await Task.Delay(750, deadline.Token);
        }
    }
    private sealed record FolderPage(List<BoxItem> Items, [property: System.Text.Json.Serialization.JsonPropertyName("next_marker")] string? NextMarker);
}
