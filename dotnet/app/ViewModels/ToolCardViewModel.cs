using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using DinoSaurList.Core.Registry;
using DinoSaurList.Core.Install;
using DinoSaurList.Core.Launch;

namespace DinoSaurList.ViewModels;

public sealed partial class ToolCardViewModel(ToolEntry tool, List<InstalledTool> installed, bool favourite, string? activeVersion = null, MainViewModel? owner = null) : ObservableObject
{
    public MainViewModel? Owner { get; } = owner;
    public Bitmap? Icon { get; set; }
    public bool HasIcon => Icon is not null;
    public double CardWidth => Owner?.ListView == true ? (Owner.StoreWidth - 24) : Math.Min(250, (Owner?.StoreWidth ?? 274) - 24);
    public bool IsListView => Owner?.ListView == true;
    public double CardHeight => IsListView ? 124 : 280;
    public InstalledTool? MatchingInstallation => IsDashboard ? SelectedCardInstallation ?? Installed : InstalledVersions.FirstOrDefault(t => t.Version == SelectedCardPackage?.Version);
    public bool IsDashboard => Owner?.Page == "Installed";
    [ObservableProperty] private InstalledTool? selectedCardInstallation;
    public string PrimaryAction => MatchingInstallation is not null ? "Open" : "Download";
    public bool CanUse => Owner?.Idle == true && (MatchingInstallation is not null || (!IsDashboard && Packages.Count > 0));
    [ObservableProperty] private ToolPackage? selectedCardPackage = tool.Packages.FirstOrDefault();
    partial void OnSelectedCardPackageChanged(ToolPackage? value) => NotifyLayout();
    partial void OnSelectedCardInstallationChanged(InstalledTool? value) => NotifyLayout();
    public void NotifyLayout() { OnPropertyChanged(nameof(IsListView)); OnPropertyChanged(nameof(CardHeight)); OnPropertyChanged(nameof(CardWidth)); OnPropertyChanged(nameof(PrimaryAction)); OnPropertyChanged(nameof(IsDashboard)); OnPropertyChanged(nameof(CanUse)); }
    public ToolPackage? LatestCompatiblePackage => Tool.Packages
        .Where(p => p.Platform == "any" || p.Platform == ToolLauncher.Platform)
        .OrderByDescending(p => p.Version, ToolVersion.Comparer).FirstOrDefault();
    public bool HasUpdate => CanInstall && Installed is not null && LatestCompatiblePackage is { } latest
        && !InstalledVersions.Any(i => ToolVersion.Compare(i.Version, latest.Version) >= 0);
    [RelayCommand] private async Task UpdateAsync()
    {
        if (Owner is null || !HasUpdate) return;
        Owner.SelectCard(this); Owner.SelectedPackage = LatestCompatiblePackage;
        await Owner.InstallCommand.ExecuteAsync(null);
    }
    [RelayCommand] private void Details() => Owner?.SelectCard(this, true);
    [RelayCommand] private async Task ReadmeAsync() { if (Owner is null) return; Owner.SelectCard(this); await Owner.ReadmeCommand.ExecuteAsync(null); }
    [RelayCommand] private async Task GuideAsync() { if (Owner is null) return; Owner.SelectCard(this); await Owner.GuideCommand.ExecuteAsync(null); }
    [RelayCommand] private async Task FavouriteAsync() { if (Owner is null) return; Owner.SelectCard(this); await Owner.FavouriteCommand.ExecuteAsync(null); }
    [RelayCommand] private async Task PrimaryAsync()
    {
        if (Owner is null) return;
        Owner.SelectCard(this); Owner.SelectedPackage = SelectedCardPackage;
        if (MatchingInstallation is { } installed) { Owner.SelectedInstallation = installed; await Owner.LaunchCommand.ExecuteAsync(null); }
        else await Owner.InstallCommand.ExecuteAsync(null);
    }
    public ToolEntry Tool { get; } = tool;
    public string Id => Tool.Id;
    public string Name => Tool.Name;
    public string Author => Tool.Author;
    public string Category => Tool.Category;
    public string Version => Tool.LatestVersion;
    public string Platforms => Tool.Platforms;
    public string FavouriteGlyph => IsFavourite ? "★" : "☆";
    partial void OnIsFavouriteChanged(bool value) => OnPropertyChanged(nameof(FavouriteGlyph));
    public string Initial => Name.TrimStart('#').Length > 0 ? Name.TrimStart('#')[..1].ToUpperInvariant() : "D";
    public List<ToolPackage> Packages => Tool.Packages;
    public List<InstalledTool> InstalledVersions { get; } = installed.Where(x => x.ToolId == tool.Id).OrderByDescending(x => x.Version == activeVersion).ThenByDescending(x => x.Version, ToolVersion.Comparer).ToList();
    public InstalledTool? Installed => InstalledVersions.FirstOrDefault();
    public string InstalledVersion => Installed?.Version ?? "—";
    public bool HasInstalled => InstalledVersions.Count > 0;
    public bool CanInstall => Tool.Supports(ToolLauncher.Platform) && Tool.Packages.Any(p => p.Platform == "any" || p.Platform == ToolLauncher.Platform);
    public string Compatibility => CanInstall ? "Compatible" : "" + Platforms + " only";
    public string UpdateStatus => Installed is null ? "Not installed" : HasUpdate ? "Update available" : "Installed";
    [ObservableProperty] private bool isFavourite = favourite;
}
