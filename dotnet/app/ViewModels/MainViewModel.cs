using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DinoSaurList.Core.Assistant;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Install;
using DinoSaurList.Core.Launch;
using DinoSaurList.Core.Profile;
using DinoSaurList.Core.Registry;
using DinoSaurList.Core.Updates;

namespace DinoSaurList.ViewModels;

public sealed record ContributionCell(string Label, string Colour);

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public AppStorage Storage { get; }
    private HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private IBoxClient box = null!;
    private CatalogueService catalogue = null!;
    private DocumentationService documents = null!;
    private DeveloperProfiles profiles = null!;
    private readonly InstallationStore installations;
    private readonly UserStateService state;
    private readonly InstalledToolService installedTools;
    private readonly ToolLauncher launcher = new();
    private readonly ActivityStore activity;
    private readonly ToolAssistant assistant = new();
    private readonly AppUpdateService updater = new();
    private readonly Dictionary<string, string> readmes = [];
    private Dictionary<string, string> activeVersions = [];
    private List<ToolEntry> allTools = [];
    private HashSet<string> favourites = new(StringComparer.OrdinalIgnoreCase);
    private UserSettings settings = new();
    private CancellationTokenSource operation = new();
    public ObservableCollection<ToolCardViewModel> Tools { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["All categories"];
    public ObservableCollection<string> Authors { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<ContributionCell> ContributionDays { get; } = [];
    public ObservableCollection<Recommendation> Recommendations { get; } = [];
    public string AppVersion => AppUpdateService.Version;
    public string DataDirectory => Storage.Root;
    public string RuntimePlatform => ToolLauncher.Platform;
    [ObservableProperty] private string page = "Store";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string category = "All categories";
    [ObservableProperty] private bool favouritesOnly;
    [ObservableProperty] private ToolCardViewModel? selectedTool;
    [ObservableProperty] private ToolPackage? selectedPackage;
    [ObservableProperty] private InstalledTool? selectedInstallation;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string status = "Loading saved catalogue…";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string query = "";
    [ObservableProperty] private string assistantStatus = "Searches tool names and cached READMEs with TF-IDF. Index documentation to include the full descriptions.";
    [ObservableProperty] private string readme = "Select a tool to read its documentation.";
    [ObservableProperty] private string news = "";
    [ObservableProperty] private string profileName = "";
    [ObservableProperty] private string profileMessage = "";
    [ObservableProperty] private string profileLinksText = "";
    [ObservableProperty] private Bitmap? profilePhoto;
    [ObservableProperty] private string jiraUrl = "";
    [ObservableProperty] private int contributionYear = DateTime.Today.Year;
    [ObservableProperty] private string contributionSummary = "Refresh the catalogue to load Box upload/edit dates.";
    [ObservableProperty] private string profileActivity = "";
    [ObservableProperty] private string boxManagerUrl = "";
    [ObservableProperty] private string updateFeedUrl = "";
    [ObservableProperty] private string theme = "System";
    [ObservableProperty] private int refreshMode = 1;
    [ObservableProperty] private string refreshSelectedFolders = "";
    [ObservableProperty] private string legacyRoot = "";
    [ObservableProperty] private string legacySummary = "Preview old installations and favourites before importing. Existing folders are linked in place and never deleted.";
    [ObservableProperty] private string updateStatus = "";
    private LegacyPreview? legacyPreview;
    public bool IsStore => Page is "Store" or "Installed";
    public bool IsAssistant => Page == "Assistant";
    public bool IsNews => Page == "News";
    public bool IsProfile => Page == "Developers";
    public bool IsSettings => Page == "Settings";
    public bool IsLogs => Page == "Logs";
    public bool Idle => !Busy;
    public IEnumerable<ToolPackage> AvailablePackages => SelectedTool?.Packages ?? [];
    public IEnumerable<InstalledTool> InstalledVersions => SelectedTool?.InstalledVersions ?? [];
    public bool HasSelection => SelectedTool is not null;
    public bool CanInstall => Idle && SelectedTool?.CanInstall == true && SelectedPackage is not null;
    public bool CanLaunch => Idle && SelectedInstallation is not null && !string.IsNullOrWhiteSpace(SelectedInstallation.ExecutionFile);
    public IEnumerable<ToolCardViewModel> VisibleTools => Tools.Where(t => (Page != "Installed" || t.HasInstalled)
        && (!FavouritesOnly || t.IsFavourite) && (Category == "All categories" || Category == t.Category)
        && (string.IsNullOrWhiteSpace(Search) || (t.Name + " " + t.Author + " " + t.Category).Contains(Search, StringComparison.OrdinalIgnoreCase)));
    public string VisibleCount => $"{VisibleTools.Count()} of {Tools.Count} tools";
    public MainViewModel(string? dataRoot = null)
    {
        Storage = new(dataRoot); installations = new(Storage); state = new(Storage); installedTools = new(Storage, installations, launcher); activity = new(Storage); ConfigureClient();
    }
    private void ConfigureClient()
    {
        http.Dispose(); http = new() { Timeout = TimeSpan.FromMinutes(30) };
        box = new BoxManagerClient(http, settings.BoxManagerUrl);
        catalogue = new(box, Storage); documents = new(box, Storage); profiles = new(box, Storage);
    }
    public async Task InitializeAsync() => await GuardAsync(async token =>
    {
        settings = await state.SettingsAsync(token);
        BoxManagerUrl = settings.BoxManagerUrl; UpdateFeedUrl = settings.UpdateFeedUrl; Theme = settings.Theme; RefreshMode = settings.RefreshMode; RefreshSelectedFolders = string.Join(", ", settings.RefreshSelectedFolders ?? []);
        ProfileName = settings.ProfileName; ProfileMessage = settings.ProfileMessage; JiraUrl = settings.JiraUrl;
        ApplyTheme(); ConfigureClient();
        favourites = await state.FavouritesAsync(token);
        News = await state.NewsAsync(UserStateService.BundledNews, token);
        var saved = await catalogue.LoadCacheAsync(token);
        if (saved is not null) { allTools = saved.Tools; await RebuildAsync(token); Status = $"Saved catalogue from {saved.FetchedAt.LocalDateTime:g}. Refresh to check Box."; }
        else { var live = await catalogue.RefreshAsync(new Progress<string>(s => Status = s), token); allTools = live.Tools; await RebuildAsync(token); Status = $"Loaded {allTools.Count} tools."; }
        await LoadActivityAsync(token);
    });
    private async Task RebuildAsync(CancellationToken token)
    {
        activeVersions = await state.ActiveVersionsAsync(token);
        var current = SelectedTool?.Id; var local = await installations.LoadAsync(token);
        Tools.Clear(); Categories.Clear(); Categories.Add("All categories"); Authors.Clear();
        foreach (var tool in allTools) Tools.Add(new(tool, local, favourites.Contains(tool.FolderName), activeVersions.GetValueOrDefault(tool.Id)));
        foreach (var value in allTools.Select(t => t.Category).Distinct().Order()) Categories.Add(value);
        foreach (var value in allTools.Select(t => t.Author).Distinct().Order()) Authors.Add(value);
        SelectedTool = Tools.FirstOrDefault(t => t.Id == current) ?? Tools.FirstOrDefault();
        assistant.Index(allTools.Select(t => (t, readmes.GetValueOrDefault(t.Id, ""))));
        NotifyTools(); BuildContributions();
    }
    private void NotifyTools()
    {
        var current = SelectedTool;
        OnPropertyChanged(nameof(VisibleTools)); OnPropertyChanged(nameof(VisibleCount));
        SelectedTool = VisibleTools.FirstOrDefault(t => t == current) ?? VisibleTools.FirstOrDefault();
    }
    partial void OnSearchChanged(string value) => NotifyTools();
    partial void OnCategoryChanged(string value) => NotifyTools();
    partial void OnFavouritesOnlyChanged(bool value) => NotifyTools();
    partial void OnPageChanged(string value)
    {
        foreach (var name in new[] { nameof(IsStore), nameof(IsAssistant), nameof(IsNews), nameof(IsProfile), nameof(IsSettings), nameof(IsLogs) }) OnPropertyChanged(name);
        NotifyTools();
    }
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(Idle)); OnPropertyChanged(nameof(CanInstall)); OnPropertyChanged(nameof(CanLaunch)); }
    partial void OnSelectedToolChanged(ToolCardViewModel? value)
    {
        OnPropertyChanged(nameof(AvailablePackages)); OnPropertyChanged(nameof(InstalledVersions));
        SelectedPackage = value?.Packages.FirstOrDefault(p => p.Platform == ToolLauncher.Platform || p.Platform == "any") ?? value?.Packages.FirstOrDefault();
        SelectedInstallation = value?.Installed;
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(CanInstall)); OnPropertyChanged(nameof(CanLaunch));
    }
    partial void OnSelectedPackageChanged(ToolPackage? value)
    {
        OnPropertyChanged(nameof(CanInstall));
        if (value is null && SelectedTool is not null) Dispatcher.UIThread.Post(() =>
        {
            if (SelectedPackage is null) SelectedPackage = SelectedTool?.Packages.FirstOrDefault(p => p.Platform == ToolLauncher.Platform || p.Platform == "any") ?? SelectedTool?.Packages.FirstOrDefault();
        });
    }
    partial void OnSelectedInstallationChanged(InstalledTool? value)
    {
        OnPropertyChanged(nameof(CanLaunch));
        if (value is null && SelectedTool?.Installed is not null) Dispatcher.UIThread.Post(() =>
        {
            if (SelectedInstallation is null) SelectedInstallation = SelectedTool?.Installed;
        });
    }
    [RelayCommand] private void Navigate(string value) => Page = value;
    [RelayCommand] private void ShowFavourites() { FavouritesOnly = true; Page = "Store"; }
    [RelayCommand] private void UsefulLinks() => DocumentRequested?.Invoke("Useful links", UserStateService.UsefulLinks);
    [RelayCommand] private void PreviewNews() => DocumentRequested?.Invoke("News & updates", News);
    [RelayCommand]
    private async Task GuideAsync() => await GuardAsync(async token =>
    {
        if (SelectedTool is not { } tool) return;
        var target = await documents.GuideAsync(tool.Tool, token);
        OpenUri(target); Status = "Opened " + Path.GetFileName(target);
    });
    [RelayCommand]
    private async Task PinVersionAsync() => await GuardAsync(async token =>
    {
        if (SelectedInstallation is not { } tool) return;
        await installedTools.ActivateAsync(tool, token);
        await RebuildAsync(token); Status = $"Default version is now {tool.Version}.";
    });
    public Func<string, Task<bool>>? ConfirmRequested { get; set; }
    [RelayCommand]
    private async Task RemoveVersionAsync() => await GuardAsync(async token =>
    {
        if (SelectedInstallation is not { } tool || ConfirmRequested is null) return;
        var message = tool.Imported ? "Forget this imported version? Its original files will be preserved." : "Remove this version? Flow.txt may also request a Windows system uninstall.";
        if (!await ConfirmRequested(message)) return;
        await installedTools.RemoveAsync(tool, token);
        await RebuildAsync(token); Status = "Removed the selected version from this application.";
    });

    [RelayCommand] private void Cancel() => operation.Cancel();
    [RelayCommand]
    private async Task RefreshAsync() => await GuardAsync(async token =>
    {
        var result = await catalogue.RefreshAsync(new Progress<string>(s => Status = s), token, new(RefreshMode, RefreshSelectedFolders.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        allTools = result.Tools; await RebuildAsync(token); Status = $"{allTools.Count} tools refreshed. {result.Warnings.Count} warnings.";
        foreach (var warning in result.Warnings) Log(warning);
    });
    [RelayCommand]
    private async Task FavouriteAsync() => await GuardAsync(async token =>
    {
        if (SelectedTool is not { } tool) return;
        tool.IsFavourite = await state.SetFavouriteAsync(tool.Tool.FolderName, !tool.IsFavourite, token);
        if (tool.IsFavourite) favourites.Add(tool.Tool.FolderName); else favourites.Remove(tool.Tool.FolderName);
        NotifyTools(); Status = "Favourites saved.";
    });
    [RelayCommand]
    private async Task InstallAsync() => await GuardAsync(async token =>
    {
        if (SelectedTool is not { } selected || SelectedPackage is not { } package) return;
        var service = new InstallService(box, Storage, installations, launcher);
        var record = await service.InstallAsync(selected.Tool, package, new Progress<DownloadProgress>(p => { Progress = p.Percent; Status = $"Downloading {p.Bytes / 1048576.0:F1} MiB ({p.Percent:F0}%)"; }), token);
        await activity.RecordAsync(record.ToolId, "install", token); await RebuildAsync(token); await LoadActivityAsync(token); Status = $"Installed {record.Name} {record.Version}.";
    });
    [RelayCommand]
    private async Task LaunchAsync() => await GuardAsync(async token =>
    {
        if (SelectedInstallation is not { } tool) return;
        await installedTools.LaunchAsync(tool, token); Status = $"Started {tool.Name}.";
    });
    [RelayCommand]
    private async Task ReadmeAsync() => await GuardAsync(async token =>
    {
        if (SelectedTool is not { } tool) return;
        Readme = await documents.ReadmeAsync(tool.Tool, token); readmes[tool.Id] = Readme; Status = $"README: {tool.Name}";
        DocumentRequested?.Invoke(tool.Name, Readme);
    });
    public event Action<string, string>? DocumentRequested;
    [RelayCommand]
    private async Task IndexDocumentationAsync() => await GuardAsync(async token =>
    {
        foreach (var tool in allTools)
        {
            Status = $"Indexing {tool.Name}";
            try { readmes[tool.Id] = await documents.ReadmeAsync(tool, token); }
            catch (HttpRequestException) { Log($"README unavailable: {tool.Name}"); }
        }
        assistant.Index(allTools.Select(t => (t, readmes.GetValueOrDefault(t.Id, ""))));
        AssistantStatus = $"Indexed {readmes.Count} README documents. This is keyword/TF-IDF retrieval, not a generative model."; Status = AssistantStatus;
    });
    [RelayCommand]
    private void Recommend()
    {
        Recommendations.Clear(); foreach (var value in assistant.Recommend(Query)) Recommendations.Add(value);
        AssistantStatus = Recommendations.Count == 0 ? "No matching documentation. Try other task terms or index the READMEs." : $"{Recommendations.Count} matching tools.";
    }
    [RelayCommand]
    private void SelectRecommendation(Recommendation? value)
    {
        if (value is null) return; SelectedTool = Tools.FirstOrDefault(t => t.Id == value.Tool.Id); Page = "Store";
    }
    [RelayCommand]
    private async Task LoadSharedProfileAsync() => await GuardAsync(async token =>
    {
        DeveloperProfile? profile;
        try { profile = await profiles.LoadAsync(ProfileName, token); Status = "Shared Box profile loaded."; }
        catch (HttpRequestException) { profile = await profiles.LoadCacheAsync(ProfileName, token); Status = "Box unavailable; showing the last saved profile."; }
        if (profile is not null) { ProfileMessage = profile.Tweet; ProfileLinksText = string.Join('\n', profile.Links.Select(l => l.Title + " | " + l.Url)); }
        var photo = await profiles.LoadPhotoAsync(ProfileName, token);
        ProfilePhoto?.Dispose(); ProfilePhoto = photo is null ? null : new Bitmap(photo);
    });
    [RelayCommand]
    private async Task SaveSharedProfileAsync() => await GuardAsync(async token =>
    {
        var links = ProfileLinksText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var pair = line.Split('|', 2, StringSplitOptions.TrimEntries);
            return new ProfileLink(pair.Length > 1 ? pair[0] : "Link", pair[^1]);
        }).ToList();
        await profiles.SaveAsync(new(ProfileName, ProfileMessage, DateTimeOffset.UtcNow.ToString("O"), links), token);
        Status = "Profile and links saved to Box.";
    });
    partial void OnProfileNameChanged(string value) => BuildContributions();
    partial void OnContributionYearChanged(int value) => BuildContributions();
    private void BuildContributions()
    {
        if (ContributionYear is < 1970 or > 9998) return;
        ContributionDays.Clear();
        var days = ContributionService.Calendar(allTools, ProfileName, ContributionYear);
        foreach (var day in days)
            ContributionDays.Add(new($"{day.Date:yyyy-MM-dd}: {day.Count} events\n" + string.Join('\n', day.Events.Select(e => e.Kind + " " + e.Tool + "/" + e.File)), day.Count switch { 0 => "#E4EAF0", 1 => "#9ECEFF", < 4 => "#559BEE", < 8 => "#276CC1", _ => "#124580" }));
        ContributionSummary = $"{ProfileName}: {days.Sum(d => d.Count)} Box upload/edit events in {ContributionYear}. These are catalogue dates, not local launch counts.";
    }
    public Func<Task<string?>>? PhotoPicker { get; set; }
    [RelayCommand]
    private async Task ChangePhotoAsync() => await GuardAsync(async token =>
    {
        if (PhotoPicker is null) return; var path = await PhotoPicker(); if (path is null) return;
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Choose a photo smaller than 8 MiB.");
        using var source = new Bitmap(path);
        if (source.PixelSize.Width > 10000 || source.PixelSize.Height > 10000) throw new InvalidDataException("Photo dimensions exceed 10,000 pixels.");
        using var image = new RenderTargetBitmap(new PixelSize(256, 256));
        var size = Math.Min(source.Size.Width, source.Size.Height);
        using (var drawing = image.CreateDrawingContext()) drawing.DrawImage(source, new Rect((source.Size.Width - size) / 2, (source.Size.Height - size) / 2, size, size), new Rect(0, 0, 256, 256));
        using var bytes = new MemoryStream(); image.Save(bytes, PngBitmapEncoderOptions.Default);
        await profiles.SavePhotoAsync(ProfileName, bytes.ToArray(), token);
        bytes.Position = 0; ProfilePhoto?.Dispose(); ProfilePhoto = new Bitmap(bytes); Status = "Profile photo saved to Box.";
    });
    [RelayCommand] private async Task SaveNewsAsync() => await GuardAsync(async token => { await state.SaveNewsAsync(News, token); Status = "News saved on this device."; });
    [RelayCommand]
    private async Task SaveSettingsAsync() => await GuardAsync(async token =>
    {
        using var validationClient = new HttpClient();
        _ = new BoxManagerClient(validationClient, BoxManagerUrl); // Validate before replacing the active configuration.
        if (!Uri.TryCreate(UpdateFeedUrl, UriKind.Absolute, out var feed) || feed.Scheme != "https") throw new ArgumentException("Update feed must use HTTPS.");
        settings = settings with { BoxManagerUrl = BoxManagerUrl, UpdateFeedUrl = UpdateFeedUrl, Theme = Theme, ProfileName = ProfileName, ProfileMessage = ProfileMessage, JiraUrl = JiraUrl, RefreshMode = RefreshMode, RefreshSelectedFolders = RefreshSelectedFolders.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() };
        await state.SaveSettingsAsync(settings, token); ConfigureClient(); ApplyTheme(); Status = "Settings and local profile saved.";
    });
    partial void OnThemeChanged(string value) => ApplyTheme();
    private void ApplyTheme() { if (Application.Current is { } app) app.RequestedThemeVariant = Theme switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default }; }
    private async Task LoadActivityAsync(CancellationToken token)
    {
        var entries = await activity.LoadAsync(token);
        ProfileActivity = string.Join('\n', entries.GroupBy(e => e.Time.LocalDateTime.Date).OrderByDescending(g => g.Key).Select(g => $"{g.Key:yyyy-MM-dd}  {new string('■', Math.Min(20, g.Count()))}  {g.Count()} actions"));
    }
    [RelayCommand]
    private async Task PreviewLegacyAsync() => await GuardAsync(async token =>
    {
        legacyPreview = await LegacyImporter.PreviewAsync(LegacyRoot, allTools, token);
        LegacySummary = $"Found {legacyPreview.Installed.Count} installed tool versions, {legacyPreview.Favourites.Count} favourites and {legacyPreview.CachedTools} cached catalogue entries. Import links existing tool folders without moving them.";
    });
    [RelayCommand]
    private async Task ImportLegacyAsync() => await GuardAsync(async token =>
    {
        if (legacyPreview is null) throw new InvalidOperationException("Preview the old folder first.");
        await new LegacyImporter(Storage, installations).ImportAsync(legacyPreview, token);
        favourites = await state.FavouritesAsync(token); await RebuildAsync(token); Status = "Legacy installations and favourites imported.";
    });
    [RelayCommand] private async Task CheckUpdatesAsync() => await GuardAsync(async _ => UpdateStatus = await updater.CheckAsync(UpdateFeedUrl));
    [RelayCommand] private async Task DownloadUpdateAsync() => await GuardAsync(async _ => UpdateStatus = await updater.DownloadAsync(p => Progress = p));
    [RelayCommand] private void ApplyUpdate() { try { updater.Apply(); } catch (Exception e) { Log(e.Message); UpdateStatus = e.Message; } }
    [RelayCommand] private void OpenDataFolder() => OpenUri(Storage.Root);
    [RelayCommand] private void OpenJira() { if (Uri.TryCreate(JiraUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") OpenUri(uri.AbsoluteUri); }
    private static void OpenUri(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    private void Log(string text)
    {
        Logs.Insert(0, $"{DateTime.Now:HH:mm:ss} {text}"); while (Logs.Count > 500) Logs.RemoveAt(Logs.Count - 1);
    }
    private async Task GuardAsync(Func<CancellationToken, Task> work)
    {
        if (Busy) return;
        Busy = true; operation.Dispose(); operation = new(); Progress = 0;
        try { await work(operation.Token); }
        catch (OperationCanceledException) { Status = "Cancelled."; }
        catch (Exception e) when (e is not OutOfMemoryException) { Status = e.Message; Log(e.GetType().Name + ": " + e.Message); }
        finally { Busy = false; }
    }
    public void Dispose() { ProfilePhoto?.Dispose(); operation.Cancel(); operation.Dispose(); http.Dispose(); }
}
