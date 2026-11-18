using System.Text.Json;
using DinoSaurList.Cli;
using DinoSaurList.Core.Install;
using DinoSaurList.Core.Launch;
using DinoSaurList.Core.Registry;
using Xunit;

namespace DinoSaurList.Tests;

public sealed class HostParityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static async Task<string> Cli(string root, params string[] command)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var code = await CliApplication.RunAsync([.. command, "--data-dir", root], output, error, Token);
        Assert.Equal("", error.ToString()); Assert.Equal(0, code); return output.ToString();
    }
    [Fact]
    public async Task CliAndUiCoreShareFavouritesSettingsAndNewsWithoutNetwork()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var state = new UserStateService(storage);
        var tool = new ToolEntry("1", "Tool-Author", "Tool", "Author", "MISC", [], FlowDocument.Parse(""), []);
        await storage.WriteAsync("catalogue.json", new CatalogueSnapshot(DateTimeOffset.UtcNow, [tool], []), Token);
        await Cli(temp.Path, "favourites", "--tool", "1", "--set", "true"); Assert.Contains("Tool-Author", await state.FavouritesAsync(Token));
        await state.SetFavouriteAsync(tool.FolderName, false, Token);
        Assert.Empty(JsonSerializer.Deserialize<List<string>>(await Cli(temp.Path, "favourites"))!);
        await state.SaveSettingsAsync(new(Theme: "Dark"), Token);
        Assert.Contains("Dark", await Cli(temp.Path, "settings"));
        var news = Path.Combine(temp.Path, "announcement.md"); await File.WriteAllTextAsync(news, "# Native news", Token);
        await Cli(temp.Path, "news", "--file", news); Assert.Equal("# Native news", await state.NewsAsync("bundled", Token));
        await Cli(temp.Path, "news", "--reset"); Assert.Equal("bundled", await state.NewsAsync("bundled", Token));
    }
    [Fact]
    public async Task CliPinAndRemoveUseSharedStateAndPreserveImportedFiles()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var store = new InstallationStore(storage);
        var legacy = Path.Combine(temp.Path, "legacy"); Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy, "run.cmd"), "legacy");
        var old = new InstalledTool("1", "Tool-A", "Tool", "A", "1.0", legacy, "run.cmd", DateTimeOffset.UtcNow, true);
        await store.AddAsync(old, Token); await store.AddAsync(old with { Version = "2.0" }, Token);
        await Cli(temp.Path, "pin", "--tool", "1", "--version", "1.0");
        Assert.Equal("1.0", (await new InstalledToolService(storage, store, new ToolLauncher()).FindAsync("1", token: Token)).Version);
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.Equal(1, await CliApplication.RunAsync(["remove", "--data-dir", temp.Path, "--tool", "1", "--version", "1.0"], output, error, Token));
        Assert.Equal(2, (await store.LoadAsync(Token)).Count);
        await Cli(temp.Path, "remove", "--tool", "1", "--version", "1.0", "--yes");
        Assert.True(File.Exists(Path.Combine(legacy, "run.cmd"))); Assert.Single(await store.LoadAsync(Token));
        Assert.Equal("2.0", (await new InstalledToolService(storage, store, new ToolLauncher()).FindAsync("1", token: Token)).Version);
    }
    [Fact]
    public async Task RemovalRefusesUnmanagedPathAndRejectedSettingsPreservePrevious()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var store = new InstallationStore(storage);
        var tool = new InstalledTool("1", "Tool-A", "Tool", "A", "1.0", temp.Path, "", DateTimeOffset.UtcNow); await store.AddAsync(tool, Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => new InstalledToolService(storage, store, new ToolLauncher()).RemoveAsync(tool, Token));
        Assert.Single(await store.LoadAsync(Token));
        var state = new UserStateService(storage); await state.SaveSettingsAsync(new(Theme: "Dark"), Token);
        await Assert.ThrowsAsync<ArgumentException>(() => state.SaveSettingsAsync(new(UpdateFeedUrl: "http://insecure.example/"), Token));
        Assert.Equal("Dark", (await state.SettingsAsync(Token)).Theme);
    }
    private sealed class Installer : IToolLauncher
    {
        public bool Fail { get; set; }
        public string? VersionPath { get; private set; }
        public Task RunAsync(string root, string relative, bool wait, CancellationToken token)
        {
            VersionPath = root;
            return Fail ? Task.FromException(new IOException("installer cancelled")) : Task.CompletedTask;
        }
    }
    [Fact]
    public async Task FailedRollbackKeepsPinnedVersionAndSuccessfulRollbackActivatesItsInstaller()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var store = new InstallationStore(storage);
        var oldDirectory = Path.Combine(storage.Packages, "1", "1.0"); Directory.CreateDirectory(oldDirectory); File.WriteAllText(Path.Combine(oldDirectory, "setup.sh"), "installer");
        var old = new InstalledTool("1", "Tool-A", "Tool", "A", "1.0", oldDirectory, "setup.sh", DateTimeOffset.UtcNow, Flow: FlowDocument.Parse("[Installation]\nauto=true\nfile=setup.sh"));
        var current = old with { Version = "2.0" }; await store.AddAsync(old, Token); await store.AddAsync(current, Token);
        var state = new UserStateService(storage); await state.PinAsync(current, Token);
        var launcher = new Installer { Fail = true }; var service = new InstalledToolService(storage, store, launcher);
        await Assert.ThrowsAsync<IOException>(() => service.ActivateAsync(old, Token));
        Assert.Equal("2.0", (await service.FindAsync("1", token: Token)).Version);
        launcher.Fail = false; await service.ActivateAsync(old, Token);
        Assert.Equal(oldDirectory, launcher.VersionPath); Assert.Equal("1.0", (await service.FindAsync("1", token: Token)).Version);
    }
}
