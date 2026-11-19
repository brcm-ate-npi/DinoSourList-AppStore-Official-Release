using System.Text.Json;
namespace DinoSaurList.Core.Registry;

// News follows the original main-branch document; application updates remain release based.
public sealed class NewsService(HttpClient http, AppStorage storage)
{
    public const string Root = "https://raw.githubusercontent.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/main/";
    public async Task<string> RefreshAsync(CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var markdown = await http.GetStringAsync(Root + "News/news.md", deadline.Token);
        if (markdown.Length > 2_000_000 || string.IsNullOrWhiteSpace(markdown)) throw new InvalidDataException("News document is empty or too large.");
        using var version = JsonDocument.Parse(await http.GetStringAsync(Root + "config-record/version.json", deadline.Token));
        var text = markdown.Replace("{{APP_VERSION}}", version.RootElement.GetProperty("version").GetString());
        await storage.WriteAsync("official-news.json", text, token);
        return text;
    }
    public async Task<string> CachedAsync(CancellationToken token = default) =>
        await storage.ReadAsync<string>("official-news.json", token) ?? UserStateService.BundledNews;
}
