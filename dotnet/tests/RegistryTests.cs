using System.Text.Json;
using DinoSaurList.Core.Registry;
using DinoSaurList.Core.Assistant;
using Xunit;

namespace DinoSaurList.Tests;

public sealed class RegistryTests
{
    [Theory]
    [InlineData("1.2", "1.2.0.0", 0)]
    [InlineData("1.10.0.0", "1.9.99.99", 1)]
    [InlineData("v2.0.0.0", "10.0.0.0", -1)]
    public void VersionComparisonIsNumeric(string a, string b, int expected) => Assert.Equal(expected, Math.Sign(ToolVersion.Compare(a, b)));
    [Fact]
    public void FlowParserHandlesRealCaseWhitespaceAndEquals()
    {
        var flow = FlowDocument.Parse("\ufeff[Author]\r\nNAME = Test User\r\n[Execution]\nfile=folder/app=1.exe\n[INSTALLATION]\nAuto=TRUE\n[Platform]\nSupported=win,mac,any");
        Assert.Equal("Test User", flow.Get("author", "name")); Assert.Equal("folder/app=1.exe", flow.Get("EXECUTION", "FILE"));
        Assert.True(flow.Flag("installation", "auto")); Assert.Equal(new[] { "windows", "macos", "any" }, flow.Platforms);
        Assert.Equal(new[] { "windows" }, FlowDocument.Parse("").Platforms);
    }
    [Fact]
    public void AllTwentyEightLiveFlowSamplesProduceSameLatestVersionsAndMetadata()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flows.json")));
        Assert.Equal(28, doc.RootElement.GetArrayLength());
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var files = row.GetProperty("files").EnumerateArray().Select(f => new BoxItem(f[0].GetString()!, f[1].GetString()!, "file")).ToList();
            var flow = FlowDocument.Parse(row.GetProperty("flow").GetString()!);
            var tool = CatalogueService.BuildEntry(new(row.GetProperty("id").GetString()!, row.GetProperty("name").GetString()!, "folder"), files, flow);
            Assert.Equal(flow.Get("author", "name"), tool.Author);
            Assert.Equal(files.Count(f => ToolVersion.PackagePattern().IsMatch(f.Name)), tool.Packages.Count);
            Assert.False(tool.Supports("macos")); Assert.True(tool.Supports("windows"));
            if (tool.Packages.Count > 1) Assert.True(ToolVersion.Compare(tool.Packages[0].Version, tool.Packages[^1].Version) >= 0);
        }
    }
    [Fact]
    public void NativeContractsMatchCapturedLegacyPythonResults()
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-contract.json")));
        foreach (var comparison in baseline.RootElement.GetProperty("versionComparisons").EnumerateArray())
            Assert.Equal(comparison.GetProperty("result").GetInt32(), Math.Sign(ToolVersion.Compare(comparison.GetProperty("a").GetString(), comparison.GetProperty("b").GetString())));
        foreach (var folder in baseline.RootElement.GetProperty("installedFolders").EnumerateArray())
        {
            var match = ToolVersion.InstalledPattern().Match(folder.GetProperty("raw_name").GetString()!);
            Assert.True(match.Success);
            Assert.Equal(folder.GetProperty("name").GetString(), match.Groups[1].Value);
            Assert.Equal(folder.GetProperty("version").GetString(), match.Groups[2].Value);
            Assert.Equal(folder.GetProperty("author").GetString(), match.Groups[3].Value);
        }
        using var samples = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flows.json")));
        foreach (var expected in baseline.RootElement.GetProperty("flows").EnumerateArray())
        {
            var sample = samples.RootElement.EnumerateArray().Single(s => s.GetProperty("id").GetString() == expected.GetProperty("id").GetString());
            var flow = FlowDocument.Parse(sample.GetProperty("flow").GetString()!);
            Assert.Equal(expected.GetProperty("author").GetString(), flow.Get("author", "name"));
            Assert.Equal(expected.GetProperty("category").GetString(), flow.Get("category", "cluster", "MISC"));
            Assert.Equal(expected.GetProperty("execution").GetString(), flow.Get("execution", "file"));
            Assert.Equal(expected.GetProperty("autoInstall").GetBoolean(), flow.Flag("installation", "auto"));
            Assert.Equal(expected.GetProperty("autoDelete").GetBoolean(), flow.Flag("delete", "auto"));
        }
    }
    [Fact]
    public void NativeEnglishNormalizationMatchesLegacyNltkCorpus()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "normalization-contract.json")));
        var normalizer = new EnglishNormalizer();
        foreach (var row in doc.RootElement.EnumerateArray())
            Assert.Equal(row.GetProperty("tokens").EnumerateArray().Select(t => t.GetString()), normalizer.Preprocess(row.GetProperty("text").GetString()!));
    }
    [Fact]
    public void AssistantChatMatchesLegacyConversationTemplatesAndFollowups()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "chat-contract.json")));
        var assistant = new ToolAssistant();
        var input = doc.RootElement.GetProperty("readmes").EnumerateArray().Select((r, i) =>
        {
            var readme = r.GetString()!;
            var name = ToolAssistant.Metadata(readme, "TOOL", "Unknown"); var author = ToolAssistant.Metadata(readme, "AUTHOR", "Unknown");
            return (new ToolEntry((i + 1).ToString(), name + "-" + author, name, author, "MISC", [], FlowDocument.Parse(""), []), readme);
        });
        assistant.Index(input); var chat = new AssistantChat(assistant);
        foreach (var row in doc.RootElement.GetProperty("conversation").EnumerateArray())
            Assert.Equal(row.GetProperty("response").GetString(), chat.Reply(row.GetProperty("query").GetString()!).Text);
    }
    [Fact]
    public void DirectToolNameCannotBeShadowedByItsShorterPrefix()
    {
        var first = new ToolEntry("1", "Analyzer-A", "Analyzer", "A", "MISC", [], FlowDocument.Parse(""), []);
        var longer = first with { Id = "2", Name = "AnalyzerExtended" };
        var assistant = new ToolAssistant(); assistant.Index([(first, "TOOL: Analyzer"), (longer, "TOOL: AnalyzerExtended")]);
        Assert.Equal("2", Assert.Single(assistant.Recommend("explain AnalyzerExtended")).Tool.Id);
    }
    [Fact]
    public void AssistantFindsRelevantToolAndDoesNotInventAnswers()
    {
        var flow = FlowDocument.Parse("");
        var regex = new ToolEntry("1", "Regex-A", "Regex", "A", "Dev Tool", [], flow, []);
        var sparam = new ToolEntry("2", "Spara-B", "Spara", "B", "RF", [], flow, []);
        var assistant = new ToolAssistant(); assistant.Index([(regex, "Group text by regular expressions and patterns"), (sparam, "Convert RF S-parameter measurement files")]);
        Assert.Equal("2", assistant.Recommend("convert RF measurement files")[0].Tool.Id);
        Assert.Empty(assistant.Recommend("zzzzunknown"));
    }
    [Fact]
    public async Task InvalidSavedJsonIsPreservedRatherThanOverwritten()
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path);
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "settings.json"), "{bad", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadAsync<UserSettings>("settings.json", TestContext.Current.CancellationToken));
        Assert.Equal("{bad", await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "settings.json"), TestContext.Current.CancellationToken));
    }
}
public sealed class Temp : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dinosaurlist-test-" + Guid.NewGuid().ToString("N"));
    public Temp() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}
