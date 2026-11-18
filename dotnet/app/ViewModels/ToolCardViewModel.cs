using CommunityToolkit.Mvvm.ComponentModel;
using DinoSaurList.Core.Registry;
using DinoSaurList.Core.Install;
using DinoSaurList.Core.Launch;

namespace DinoSaurList.ViewModels;

public sealed partial class ToolCardViewModel(ToolEntry tool, List<InstalledTool> installed, bool favourite, string? activeVersion = null) : ObservableObject
{
    public ToolEntry Tool { get; } = tool;
    public string Id => Tool.Id;
    public string Name => Tool.Name;
    public string Author => Tool.Author;
    public string Category => Tool.Category;
    public string Version => Tool.LatestVersion;
    public string Platforms => Tool.Platforms;
    public string Initial => Name.TrimStart('#').Length > 0 ? Name.TrimStart('#')[..1].ToUpperInvariant() : "D";
    public List<ToolPackage> Packages => Tool.Packages;
    public List<InstalledTool> InstalledVersions { get; } = installed.Where(x => x.ToolId == tool.Id).OrderByDescending(x => x.Version == activeVersion).ThenByDescending(x => x.Version, ToolVersion.Comparer).ToList();
    public InstalledTool? Installed => InstalledVersions.FirstOrDefault();
    public string InstalledVersion => Installed?.Version ?? "—";
    public bool HasInstalled => InstalledVersions.Count > 0;
    public bool CanInstall => Tool.Supports(ToolLauncher.Platform) && Tool.Packages.Any(p => p.Platform == "any" || p.Platform == ToolLauncher.Platform);
    public string Compatibility => CanInstall ? "Compatible" : "" + Platforms + " only";
    public string UpdateStatus => Installed is null ? "Not installed" : ToolVersion.Compare(Version, Installed.Version) > 0 ? "Update available" : "Installed";
    [ObservableProperty] private bool isFavourite = favourite;
}
