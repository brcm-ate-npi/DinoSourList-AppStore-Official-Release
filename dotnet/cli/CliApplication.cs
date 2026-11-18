using System.Text.Json;
using DinoSaurList.Core.Assistant;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Install;
using DinoSaurList.Core.Launch;
using DinoSaurList.Core.Profile;
using DinoSaurList.Core.Registry;
using DinoSaurList.Core.Updates;

namespace DinoSaurList.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation = default)
    {
        // JSON stdout is stable for automation; progress and diagnostics go to stderr.
        var command = args.FirstOrDefault() ?? "help";
        string? Option(string name) { var i = Array.IndexOf(args, name); if (i < 0) return null; if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Missing value for {name}."); return args[i + 1]; }
        string Required(string name) => Option(name) ?? throw new ArgumentException($"{name} is required.");
        void Json<T>(T value) => output.WriteLine(JsonSerializer.Serialize(value, AppStorage.Json));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));
        var token = deadline.Token;
        try
        {
            if (command is "help" or "--help" or "-h")
            {
                await output.WriteLineAsync("""
        DinoSaurList CLI — the same Core services as the Avalonia UI
        catalogue [--refresh]                    List tool metadata (cache first)
        readme --tool ID                         Print the tool README
        guide --tool ID                          Download the guide and print its path
        recommend --query TEXT                   Rank tools using their READMEs
        installed                               List installed versions
        install --tool ID [--version VERSION]    Download, verify and install a package
        launch --tool ID [--version VERSION]     Start the pinned/latest installed version
        pin --tool ID --version VERSION         Set the default installed version
        remove --tool ID --version VERSION --yes Remove managed files / forget imported version
        favourites [--tool ID --set true|false]  List or update favourites
        import-preview --root DIR               Inspect a legacy installation
        import --root DIR --yes                  Link legacy installations and import favourites
        settings [--file JSON]                  Read or replace local settings
        news [--file MARKDOWN | --reset]         Read, edit or reset local news
        profile --author NAME [--file JSON]      Read or save a shared Box profile
        photo --author NAME [--file PNG]         Read or save a shared Box photo
        activity                                List this device's recorded actions
        update [--download | --apply --yes]      Check, download or apply an app update
        All commands: --data-dir DIR, --server HTTPS_URL
        Removal honors Flow Delete on Windows; forgetting an imported version preserves its original files.
        """); return 0;
            }
            var storage = new AppStorage(Option("--data-dir"));
            var state = new UserStateService(storage); var settings = await state.SettingsAsync(token);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            var box = new BoxManagerClient(http, Option("--server") ?? settings.BoxManagerUrl);
            var catalogue = new CatalogueService(box, storage); var installations = new InstallationStore(storage);
            var launcher = new ToolLauncher(); var installed = new InstalledToolService(storage, installations, launcher);
            Task<CatalogueSnapshot> Catalogue() => LoadCatalogueAsync();
            async Task<CatalogueSnapshot> LoadCatalogueAsync() => args.Contains("--refresh") ? await catalogue.RefreshAsync(null, token) : await catalogue.LoadCacheAsync(token) ?? await catalogue.RefreshAsync(null, token);
            async Task<ToolEntry> Tool() => (await Catalogue()).Tools.SingleOrDefault(t => t.Id == Required("--tool")) ?? throw new ArgumentException("Unknown tool ID.");
            void Confirm() { if (!args.Contains("--yes")) throw new ArgumentException("This operation requires --yes."); }
            switch (command)
            {
                case "catalogue": Json(await Catalogue()); break;
                case "readme": await output.WriteLineAsync(await new DocumentationService(box, storage).ReadmeAsync(await Tool(), token)); break;
                case "guide": Json(new { path = await new DocumentationService(box, storage).GuideAsync(await Tool(), token) }); break;
                case "recommend":
                    var input = new List<(ToolEntry, string)>(); var docs = new DocumentationService(box, storage);
                    foreach (var tool in (await Catalogue()).Tools) input.Add((tool, await docs.ReadmeAsync(tool, token)));
                    var assistant = new ToolAssistant(); assistant.Index(input); Json(assistant.Recommend(Required("--query"))); break;
                case "installed": Json(await installations.LoadAsync(token)); break;
                case "install":
                    var selected = await Tool(); var version = Option("--version");
                    var package = selected.Packages.FirstOrDefault(p => (version is null || p.Version == version) && (p.Platform == "any" || p.Platform == ToolLauncher.Platform)) ?? throw new PlatformNotSupportedException("No matching package for this version and OS.");
                    var record = await new InstallService(box, storage, installations, launcher).InstallAsync(selected, package, new Progress<DownloadProgress>(p => error.WriteLine($"{p.Percent:F0}% {p.Bytes} bytes")), token);
                    await new ActivityStore(storage).RecordAsync(record.ToolId, "install", token); Json(record); break;
                case "launch":
                    var target = await installed.FindAsync(Required("--tool"), Option("--version"), token); await installed.LaunchAsync(target, token); Json(new { launched = target }); break;
                case "pin":
                    var pinned = await installed.FindAsync(Required("--tool"), Required("--version"), token); await state.PinAsync(pinned, token); Json(pinned); break;
                case "remove":
                    Confirm(); var removed = await installed.FindAsync(Required("--tool"), Required("--version"), token); await installed.RemoveAsync(removed, token); Json(new { removed }); break;
                case "favourites":
                    if (Option("--tool") is not null) await state.SetFavouriteAsync((await Tool()).FolderName, bool.Parse(Required("--set")), token);
                    Json(await state.FavouritesAsync(token)); break;
                case "import-preview": Json(await LegacyImporter.PreviewAsync(Required("--root"), (await Catalogue()).Tools, token)); break;
                case "import":
                    Confirm(); var preview = await LegacyImporter.PreviewAsync(Required("--root"), (await Catalogue()).Tools, token); await new LegacyImporter(storage, installations).ImportAsync(preview, token); Json(preview); break;
                case "settings":
                    if (Option("--file") is { } settingsFile) await state.SaveSettingsAsync(JsonSerializer.Deserialize<UserSettings>(await File.ReadAllTextAsync(settingsFile, token), AppStorage.Json) ?? throw new InvalidDataException("Settings are empty."), token);
                    Json(await state.SettingsAsync(token)); break;
                case "news":
                    if (args.Contains("--reset")) state.ResetNews();
                    else if (Option("--file") is { } newsFile) await state.SaveNewsAsync(await File.ReadAllTextAsync(newsFile, token), token);
                    await output.WriteLineAsync(await state.NewsAsync(UserStateService.BundledNews, token)); break;
                case "profile":
                    var profiles = new DeveloperProfiles(box, storage); var author = Required("--author");
                    if (Option("--file") is { } profileFile)
                    {
                        var profile = JsonSerializer.Deserialize<DeveloperProfile>(await File.ReadAllTextAsync(profileFile, token), AppStorage.Json) ?? throw new InvalidDataException("Profile is empty.");
                        if (profile.Author != author) throw new ArgumentException("Profile author differs from --author."); await profiles.SaveAsync(profile, token);
                    }
                    Json(await profiles.LoadAsync(author, token)); break;
                case "photo":
                    var photos = new DeveloperProfiles(box, storage); var photoAuthor = Required("--author");
                    if (Option("--file") is { } photoFile) await photos.SavePhotoAsync(photoAuthor, await File.ReadAllBytesAsync(photoFile, token), token);
                    Json(new { path = await photos.LoadPhotoAsync(photoAuthor, token) }); break;
                case "activity": Json(await new ActivityStore(storage).LoadAsync(token)); break;
                case "update":
                    var updater = new AppUpdateService(); var status = await updater.CheckAsync(settings.UpdateFeedUrl);
                    if (args.Contains("--download") || args.Contains("--apply")) { if (args.Contains("--apply")) Confirm(); status = await updater.DownloadAsync(p => error.WriteLine($"{p}%")); }
                    Json(new { status }); if (args.Contains("--apply")) updater.Apply(); break;
                default: throw new ArgumentException("Unknown command; use help to list commands.");
            }
            return 0;
        }
        catch (OperationCanceledException) { await error.WriteLineAsync("Cancelled."); return 130; }
        catch (Exception e) { await error.WriteLineAsync(e.GetType().Name + ": " + e.Message); return 1; }

    }
}
