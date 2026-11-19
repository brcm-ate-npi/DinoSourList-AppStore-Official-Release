using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

public sealed record ChatMessage(string Role, string Text);
public sealed record ToolSection(string Name, List<ToolCardViewModel> Tools) { public string Title => $"{Name} ({Tools.Count})"; }
public sealed record ContributionCell(string Label, string Colour, DateOnly? Date = null) { public bool HasDate => Date is not null; }

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public AppStorage Storage { get; }
    private readonly HttpClient newsHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private IBoxClient box = null!;
    private readonly IBoxClient? suppliedBox;
    private readonly IInstallIntegration? installIntegration;
    private CatalogueService catalogue = null!;
    private DocumentationService documents = null!;
    private DeveloperProfiles profiles = null!;
    private readonly InstallationStore installations;
    private readonly UserStateService state;
    private readonly InstalledToolService installedTools;
    private readonly ToolLauncher launcher = new();
    private readonly ActivityStore activity;
    private readonly ToolAssistant assistant = new();
    private readonly AssistantChat chat;
    private readonly IApplicationUpdater updater;
    private readonly Dictionary<string, string> readmes = [];
    private Dictionary<string, string> activeVersions = [];
    private List<ToolEntry> allTools = [];
    private List<ContributionEvent> manualContributions = [];
    private HashSet<string> favourites = new(StringComparer.OrdinalIgnoreCase);
    private UserSettings settings = new();
    private CancellationTokenSource operation = new();
    public ObservableCollection<ToolCardViewModel> Tools { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["All categories"];
    public ObservableCollection<string> Authors { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<ContributionCell> ContributionDays { get; } = [];
    public ObservableCollection<Recommendation> Recommendations { get; } = [];
    public ObservableCollection<ChatMessage> AssistantMessages { get; } = [new("Assistant", "Welcome to Dinosaur List. Describe your task or ask me to list all tools.")];
    public string WindowTitle => "DinoSaurList — " + AppVersion;
    public string AppVersion => AppUpdateService.Version;
    public string AppReleaseChannel => AppVersion.Contains('-') ? "Preview" : "Stable";
    public string AppReleaseUrl => AppUpdateService.GithubRepository + "/releases/tag/v" + AppVersion;
    [RelayCommand] private void OpenApplicationRelease() => OpenUri(AppReleaseUrl);
    public string DataDirectory => Storage.Root;
    public string RuntimePlatform => ToolLauncher.Platform;
    [ObservableProperty] private string page = "Installed";
    [ObservableProperty] private bool listView;
    [ObservableProperty] private double windowWidth = 1240;
    [ObservableProperty] private bool sidebarCollapsed;
    public double SidebarWidth => SidebarCollapsed ? 76 : 210;
    public string SidebarToggleGlyph => SidebarCollapsed ? "›" : "‹";
    public string ExitLabel => SidebarCollapsed ? "×" : "Exit";
    [RelayCommand] private void ToggleSidebar() => SidebarCollapsed = !SidebarCollapsed;
    partial void OnSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarWidth)); OnPropertyChanged(nameof(SidebarToggleGlyph)); OnPropertyChanged(nameof(ExitLabel));
        foreach (var tool in Tools) tool.NotifyLayout();
    }
    public double StoreWidth => Math.Max(120, WindowWidth - SidebarWidth - 48 - 18 - (DetailsVisible ? 336 : 0));
    partial void OnWindowWidthChanged(double value) { foreach (var tool in Tools) tool.NotifyLayout(); }
    partial void OnDetailsVisibleChanged(bool value)
    {
        if (value && !IsDashboard) { DetailsVisible = false; return; }
        foreach (var tool in Tools) tool.NotifyLayout();
    }
    [ObservableProperty] private bool detailsVisible;
    [ObservableProperty] private bool editingProfile;
    public string PageTitle => FavouritesOnly ? "Favourites" : Page switch { "Store" => "Software Store", "Installed" => "Local Dashboard", "Developers" => "Dev Profile", "Assistant" => "AI Assistant", "News" => "News Updates", "Links" => "Useful Links", _ => Page };
    public string UserName => Environment.UserName;
    [ObservableProperty] private string localIp = "Resolving local IP…";
    [ObservableProperty] private string assistantConversation = "Assistant: Hello! Describe your task to find the right tool. You can also ask me to list all tools or explain a recommendation.";
    [ObservableProperty] private bool updateBusy;
    [ObservableProperty] private bool restartReady;
    public event Action? AssistantRequested;
    [RelayCommand] private void OpenAssistant() => AssistantRequested?.Invoke();
    public string LocalCount => $"Local Downloads: {Tools.Count(t => t.HasInstalled)}";
    public string StoreCount => $"Software Store: {Tools.Count}";
    public string CategoryCount => $"Software Categories: {Math.Max(0, Categories.Count - 1)}";
    public IEnumerable<ToolSection> ToolSections => VisibleTools.GroupBy(t => t.Category).OrderBy(g => g.Key == "Dev Tool" ? 0 : g.Key == "Foundation" ? 1 : g.Key == "MISC" ? 3 : 2).ThenBy(g => g.Key).Select(g => new ToolSection(g.Key, g.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList()));
    public string ProfileInitial => ProfileName.Length > 0 ? ProfileName[..1].ToUpperInvariant() : "D";
    public int ProfileApplications => allTools.Count(t => t.Author == ProfileName);
    public int ProfileVersions => allTools.Where(t => t.Author == ProfileName).Sum(t => t.Packages.Count);
    public int ProfileUpdates { get; private set; }
    public int ProfileActiveDays { get; private set; }
    public int ProfileStreak { get; private set; }
    [ObservableProperty] private ContributionCell? selectedContributionDay;
    public string ProfileEventFilter => SelectedContributionDay?.Date?.ToString("yyyy-MM-dd") ?? "All days";
    public IEnumerable<ToolCardViewModel> ProfileTools => Tools.Where(t => t.Author.Equals(ProfileName, StringComparison.OrdinalIgnoreCase));
    public IEnumerable<ContributionEvent> ProfileEvents => ContributionService.Calendar(allTools, ProfileName, ContributionYear, manualContributions).SelectMany(d => d.Events).Where(e => SelectedContributionDay?.Date is not { } selected || e.Date == selected).OrderByDescending(e => e.Date).ThenBy(e => e.Tool).Take(100);
    partial void OnSelectedContributionDayChanged(ContributionCell? value) { OnPropertyChanged(nameof(ProfileEvents)); OnPropertyChanged(nameof(ProfileEventFilter)); }
    [RelayCommand] private void SelectContributionDay(ContributionCell? cell) => SelectedContributionDay = cell;
    [RelayCommand] private void OpenProfileTool(ToolCardViewModel? tool) { if (tool is null) return; Navigate("Store"); SelectCard(tool, true); }
    public IEnumerable<ProfileLink> ProfileLinks => ProfileLinksText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('|', 3, StringSplitOptions.TrimEntries)).Where(p => p.Length > 1).Select(p => new ProfileLink(p[0], p[1], p.Length > 2 ? p[2] : ""));
    public void SelectCard(ToolCardViewModel card, bool details = false) { SelectedTool = card; DetailsVisible = details && IsDashboard; }
    partial void OnListViewChanged(bool value) { foreach (var tool in Tools) tool.NotifyLayout(); }
    partial void OnProfileLinksTextChanged(string value) => OnPropertyChanged(nameof(ProfileLinks));
    [RelayCommand] private void CloseDetails() => DetailsVisible = false;
    [RelayCommand] private void EditProfile() => EditingProfile = !EditingProfile;
    [RelayCommand] private void OpenProfileLink(ProfileLink? link) { if (link is not null && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") OpenUri(uri.AbsoluteUri); }
    [RelayCommand] private void Exit() => (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    [ObservableProperty] private string dashboardSearch = "";
    [ObservableProperty] private string storeSearch = "";
    public string Search { get => IsDashboard ? DashboardSearch : StoreSearch; set { if (IsDashboard) DashboardSearch = value; else StoreSearch = value; } }
    [ObservableProperty] private string category = "All categories";
    [ObservableProperty] private bool favouritesOnly;
    [ObservableProperty] private ToolCardViewModel? selectedTool;
    [ObservableProperty] private ToolPackage? selectedPackage;
    [ObservableProperty] private InstalledTool? selectedInstallation;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string status = "Loading saved catalogue…";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string query = "";
    [ObservableProperty] private string assistantStatus = "Ask about a tool or describe your task. I will use the tool descriptions to help you.";
    [ObservableProperty] private string assistantResponse = "";
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
    [ObservableProperty] private string updateStatus = "Application updates are checked automatically on startup and every 15 minutes.";
    public bool CanUpdateApplication => Idle && !UpdateBusy;
    partial void OnUpdateBusyChanged(bool value) => OnPropertyChanged(nameof(CanUpdateApplication));
    private LegacyPreview? legacyPreview;
    public bool IsDashboard => Page == "Installed";
    public Func<Task<string?>>? LegacyFolderPicker { get; set; }
    public Action<string>? DownloadFolderRequested { get; set; }
    [RelayCommand] private async Task BrowseLegacyAsync() { if (LegacyFolderPicker is null) return; var path = await LegacyFolderPicker(); if (path is null) return; LegacyRoot = path; Page = "Settings"; await PreviewLegacyCommand.ExecuteAsync(null); }
    public bool IsStore => Page is "Store" or "Installed";
    public bool IsAssistant => Page == "Assistant";
    public bool IsLinks => Page == "Links";
    public bool IsNews => Page == "News";
    public bool IsProfile => Page == "Developers";
    public bool IsSettings => Page == "Settings";
    public bool IsLogs => Page == "Logs";
    public bool Idle => !Busy;
    public IEnumerable<ToolPackage> AvailablePackages => SelectedTool?.Packages ?? [];
    public IEnumerable<InstalledTool> InstalledVersions => SelectedTool?.InstalledVersions ?? [];
    public bool HasSelection => SelectedTool is not null;
    public bool CanInstall => Idle && SelectedTool is not null && SelectedPackage is not null;
    public bool CanLaunch => Idle && SelectedInstallation is not null;
    public IEnumerable<ToolCardViewModel> VisibleTools => Tools.Where(t => (Page != "Installed" || t.HasInstalled)
        && (!FavouritesOnly || t.IsFavourite) && (Category == "All categories" || Category == t.Category)
        && (string.IsNullOrWhiteSpace(Search) || (t.Name + " " + t.Author + " " + t.Category).Contains(Search, StringComparison.OrdinalIgnoreCase)));
    public string VisibleCount => $"{VisibleTools.Count()} of {Tools.Count} tools";
    private readonly bool autoLoadProfiles;
    public MainViewModel(string? dataRoot = null, bool autoLoadProfiles = true, IBoxClient? boxClient = null, IApplicationUpdater? applicationUpdater = null, IInstallIntegration? installIntegration = null)
    {
        this.installIntegration = installIntegration; this.autoLoadProfiles = autoLoadProfiles; suppliedBox = boxClient; updater = applicationUpdater ?? new AppUpdateService(); chat = new(assistant); Storage = new(dataRoot); installations = new(Storage); state = new(Storage); installedTools = new(Storage, installations, launcher); activity = new(Storage); ConfigureClient();
    }
    private void ConfigureClient()
    {
        http.Dispose(); http = new() { Timeout = TimeSpan.FromMinutes(30) };
        box = suppliedBox ?? new BoxManagerClient(http, settings.BoxManagerUrl);
        profileEpoch++; profileLoads.Clear();
        catalogue = new(box, Storage); documents = new(box, Storage); profiles = new(box, Storage);
    }
    public async Task InitializeAsync() => await GuardAsync(async token =>
    {
        settings = await state.SettingsAsync(token);
        BoxManagerUrl = settings.BoxManagerUrl; UpdateFeedUrl = settings.UpdateFeedUrl; Theme = settings.Theme; RefreshMode = settings.RefreshMode; RefreshSelectedFolders = string.Join(", ", settings.RefreshSelectedFolders ?? []);
        ProfileName = settings.ProfileName; ProfileMessage = settings.ProfileMessage; JiraUrl = settings.JiraUrl;
        ApplyTheme(); ConfigureClient();
        favourites = await state.FavouritesAsync(token);
        News = await state.NewsAsync(await new NewsService(newsHttp, Storage).CachedAsync(token), token);
        var saved = await catalogue.LoadCacheAsync(token);
        if (saved is not null) { allTools = saved.Tools; await RebuildAsync(token); Status = $"Saved catalogue from {saved.FetchedAt.LocalDateTime:g}. Refresh to check Box."; }
        else { var live = await catalogue.RefreshAsync(new Progress<string>(s => Status = s), token); allTools = live.Tools; await RebuildAsync(token); Status = $"Loaded {allTools.Count} tools."; }
        manualContributions = await ContributionService.LoadManualAsync(Storage, token); BuildContributions();
        if (AssistantMessages.Count <= 1)
        {
            AssistantMessages.Clear();
            AssistantMessages.Add(new("Assistant", "Welcome to WSD Dinosaur List!\n\nDescribe your task, ask for a tool by name, or type 'list all tools'.\n\nSome tools you can ask about:\n" + string.Join("\n", allTools.Take(5).Select(t => "• " + t.Name))));
        }
        await LoadActivityAsync(token);
    });
    private async Task RebuildAsync(CancellationToken token)
    {
        activeVersions = await state.ActiveVersionsAsync(token);
        var previousCategory = Category;
        var current = SelectedTool?.Id; var local = await installations.LoadAsync(token);
        var selectedPackages = Tools.ToDictionary(t => t.Id, t => t.SelectedCardPackage?.FileId);
        foreach (var old in Tools) old.Icon?.Dispose();
        Tools.Clear(); Categories.Clear(); Categories.Add("All categories"); Authors.Clear();
        foreach (var tool in allTools)
        {
            var card = new ToolCardViewModel(tool, local, favourites.Contains(tool.FolderName), activeVersions.GetValueOrDefault(tool.Id), this);
            card.SelectedCardPackage = card.Packages.FirstOrDefault(p => p.FileId == selectedPackages.GetValueOrDefault(tool.Id)) ?? card.SelectedCardPackage;
            try { var icon = await documents.IconAsync(tool, token); if (icon is not null) card.Icon = new Bitmap(icon); }
            catch (Exception error) when (error is IOException or HttpRequestException or ArgumentException) { Log($"Icon unavailable: {tool.Name}"); }
            card.SelectedCardInstallation = card.Installed; Tools.Add(card);
        }
        foreach (var value in allTools.Select(t => t.Category).Distinct().Order()) Categories.Add(value);
        foreach (var value in allTools.Select(t => t.Author).Distinct().OrderBy(a => a.Equals("NPI DEV", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(a => a, StringComparer.OrdinalIgnoreCase)) Authors.Add(value);
        Category = Categories.Contains(previousCategory) ? previousCategory : "All categories";
        if (string.IsNullOrWhiteSpace(ProfileName)) ProfileName = Authors.FirstOrDefault() ?? "";
        SelectedTool = Tools.FirstOrDefault(t => t.Id == current) ?? Tools.FirstOrDefault();
        var index = allTools.Select(t => (t, readmes.GetValueOrDefault(t.Id, ""))).ToArray();
        await Task.Run(() => assistant.Index(index), token);
        NotifyTools(); BuildContributions();
    }
    private void NotifyTools()
    {
        var current = SelectedTool;
        OnPropertyChanged(nameof(VisibleTools)); OnPropertyChanged(nameof(VisibleCount)); OnPropertyChanged(nameof(ToolSections));
        foreach (var tool in Tools) tool.NotifyLayout();
        foreach (var name in new[] { nameof(LocalCount), nameof(StoreCount), nameof(CategoryCount), nameof(PageTitle) }) OnPropertyChanged(name);
        SelectedTool = VisibleTools.FirstOrDefault(t => t == current) ?? VisibleTools.FirstOrDefault();
    }
    partial void OnDashboardSearchChanged(string value) { if (IsDashboard) { OnPropertyChanged(nameof(Search)); NotifyTools(); } }
    partial void OnStoreSearchChanged(string value) { if (!IsDashboard) { OnPropertyChanged(nameof(Search)); NotifyTools(); } }
    partial void OnCategoryChanged(string value) => NotifyTools();
    partial void OnFavouritesOnlyChanged(bool value) => NotifyTools();
    partial void OnPageChanged(string value)
    {
        DetailsVisible = false;
        OnPropertyChanged(nameof(Search));
        foreach (var name in new[] { nameof(IsDashboard), nameof(IsStore), nameof(IsAssistant), nameof(IsLinks), nameof(IsNews), nameof(IsProfile), nameof(IsSettings), nameof(IsLogs) }) OnPropertyChanged(name);
        NotifyTools();
    }
    partial void OnBusyChanged(bool value) { foreach (var tool in Tools) tool.NotifyLayout(); OnPropertyChanged(nameof(Idle)); OnPropertyChanged(nameof(CanUpdateApplication)); OnPropertyChanged(nameof(CanInstall)); OnPropertyChanged(nameof(CanLaunch)); }
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
    [RelayCommand] private void Navigate(string value) { FavouritesOnly = false; Page = value; DetailsVisible = false; if (autoLoadProfiles && value == "Developers") ProfileDisplayTask = ShowProfileAsync(false); }
    [RelayCommand] private void ShowFavourites() { FavouritesOnly = true; Page = "Store"; }
    [RelayCommand] private void UsefulLinks() => Page = "Links";
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
        var alreadyInstalled = selected.InstalledVersions.Any(i => ToolVersion.Compare(i.Version, package.Version) == 0);
        if (alreadyInstalled || !selected.Tool.Supports(ToolLauncher.Platform) || (package.Platform != "any" && package.Platform != ToolLauncher.Platform))
        {
            var file = await new PackageDownloadService(box, Storage).DownloadAsync(selected.Tool, package,
                new Progress<DownloadProgress>(p => { Progress = p.Percent; Status = $"Downloading archive: {p.Percent:F0}%"; }), token);
            Status = alreadyInstalled ? $"Downloaded {Path.GetFileName(file)}. The installed copy was preserved." : $"Downloaded {Path.GetFileName(file)}. This package requires {package.Platform}; it cannot run on {ToolLauncher.Platform}.";
            var folder = Path.GetDirectoryName(file)!;
            if (DownloadFolderRequested is { } showFolder) showFolder(folder); else OpenUri(folder);
            return;
        }
        var service = new InstallService(box, Storage, installations, launcher, installIntegration);
        var record = await service.InstallAsync(selected.Tool, package, new Progress<DownloadProgress>(p => { Progress = p.Percent; Status = $"Downloading {p.Bytes / 1048576.0:F1} MiB ({p.Percent:F0}%)"; }), token);
        await activity.RecordAsync(record.ToolId, "install", token); await RebuildAsync(token); await LoadActivityAsync(token); Status = $"Installed {record.Name} {record.Version}.";
    });
    [RelayCommand]
    private async Task LaunchAsync() => await GuardAsync(async token =>
    {
        if (SelectedInstallation is not { } tool) return;
        Status = $"Starting {tool.Name}…";
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
    private async Task LoadAssistantDocumentsAsync(CancellationToken token)
    {
        foreach (var tool in allTools.Where(t => DocumentationService.ReadmeFile(t) is not null))
        {
            Status = $"Indexing {tool.Name}";
            try { readmes[tool.Id] = await documents.ReadmeAsync(tool, token); }
            catch (HttpRequestException) { Log($"README unavailable: {tool.Name}"); }
        }
        var index = allTools.Where(t => readmes.ContainsKey(t.Id)).Select(t => (t, readmes[t.Id])).ToArray();
        await Task.Run(() => assistant.Index(index), token); chat.Reset();
        AssistantStatus = $"Ready — {readmes.Count} tool descriptions loaded.";
    }
    [RelayCommand]
    private async Task IndexDocumentationAsync() => await GuardAsync(LoadAssistantDocumentsAsync);
    [RelayCommand]
    private async Task RecommendAsync() => await GuardAsync(async token =>
    {
        var question = Query.Trim();
        if (question.Length == 0) return;
        Query = ""; AssistantMessages.Add(new("You", question));
        AssistantConversation += "\n\nYou: " + question;
        AssistantStatus = "Reading tool descriptions…";
        try
        {
            if (readmes.Count == 0) await LoadAssistantDocumentsAsync(token);
            var reply = await Task.Run(() => chat.Reply(question), token); AssistantResponse = reply.Text;
            AssistantMessages.Add(new("Assistant", reply.Text));
            AssistantConversation += "\n\nAssistant: " + reply.Text;
            Recommendations.Clear(); foreach (var value in reply.Tools) Recommendations.Add(value);
            AssistantStatus = Recommendations.Count == 0 ? "Ready for your next question." : $"{Recommendations.Count} tools — choose one below to view it in the Store.";
        }
        catch
        {
            AssistantMessages.Add(new("Assistant", "I could not prepare suggestions. Please try again or reload the tool descriptions."));
            AssistantStatus = "Please try again."; throw;
        }
    });
    [RelayCommand]
    private void SelectRecommendation(Recommendation? value)
    {
        if (value is null) return; Navigate("Store"); StoreSearch = value.Tool.Name; Category = "All categories"; SelectedTool = Tools.FirstOrDefault(t => t.Id == value.Tool.Id);
    }
    [RelayCommand]
    private async Task SaveSharedProfileAsync() => await GuardAsync(async token =>
    {
        var links = ProfileLinksText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var pair = line.Split('|', 3, StringSplitOptions.TrimEntries);
            return new ProfileLink(pair.Length > 1 ? pair[0] : "Link", pair.Length > 1 ? pair[1] : pair[0], pair.Length > 2 ? pair[2] : "");
        }).ToList();
        await profiles.SaveAsync(new(ProfileName, ProfileMessage, DateTimeOffset.UtcNow.ToString("O"), links), token);
        profileEpoch++; profileLoads.Clear(); Status = "Profile and links saved to Box.";
    });
    partial void OnProfileNameChanged(string value) { BuildContributions(); if (autoLoadProfiles && IsProfile) ProfileDisplayTask = ShowProfileAsync(false); }
    partial void OnContributionYearChanged(int value) => BuildContributions();
    private void BuildContributions()
    {
        if (ContributionYear is < 1970 or > 9998) return;
        ContributionDays.Clear(); SelectedContributionDay = null;
        var days = ContributionService.Calendar(allTools, ProfileName, ContributionYear, manualContributions);
        for (var padding = 0; padding < (int)new DateOnly(ContributionYear, 1, 1).DayOfWeek; padding++) ContributionDays.Add(new("", "Transparent"));
        foreach (var day in days)
            ContributionDays.Add(new($"{day.Date:yyyy-MM-dd}: {day.Count} events\n" + string.Join('\n', day.Events.Select(e => e.Kind + " " + e.Tool + "/" + e.File)), day.Count switch { 0 => "#E4EAF0", 1 => "#9ECEFF", < 4 => "#559BEE", < 8 => "#276CC1", _ => "#124580" }, day.Date));
        ProfileUpdates = days.Sum(d => d.Count); ProfileActiveDays = days.Count(d => d.Count > 0);
        var streak = 0; ProfileStreak = 0;
        foreach (var day in days) { streak = day.Count > 0 ? streak + 1 : 0; ProfileStreak = Math.Max(ProfileStreak, streak); }
        foreach (var name in new[] { nameof(ProfileInitial), nameof(ProfileApplications), nameof(ProfileVersions), nameof(ProfileUpdates), nameof(ProfileActiveDays), nameof(ProfileStreak), nameof(ProfileTools), nameof(ProfileEvents), nameof(ProfileEventFilter) }) OnPropertyChanged(name);
        ContributionSummary = $"{ProfileName}: {days.Sum(d => d.Count)} Box upload/edit and imported events in {ContributionYear}. These are catalogue dates, not local launch counts.";
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
        bytes.Position = 0; ProfilePhoto?.Dispose(); ProfilePhoto = new Bitmap(bytes); profileEpoch++; profileLoads.Clear(); Status = "Profile photo saved to Box.";
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
        favourites = await state.FavouritesAsync(token); manualContributions = await ContributionService.LoadManualAsync(Storage, token);
        settings = await state.SettingsAsync(token); RefreshMode = settings.RefreshMode; RefreshSelectedFolders = string.Join(", ", settings.RefreshSelectedFolders ?? []);
        News = await state.NewsAsync(await new NewsService(newsHttp, Storage).CachedAsync(token), token);
        await RebuildAsync(token); Status = "Legacy installations and user data imported; original files preserved.";
    });
    private readonly CancellationTokenSource updateCancellation = new();
    public async Task LoadLocalIdentityAsync()
    {
        try
        {
            var address = await Task.Run(() => NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))?.Address.ToString());
            LocalIp = address ?? "Unavailable";
        }
        catch (NetworkInformationException) { LocalIp = "Unavailable"; }
    }
    [RelayCommand] private async Task RefreshNewsAsync()
    {
        try
        {
            var official = await new NewsService(newsHttp, Storage).RefreshAsync(updateCancellation.Token);
            News = await state.NewsAsync(official, updateCancellation.Token);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or JsonException)
        { Log("News refresh unavailable; saved news retained: " + error.Message); }
    }
    [RelayCommand] private async Task ResetNewsAsync()
    {
        state.ResetNews(); News = await new NewsService(newsHttp, Storage).CachedAsync();
        await RefreshNewsAsync();
    }
    public Task PrepareAutomaticUpdateAsync() => RunUpdateAsync(false);
    private bool restartRequested;
    public async Task<bool> PrepareStartupUpdateAsync()
    {
        restartRequested = false;
        await RunUpdateAsync(true);
        return restartRequested;
    }
    [RelayCommand] private Task UpdateApplicationAsync() => RunUpdateAsync(true);
    private async Task RunUpdateAsync(bool restart)
    {
        if (UpdateBusy || (restart && Busy)) return;
        UpdateBusy = true;
        try
        {
            if (!RestartReady)
            {
                UpdateStatus = "Checking published application releases…";
                var saved = await state.SettingsAsync(updateCancellation.Token);
                UpdateStatus = await updater.PrepareAutomaticUpdateAsync(saved.UpdateFeedUrl,
                    p => Dispatcher.UIThread.Post(() => UpdateStatus = $"Downloading application update: {p}%"), updateCancellation.Token);
                RestartReady = updater.RestartReady;
            }
            if (restart && RestartReady) { UpdateStatus = "Applying application update…"; updater.Apply(); restartRequested = true; }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { UpdateStatus = "Update unavailable: " + error.Message; Log(UpdateStatus); }
        finally
        {
            UpdateBusy = false;
            try { await Storage.WriteAsync("update-status.json", new { checkedAt = DateTimeOffset.UtcNow, version = AppVersion, release = AppReleaseUrl, channel = AppReleaseChannel, status = UpdateStatus, restartReady = RestartReady }); }
            catch (IOException error) { Log("Could not save update status: " + error.Message); }
        }
    }
    [RelayCommand] private async Task CheckUpdatesAsync()
    {
        if (UpdateBusy) return;
        UpdateBusy = true;
        try { UpdateStatus = await updater.CheckAsync((await state.SettingsAsync()).UpdateFeedUrl); RestartReady = updater.RestartReady; }
        catch (Exception error) { UpdateStatus = error.Message; Log(error.Message); }
        finally { UpdateBusy = false; }
    }
    [RelayCommand] private async Task DownloadUpdateAsync() => await RunUpdateAsync(false);
    [RelayCommand] private void ApplyUpdate() { if (Busy || UpdateBusy) { UpdateStatus = "Wait for the current operation to finish before restarting."; return; } try { updater.Apply(); } catch (Exception e) { Log(e.Message); UpdateStatus = e.Message; } }
    [RelayCommand] private void OpenDataFolder() => OpenUri(Storage.Root);
    [RelayCommand] private void OpenJira() { if (Uri.TryCreate(JiraUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") OpenUri(uri.AbsoluteUri); }
    private async void OpenUri(string target)
    {
        try { await Task.Run(() => { using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }); }
        catch (Exception error) { Status = error.Message; Log(error.Message); }
    }
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
    public void Dispose() { foreach (var tool in Tools) tool.Icon?.Dispose(); updateCancellation.Cancel(); ProfilePhoto?.Dispose(); operation.Cancel(); operation.Dispose(); http.Dispose(); newsHttp.Dispose(); }
}
