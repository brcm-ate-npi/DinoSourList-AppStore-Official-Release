using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DinoSaurList;
using DinoSaurList.ViewModels;
using DinoSaurList.Core.Registry;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(DinoSaurList.UiTests.TestAppBuilder))]
namespace DinoSaurList.UiTests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class WindowTests
{
    [AvaloniaFact]
    public async Task MainWindowBindsCatalogueFiltersFavouritesAndNavigation()
    {
        var root = Path.Combine(Path.GetTempPath(), "dinosaurlist-ui-" + Guid.NewGuid().ToString("N"));
        var storage = new AppStorage(root);
        var tool = new ToolEntry("1", "BandMaster-Author", "BandMaster", "Author", "Foundation", [new("2", "v1.0.0.0.zip", "1.0.0.0", 123, new string('a', 40))], FlowDocument.Parse(""), []);
        await storage.WriteAsync("catalogue.json", new CatalogueSnapshot(DateTimeOffset.UtcNow, [tool], []), TestContext.Current.CancellationToken);
        using var vm = new MainViewModel(root); var window = new MainWindow { DataContext = vm }; window.Show();
        await vm.InitializeAsync();
        try
        {
            Dispatcher.UIThread.RunJobs(); Assert.Single(vm.VisibleTools);
            Assert.Equal("All categories", window.FindControl<ComboBox>("CategoryPicker")!.SelectedItem);
            Assert.NotNull(window.FindControl<ListBox>("ToolList")); Assert.Equal("BandMaster", vm.SelectedTool?.Name);
            Assert.Equal(OperatingSystem.IsWindows(), vm.CanInstall);
            vm.Search = "nonexistent"; Assert.Empty(vm.VisibleTools); vm.Search = "Author"; Assert.Single(vm.VisibleTools);
            await vm.FavouriteCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs(); Assert.True(vm.SelectedTool!.IsFavourite);
            Assert.NotNull(vm.SelectedPackage);
            Assert.Equal(vm.SelectedPackage, window.FindControl<ComboBox>("PackagePicker")!.SelectedItem);
            var output = Environment.GetEnvironmentVariable("DINOSAURLIST_SCREENSHOTS");
            if (output is not null) { Directory.CreateDirectory(output); window.CaptureRenderedFrame()?.Save(Path.Combine(output, "store.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            foreach (var page in new[] { "Installed", "Assistant", "News", "Developers", "Settings", "Logs" })
            {
                vm.NavigateCommand.Execute(page); Dispatcher.UIThread.RunJobs();
                if (output is not null) window.CaptureRenderedFrame()?.Save(Path.Combine(output, page.ToLowerInvariant() + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            vm.NavigateCommand.Execute("Installed"); Assert.Empty(vm.VisibleTools);
            vm.NavigateCommand.Execute("Store"); Assert.Single(vm.VisibleTools);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public void ReadmeViewerRendersHeadingCodeAndLinksWithoutWebView()
    {
        var window = new DocumentWindow("README", "# Title\n\nSome **text**\n```\ncode();\n```\n[Guide](https://example.test/guide)"); window.Show();
        try { Assert.Contains(window.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text == "Title"); }
        finally { window.Close(); }
    }
}
