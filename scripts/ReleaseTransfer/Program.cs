using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? throw new InvalidOperationException("Repository required");
var token = Environment.GetEnvironmentVariable("GH_TOKEN") ?? throw new InvalidOperationException("Ephemeral job token required");
var tag = Environment.GetEnvironmentVariable("NATIVE_RELEASE_TAG") ?? throw new InvalidOperationException("Release tag required");
var sha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? throw new InvalidOperationException("Commit required");
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.UserAgent.ParseAdd("DinoSaurList-Release-CI/1.0");
http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
// The tag endpoint omits drafts; list authenticated drafts, then select this exact tag.
var api = $"https://api.github.com/repos/{repository}/releases?per_page=100";
async Task<JsonObject> Release() => JsonNode.Parse(await http.GetStringAsync(api))!.AsArray()
    .Single(r => r!["tag_name"]!.GetValue<string>() == tag)!.AsObject();
var release = await Release();
if (release["draft"]?.GetValue<bool>() != true || release["target_commitish"]?.GetValue<string>() != sha)
    throw new InvalidOperationException("Only a draft for this exact commit can receive assets.");
var id = release["id"]!.GetValue<long>();
// Stable packages also publish compatibility manifests for already-installed
// preview clients. The referenced package remains the single stable build.
if (!tag.Contains('-'))
{
    foreach (var platform in new[] { "win", "osx" })
    {
        foreach (var (source, destination) in new[]
        {
            ($"releases.{platform}.json", $"releases.{platform}-preview.json"),
            ($"assets.{platform}.json", $"assets.{platform}-preview.json"),
            (platform == "win" ? "RELEASES" : $"RELEASES-{platform}", $"RELEASES-{platform}-preview")
        })
        {
            var original = Path.Combine("Releases", source);
            if (File.Exists(original)) File.Copy(original, Path.Combine("Releases", destination), true);
        }
    }
}
var files = Directory.GetFiles("Releases").Where(f => !Path.GetFileName(f).StartsWith('.')).ToList();
if (!files.Any(f => f.EndsWith("-full.nupkg", StringComparison.Ordinal))) throw new InvalidDataException("Native package missing");
foreach (var file in files)
{
    var name = Path.GetFileName(file);
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
