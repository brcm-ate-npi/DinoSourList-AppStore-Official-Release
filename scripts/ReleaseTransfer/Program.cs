using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? throw new InvalidOperationException("Repository required");
var token = Environment.GetEnvironmentVariable("GH_TOKEN") ?? throw new InvalidOperationException("Ephemeral job token required");
var tag = Environment.GetEnvironmentVariable("NATIVE_RELEASE_TAG") ?? throw new InvalidOperationException("Release tag required");
var sha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? throw new InvalidOperationException("Commit required");
var rid = Environment.GetEnvironmentVariable("NATIVE_RID") ?? throw new InvalidOperationException("Runtime required");
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.UserAgent.ParseAdd("DinoSaurList-Release-CI/1.0");
http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
var api = $"https://api.github.com/repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}";
async Task<JsonObject> Release() => JsonNode.Parse(await http.GetStringAsync(api))!.AsObject();
var release = await Release();
if (release["draft"]?.GetValue<bool>() != true || release["target_commitish"]?.GetValue<string>() != sha)
    throw new InvalidOperationException("Only a draft for this exact commit can receive assets.");
var id = release["id"]!.GetValue<long>();
var files = Directory.GetFiles("Releases").Where(f => !Path.GetFileName(f).StartsWith('.')).ToList();
if (!files.Any(f => f.EndsWith("-full.nupkg", StringComparison.Ordinal))) throw new InvalidDataException("Native package missing");
var evidence = Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!, "native-installed-evidence");
if (!File.Exists(Path.Combine(evidence, "installed-ui.json"))) throw new InvalidDataException("Installed UI proof missing");
var evidenceZip = Path.Combine(Path.GetTempPath(), $"validation-{rid}-{Guid.NewGuid():N}.zip");
try
{
    ZipFile.CreateFromDirectory(evidence, evidenceZip);
    files.Add(evidenceZip);
    foreach (var file in files)
    {
        var name = file == evidenceZip ? $"validation-{rid}.zip" : Path.GetFileName(file);
        // Re-read after each upload: both platform jobs share this draft.
        release = await Release();
        if (release["draft"]?.GetValue<bool>() != true) throw new InvalidOperationException("Release was published during staging");
        var existing = release["assets"]!.AsArray().FirstOrDefault(a => a!["name"]!.GetValue<string>() == name);
        if (existing is not null)
        {
            using var input = File.OpenRead(file);
            var digest = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();
            if (existing["digest"]?.GetValue<string>() == digest) { Console.WriteLine($"Verified existing draft asset {name}"); continue; }
            using var removed = await http.DeleteAsync($"https://api.github.com/repos/{repository}/releases/assets/{existing["id"]!.GetValue<long>()}");
            removed.EnsureSuccessStatusCode();
        }
        using var stream = File.OpenRead(file);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await http.PostAsync($"https://uploads.github.com/repos/{repository}/releases/{id}/assets?name={Uri.EscapeDataString(name)}", content);
        if (!response.IsSuccessStatusCode) throw new IOException($"Release upload failed for {name}: HTTP {(int)response.StatusCode}");
        var uploaded = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        if (uploaded["size"]!.GetValue<long>() != stream.Length) throw new InvalidDataException("Uploaded asset length mismatch");
        using var verify = File.OpenRead(file);
        var expected = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(verify)).ToLowerInvariant();
        if (uploaded["digest"]?.GetValue<string>() != expected) throw new InvalidDataException("Uploaded asset checksum mismatch");
        Console.WriteLine($"Staged {name} ({stream.Length} bytes)");
    }
}
finally { File.Delete(evidenceZip); }
