using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Install;
using DinoSaurList.Core.Profile;
using DinoSaurList.Core.Registry;
using DinoSaurList.Core.Updates;
using DinoSaurList.ViewModels;
using Xunit;

namespace DinoSaurList.UiTests;

public sealed class FollowUpTests
{
    private sealed class Temp : IDisposable { public string Root { get; } = Path.Combine(Path.GetTempPath(), "dino-followup-" + Guid.NewGuid().ToString("N")); public Temp() => Directory.CreateDirectory(Root); public void Dispose() => Directory.Delete(Root, true); }
    private static ToolEntry Tool(string id = "1", string author = "NPI DEV") => new(id, "Tool-" + author, "Tool " + id, author, "MISC", [new("p", "v1.0-any.zip", "1.0", 1, "", "any")], FlowDocument.Parse("[Platform]\nsupported=any"), []);
    [AvaloniaFact]
    public async Task DashboardAndStoreKeepIndependentSearchText()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Root);
        await storage.WriteAsync("catalogue.json", new CatalogueSnapshot(DateTimeOffset.UtcNow, [Tool()], []), TestContext.Current.CancellationToken);
        using var vm = new MainViewModel(temp.Root, false); await vm.InitializeAsync();
        vm.Search = "installed search"; vm.NavigateCommand.Execute("Store"); Assert.Equal("", vm.Search);
        vm.Search = "store search"; vm.NavigateCommand.Execute("Installed"); Assert.Equal("installed search", vm.Search);
        vm.NavigateCommand.Execute("Store"); Assert.Equal("store search", vm.Search);
    }
    private sealed class Updater : IApplicationUpdater
    {
        public bool RestartReady { get; private set; }
        public bool HasUpdate { get; set; } = true;
        public bool Fail { get; set; }
        public int Applied { get; private set; }
        public Task<string> CheckAsync(string feed) => Task.FromResult("checked");
        public Task<string> PrepareAutomaticUpdateAsync(string feed, Action<int> progress, CancellationToken token = default)
        { if (Fail) throw new IOException("offline"); RestartReady = HasUpdate; progress(100); return Task.FromResult(HasUpdate ? "downloaded" : "latest"); }
        public void Apply() { Assert.True(RestartReady); Applied++; }
    }
    [AvaloniaFact]
    public async Task StartupAppliesDownloadedUpdatesWithoutAUserCommandAndFailsOpenOffline()
    {
        using var temp = new Temp(); var updater = new Updater();
        using var vm = new MainViewModel(temp.Root, false, applicationUpdater: updater);
        Assert.True(await vm.PrepareStartupUpdateAsync()); Assert.Equal(1, updater.Applied);
        using var noUpdate = new MainViewModel(temp.Root, false, applicationUpdater: new Updater { HasUpdate = false });
        Assert.False(await noUpdate.PrepareStartupUpdateAsync());
        var offlineUpdater = new Updater { Fail = true };
        using var offline = new MainViewModel(temp.Root, false, applicationUpdater: offlineUpdater);
        Assert.False(await offline.PrepareStartupUpdateAsync()); Assert.Equal(0, offlineUpdater.Applied); Assert.Contains("offline", offline.UpdateStatus);
    }
    [AvaloniaFact]
    public void LongReadmeHasARealScrollbarAndItsContentStaysOutsideTheGutter()
    {
        var markdown = "# Long README\n\n" + string.Join("\n\n", Enumerable.Repeat("A long paragraph with enough words to wrap across the document viewport. " + new string('a', 120), 60));
        var window = new DocumentWindow("README", markdown) { Width = 600, Height = 420 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
            Assert.Equal(20, scroll.Padding.Right);
            var content = Assert.IsAssignableFrom<Control>(scroll.Content);
            var right = content.TranslatePoint(new Point(content.Bounds.Width, 0), scroll)!.Value.X;
            Assert.True(right <= scroll.Bounds.Width - 19, $"Document content {right} overlaps gutter of {scroll.Bounds.Width}.");
            var output = Environment.GetEnvironmentVariable("DINOSAURLIST_SCREENSHOTS");
            if (output is not null) { Directory.CreateDirectory(output); window.CaptureRenderedFrame()?.Save(Path.Combine(output, "readme-gutter.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        }
        finally { window.Close(); }
    }
    private sealed class PackageBox(byte[] archive) : IBoxClient
    {
        public string? LastDownload { get; private set; }
        public Task<List<BoxItem>> ListAsync(string id, CancellationToken token = default) => Task.FromResult(new List<BoxItem>());
        public Task<string> ReadTextAsync(string id, CancellationToken token = default) => throw new NotSupportedException();
        public async Task DownloadAsync(BoxItem file, string target, IProgress<DownloadProgress>? progress, CancellationToken token) { LastDownload = file.Id; await File.WriteAllBytesAsync(target, archive, token); }
        public Task UploadAsync(string folder, string name, byte[] bytes, CancellationToken token) => throw new NotSupportedException();
    }
    [AvaloniaFact]
    public async Task LatestDownloadsTheChosenArchiveWithoutReplacingLocalFilesAndUpdateInstallsMissingVersions()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Root);
        var execution = OperatingSystem.IsWindows() ? "run.cmd" : "run.sh";
        using var bytes = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Create, true))
        { using var writer = new StreamWriter(zip.CreateEntry(execution).Open()); writer.Write("downloaded executable"); }
        var box = new PackageBox(bytes.ToArray()); var flow = FlowDocument.Parse("[Platform]\nsupported=any\n[Execution]\nfile=" + execution);
        var tool = new ToolEntry("1", "Tool-Author", "Tool", "Author", "MISC", [new("latest", "v2.0-any.zip", "2.0", 1, "", "any"), new("older", "v1.0-any.zip", "1.0", 1, "", "any")], flow, []);
        await storage.WriteAsync("catalogue.json", new CatalogueSnapshot(DateTimeOffset.UtcNow, [tool], []), TestContext.Current.CancellationToken);
        var store = new DinoSaurList.Core.Install.InstallationStore(storage);
        foreach (var version in new[] { "1.0", "2.0" })
        {
            var folder = Path.Combine(storage.Packages, "1", version); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, execution), "preserve local file", TestContext.Current.CancellationToken);
            await store.AddAsync(new("1", "Tool-Author", "Tool", "Author", version, folder, execution, DateTimeOffset.UtcNow, Flow: flow), TestContext.Current.CancellationToken);
        }
        using var vm = new MainViewModel(temp.Root, false, box, installIntegration: new IsolatedInstallIntegration()); await vm.InitializeAsync();
        vm.DownloadFolderRequested = folder => Assert.True(Directory.Exists(folder));
        var card = Assert.Single(vm.Tools); Assert.Equal("Latest", card.PrimaryAction);
        card.SelectedCardPackage = card.Packages.Single(p => p.Version == "1.0"); await card.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal("older", box.LastDownload); Assert.Contains("preserved", vm.Status);
        Assert.Equal("preserve local file", await File.ReadAllTextAsync(Path.Combine(storage.Packages, "1", "1.0", execution), TestContext.Current.CancellationToken));
        card.Packages.Add(new("newest", "v3.0-any.zip", "3.0", 1, "", "any"));
        card.SelectedCardPackage = card.Packages.Last(); Assert.Equal("Update", card.PrimaryAction);
        await card.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal("newest", box.LastDownload); Assert.Equal(3, (await store.LoadAsync(TestContext.Current.CancellationToken)).Count);
        Assert.Equal("Latest", Assert.Single(vm.Tools).PrimaryAction);
    }
    // This regression covers archive/version state. Real Windows shortcuts and tool
    // execution are verified separately by the packaged native integration gate.
    private sealed class IsolatedInstallIntegration : IInstallIntegration
    {
        public Task BeforeInstallAsync(ToolEntry tool, InstalledTool? previous, CancellationToken token) => Task.CompletedTask;
        public Task AfterInstallAsync(ToolEntry tool, string directory, string execution, CancellationToken token) => Task.CompletedTask;
        public Task BeforeRemoveAsync(InstalledTool tool, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class ProfileBox : IBoxClient
    {
        public int Reads { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<List<BoxItem>> ListAsync(string id, CancellationToken token = default) => Task.FromResult(id == CatalogueService.RootFolder
            ? new List<BoxItem> { new("profiles", ".devprofile-data", "folder") } : new List<BoxItem> { new("profile", "NPI_DEV.json", "file") });
        public async Task<string> ReadTextAsync(string id, CancellationToken token = default) { Reads++; Started.TrySetResult(); await Release.Task.WaitAsync(token); return "{\"tweet\":\"fresh shared message\"}"; }
        public Task DownloadAsync(BoxItem file, string target, IProgress<DownloadProgress>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task UploadAsync(string folder, string name, byte[] bytes, CancellationToken token) => throw new InvalidOperationException("Preload must never write to Box.");
    }
    [AvaloniaFact]
    public async Task ProfilePreloadReusesInflightReadShowsCacheAndDoesNotBlockTools()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Root); var box = new ProfileBox();
        await storage.WriteAsync("catalogue.json", new CatalogueSnapshot(DateTimeOffset.UtcNow, [Tool()], []), TestContext.Current.CancellationToken);
        await storage.WriteAsync("profile-NPI_DEV.json", new DeveloperProfile("NPI DEV", "cached message", "", []), TestContext.Current.CancellationToken);
        using var vm = new MainViewModel(temp.Root, true, box);
        await vm.InitializeAsync(); var preload = vm.PreloadDeveloperProfilesAsync();
        await box.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var cachedShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(vm.ProfileMessage) && vm.ProfileMessage == "cached message") cachedShown.TrySetResult(); };
        vm.NavigateCommand.Execute("Developers"); Dispatcher.UIThread.RunJobs();
        await cachedShown.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(vm.Busy); Assert.Equal("cached message", vm.ProfileMessage); Assert.Equal(1, box.Reads);
        box.Release.SetResult(); await preload; await vm.LoadSharedProfileCommand.ExecuteAsync(null);
        // Explicit refresh is allowed; plain navigation after warmup must reuse it.
        var reads = box.Reads; vm.NavigateCommand.Execute("Store"); vm.NavigateCommand.Execute("Developers"); Dispatcher.UIThread.RunJobs();
        Assert.Equal(reads, box.Reads); Assert.Equal("fresh shared message", vm.ProfileMessage);
    }
}
