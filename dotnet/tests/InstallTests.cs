using System.IO.Compression;
using DinoSaurList.Core.Box;
using DinoSaurList.Core.Install;
using DinoSaurList.Core.Launch;
using DinoSaurList.Core.Registry;
using Xunit;

namespace DinoSaurList.Tests;

public sealed class InstallTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    [Fact(SkipWhen = nameof(IsWindows), Skip = "Unix symbolic-link contract; Windows may require privileged symlink creation.")]
    public void LinkedRootAndLinkedRemovalTargetCannotEscapeOwnedTree()
    {
        using var temp = new Temp();
        var outside = Path.Combine(temp.Path, "outside"); Directory.CreateDirectory(outside);
        var linked = Path.Combine(temp.Path, "linked"); Directory.CreateSymbolicLink(linked, outside);
        Assert.Throws<InvalidDataException>(() => SafePaths.Within(linked, "file.txt"));
        Assert.Throws<InvalidDataException>(() => SafePaths.Within(temp.Path, "linked"));
    }
    private static string Zip(string root, string name)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create); var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open()); writer.Write("example"); return path;
    }
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("a/../../outside.txt")]
    public async Task ArchiveTraversalCannotWriteOutsideStaging(string name)
    {
        using var temp = new Temp(); var zip = Zip(temp.Path, name);
        await Assert.ThrowsAsync<InvalidDataException>(() => InstallService.ExtractAsync(zip, Path.Combine(temp.Path, "stage"), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(temp.Path, "outside.txt")));
    }
    [Fact]
    public async Task ArchiveSymbolicLinksAreRejected()
    {
        using var temp = new Temp(); var zip = Path.Combine(temp.Path, "link.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { var entry = archive.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)(0xa1ffu << 16)); }
        await Assert.ThrowsAsync<InvalidDataException>(() => InstallService.ExtractAsync(zip, Path.Combine(temp.Path, "stage"), TestContext.Current.CancellationToken));
    }
    private sealed class Box(string source) : IBoxClient
    {
        public Task<List<BoxItem>> ListAsync(string folderId, CancellationToken token = default) => throw new NotImplementedException();
        public Task<string> ReadTextAsync(string fileId, CancellationToken token = default) => throw new NotImplementedException();
        public Task DownloadAsync(BoxItem file, string destination, IProgress<DownloadProgress>? progress, CancellationToken token) { File.Copy(source, destination); return Task.CompletedTask; }
    }
    private sealed class Launcher : IToolLauncher
    {
        public bool Fail { get; set; }
        public string? Root { get; private set; }
        public Task RunAsync(string root, string relative, bool wait, CancellationToken token) { Root = root; return Fail ? Task.FromException(new IOException("installer failed")) : Task.CompletedTask; }
    }
    [Fact]
    public async Task FailedInstallerPreservesOldStateAndDoesNotStrandTheLock()
    {
        using var temp = new Temp(); var storage = new AppStorage(Path.Combine(temp.Path, "user")); var store = new InstallationStore(storage);
        var zip = Zip(temp.Path, "run.sh"); var launcher = new Launcher { Fail = true };
        var flow = FlowDocument.Parse("[Platform]\nsupported=any\n[Execution]\nfile=run.sh\n[Installation]\nauto=true\nfile=run.sh");
        var package = new ToolPackage("2", "v1.0.0.0-any.zip", "1.0.0.0", null, new string('a', 40), "any");
        var tool = new ToolEntry("1", "Test-Author", "Test", "Author", "MISC", [package], flow, []);
        var service = new InstallService(new Box(zip), storage, store, launcher);
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(tool, package, null, TestContext.Current.CancellationToken)); Assert.Empty(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(storage.Packages, "1"), ".stage-*"));
        launcher.Fail = false; var record = await service.InstallAsync(tool, package, null, TestContext.Current.CancellationToken);
        Assert.Equal(record.Directory, launcher.Root); Assert.True(File.Exists(Path.Combine(record.Directory, "run.sh"))); Assert.Single(await store.LoadAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public void LauncherUsesArgumentsWithoutShellConcatenationAndRejectsWindowsTargetsOnMac()
    {
        using var temp = new Temp(); File.WriteAllText(Path.Combine(temp.Path, "a ; literal.sh"), "");
        var info = ToolLauncher.StartInfo(temp.Path, "a ; literal.sh", "macos", false);
        Assert.Equal("/bin/bash", info.FileName); Assert.Equal(Path.Combine(temp.Path, "a ; literal.sh"), Assert.Single(info.ArgumentList)); Assert.False(info.UseShellExecute);
        File.WriteAllText(Path.Combine(temp.Path, "tool.exe"), "");
        Assert.Throws<PlatformNotSupportedException>(() => ToolLauncher.StartInfo(temp.Path, "tool.exe", "macos", false));
        Assert.Throws<InvalidDataException>(() => ToolLauncher.StartInfo(temp.Path, "../tool.exe", "windows", false));
    }
    [Fact]
    public async Task LegacyImportLinksExistingToolsWithoutMovingOrChangingThem()
    {
        using var temp = new Temp(); var root = Path.Combine(temp.Path, "legacy"); var folder = Path.Combine(root, "Software_Downloaded", "Tool_V-1.2.3.4_A-Author");
        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "run.cmd"), "old application"); Directory.CreateDirectory(Path.Combine(root, "config-record"));
        File.WriteAllText(Path.Combine(root, "config-record", "favourites.json"), "{\"favourites\":[\"Tool-Author\"]}");
        var flow = FlowDocument.Parse("[Execution]\nfile=run.cmd");
        var tool = new ToolEntry("1", "Tool-Author", "Tool", "Author", "MISC", [], flow, []);
        var preview = await LegacyImporter.PreviewAsync(root, [tool], TestContext.Current.CancellationToken); Assert.Single(preview.Installed);
        var storage = new AppStorage(Path.Combine(temp.Path, "new")); var store = new InstallationStore(storage);
        var importer = new LegacyImporter(storage, store); await importer.ImportAsync(preview, TestContext.Current.CancellationToken); await importer.ImportAsync(preview, TestContext.Current.CancellationToken);
        var imported = Assert.Single(await store.LoadAsync(TestContext.Current.CancellationToken)); Assert.True(imported.Imported); Assert.Equal(folder, imported.Directory);
        Assert.Equal("old application", File.ReadAllText(Path.Combine(folder, "run.cmd")));
        Assert.Contains("Tool-Author", (await storage.ReadAsync<HashSet<string>>("favourites.json", TestContext.Current.CancellationToken))!);
    }
}
