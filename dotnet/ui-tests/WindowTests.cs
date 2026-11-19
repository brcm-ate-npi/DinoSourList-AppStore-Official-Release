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
        using var vm = new MainViewModel(root, autoLoadProfiles: false); var window = new MainWindow { DataContext = vm }; window.Show();
        await vm.InitializeAsync(); vm.NavigateCommand.Execute("Store");
        try
        {
            Dispatcher.UIThread.RunJobs(); Assert.Single(vm.VisibleTools);
            Assert.Equal("All categories", window.FindControl<ComboBox>("CategoryPicker")!.SelectedItem);
            Assert.NotNull(window.FindControl<ItemsControl>("ToolList")); Assert.Equal("BandMaster", vm.SelectedTool?.Name);
            Assert.True(vm.CanInstall); Assert.True(vm.Tools[0].CanUse); // Windows archives remain downloadable on Mac/Linux.
            Assert.Contains("5.0.0.0", vm.News);
            vm.NavigateCommand.Execute("Installed"); Dispatcher.UIThread.RunJobs();
            Assert.Null(window.FindControl<Button>("DashboardUpdateButton"));
            Assert.False(window.FindControl<ScrollViewer>("SettingsScroll")!.IsVisible);
            Assert.NotNull(window.FindControl<TextBlock>("LocalIpLabel"));
            window.Height = 600; Dispatcher.UIThread.RunJobs();
            var navigation = window.FindControl<ScrollViewer>("SidebarNavigation")!;
            var footer = window.FindControl<StackPanel>("SidebarFooter")!;
            Assert.True(navigation.Bounds.Bottom <= footer.Bounds.Y + 1, "Navigation must scroll above the profile footer on small screens.");
            window.Height = 820; Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Border>("UserInfoSeparator")!.IsEffectivelyVisible);
            var labelX = new[] { "DashboardNavLabel", "StoreNavLabel", "ProfileNavLabel", "FavouritesNavLabel", "LinksNavLabel", "NewsNavLabel" }
                .Select(name => window.FindControl<TextBlock>(name)!.TranslatePoint(default, window)!.Value.X).ToArray();
            Assert.All(labelX, x => Assert.Equal(labelX[0], x, 1));
            var expandedWidth = window.FindControl<Border>("Sidebar")!.Bounds.Width;
            vm.ToggleSidebarCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Border>("Sidebar")!.Bounds.Width < expandedWidth);
            Assert.False(window.FindControl<TextBlock>("DashboardNavLabel")!.IsEffectivelyVisible);
            window.FindControl<Button>("StoreNav")!.Command!.Execute("Store"); Assert.Equal("Store", vm.Page);
            vm.ToggleSidebarCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            vm.Search = "nonexistent"; Assert.Empty(vm.VisibleTools); vm.Search = "Author"; Assert.Single(vm.VisibleTools);
            await vm.FavouriteCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs(); Assert.True(vm.SelectedTool!.IsFavourite);
            Assert.NotNull(vm.SelectedPackage);
            Assert.Equal(vm.SelectedPackage, window.FindControl<ComboBox>("PackagePicker")!.SelectedItem);
            vm.DetailsVisible = false; vm.Search = ""; Dispatcher.UIThread.RunJobs();
            var output = Environment.GetEnvironmentVariable("DINOSAURLIST_SCREENSHOTS");
            if (output is not null) { Directory.CreateDirectory(output); window.CaptureRenderedFrame()?.Save(Path.Combine(output, "store.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            foreach (var page in new[] { "Installed", "Assistant", "News", "Developers", "Settings", "Logs" })
            {
                vm.NavigateCommand.Execute(page); Dispatcher.UIThread.RunJobs();
                if (page == "Settings") Assert.True(window.FindControl<Button>("BrowsePythonInstallation")!.IsEffectivelyVisible);
                if (output is not null) window.CaptureRenderedFrame()?.Save(Path.Combine(output, page.ToLowerInvariant() + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            window.FindControl<Button>("AssistantButton")!.Command!.Execute(null); Dispatcher.UIThread.RunJobs();
            var assistantWindow = window.OwnedWindows.OfType<AssistantWindow>().Single();
            Assert.True(assistantWindow.IsVisible);
            vm.Query = "hello"; await vm.RecommendCommand.ExecuteAsync(null);
            Assert.Contains("You: hello", vm.AssistantConversation); Assert.Contains("Welcome", vm.AssistantConversation);
            assistantWindow.Close();
            vm.NavigateCommand.Execute("Installed"); Assert.Empty(vm.VisibleTools);
            vm.NavigateCommand.Execute("Store"); Assert.Single(vm.VisibleTools);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task DashboardIconOpensTheSelectedInstalledVersionThroughCore()
    {
        var root = Path.Combine(Path.GetTempPath(), "dinosaurlist-card-" + Guid.NewGuid().ToString("N"));
        var storage = new AppStorage(root); var directory = Path.Combine(storage.Packages, "1", "1.0.0"); Directory.CreateDirectory(directory);
        var execution = OperatingSystem.IsWindows() ? "run.cmd" : "run.sh";
        await File.WriteAllTextAsync(Path.Combine(directory, execution), OperatingSystem.IsWindows() ? "@echo off\r\n> \"%~dp0launched.txt\" echo launched\r\n" : "#!/bin/sh\nprintf launched > \"$(dirname \"$0\")/launched.txt\"\n", TestContext.Current.CancellationToken);
        var flow = FlowDocument.Parse("[Platform]\nsupported=any\n[Execution]\nfile=" + execution);
        var tool = new ToolEntry("1", "Tool-Author", "Tool", "Author", "Foundation", [new("3", "v2.0.0-any.zip", "2.0.0", 1, new string('a', 40), "any"), new("2", "v1.0.0-any.zip", "1.0.0", 1, new string('b', 40), "any")], flow, []);
        await storage.WriteAsync("catalogue.json", new CatalogueSnapshot(DateTimeOffset.UtcNow, [tool], []), TestContext.Current.CancellationToken);
        await new DinoSaurList.Core.Install.InstallationStore(storage).AddAsync(new("1", "Tool-Author", "Tool", "Author", "1.0.0", directory, execution, DateTimeOffset.UtcNow, Flow: flow), TestContext.Current.CancellationToken);
        using var vm = new MainViewModel(root, autoLoadProfiles: false); var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            await vm.InitializeAsync(); vm.NavigateCommand.Execute("Installed"); Dispatcher.UIThread.RunJobs();
            var card = Assert.Single(vm.Tools); Assert.Equal("Update", card.PrimaryAction); Assert.Equal(2, card.Packages.Count);
            card.SelectedCardPackage = tool.Packages[1]; Assert.Equal("Update", card.PrimaryAction);
            var icon = window.GetVisualDescendants().OfType<Button>().First(b => b.Command == card.IconCommand);
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)icon.Command!).ExecuteAsync(null);
            var marker = Path.Combine(directory, "launched.txt");
            for (var attempt = 0; attempt < 30 && !File.Exists(marker); attempt++) await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(marker), vm.Status); Assert.Equal("1.0.0", vm.SelectedInstallation?.Version);
            vm.ListView = true; Dispatcher.UIThread.RunJobs(); Assert.True(card.IsListView); Assert.Equal(124, card.CardHeight);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public void UpdateIgnoresPinnedOldVersionWhenLatestOrNewerIsAlreadyInstalled()
    {
        var flow = FlowDocument.Parse("[Platform]\nsupported=any");
        var tool = new ToolEntry("1", "Tool-Author", "Tool", "Author", "MISC",
            [new("future", "v99.0.0-foreign.zip", "99.0.0", 1, "", "unsupported"), new("latest", "v2.0.0-any.zip", "2.0.0", 1, "", "any")], flow, []);
        InstalledTool Local(string version) => new("1", "Tool-Author", "Tool", "Author", version, Path.GetTempPath(), "run", DateTimeOffset.UtcNow);
        var olderOnly = new ToolCardViewModel(tool, [Local("1.0.0")], false, "1.0.0");
        Assert.True(olderOnly.HasUpdate); Assert.Equal("Update available", olderOnly.UpdateStatus);
        foreach (var installed in new[] { "2.0.0", "2.0.0.0", "3.0.0" })
        {
            var card = new ToolCardViewModel(tool, [Local("1.0.0"), Local(installed)], false, "1.0.0");
            Assert.Equal("1.0.0", card.InstalledVersion);  // keep the user's rollback preference
            Assert.False(card.HasUpdate); Assert.Equal("Installed", card.UpdateStatus); Assert.Equal("Latest", card.PrimaryAction);
        }
    }

    [AvaloniaFact]
    public async Task StoreSuppressesDetailsAndScrollContentReservesAGutterAtSmallWindowSizes()
    {
        var root = Path.Combine(Path.GetTempPath(), "dinosaurlist-gutter-" + Guid.NewGuid().ToString("N"));
        var storage = new AppStorage(root);
        var tools = Enumerable.Range(1, 24).Select(i => new ToolEntry(i.ToString(), "Tool-Author" + i, "Tool " + i, "Author", "Foundation", [new("p" + i, "v1.0-any.zip", "1.0", 1, "", "any")], FlowDocument.Parse("[Platform]\nsupported=any"), new List<BoxItem>())).ToList();
        await storage.WriteAsync("catalogue.json", new CatalogueSnapshot(DateTimeOffset.UtcNow, tools, []), TestContext.Current.CancellationToken);
        using var vm = new MainViewModel(root, autoLoadProfiles: false);
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 600 }; window.Show();
        try
        {
            await vm.InitializeAsync(); vm.NavigateCommand.Execute("Store");
            vm.SelectCard(vm.Tools[0], true); Assert.False(vm.DetailsVisible);
            foreach (var list in new[] { false, true })
            foreach (var collapsed in new[] { false, true })
            {
                vm.ListView = list; vm.SidebarCollapsed = collapsed; Dispatcher.UIThread.RunJobs();
                var scroll = window.FindControl<ScrollViewer>("ToolsScroll")!;
                var content = window.FindControl<ItemsControl>("ToolList")!;
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "Fixture must have an actual vertical scrollbar.");
                var right = content.TranslatePoint(new Point(content.Bounds.Width, 0), scroll)!.Value.X;
                Assert.True(right <= scroll.Bounds.Width - 17, $"Content {right} intrudes on scrollbar in {scroll.Bounds.Width} width.");
                Assert.Equal(0, scroll.ScrollBarMaximum.X);
            }
            vm.NavigateCommand.Execute("Installed"); vm.SelectCard(vm.Tools[0], true); Assert.True(vm.DetailsVisible);
            vm.NavigateCommand.Execute("Store"); Assert.False(vm.DetailsVisible);
            var output = Environment.GetEnvironmentVariable("DINOSAURLIST_SCREENSHOTS");
            if (output is not null) { Directory.CreateDirectory(output); window.CaptureRenderedFrame()?.Save(Path.Combine(output, "small-store-gutter.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void ReadmeViewerRendersHeadingCodeAndLinksWithoutWebView()
    {
        var window = new DocumentWindow("README", "# Title\n\nSome **text**\n```\ncode();\n```\n[Guide](https://example.test/guide)"); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var viewer = Assert.IsType<global::Markdown.Avalonia.Full.MarkdownScrollViewer>(window.Content);
            Assert.Contains(viewer.GetVisualDescendants(), c => c.Bounds.Width > 0 && c.Bounds.Height > 0);
            Assert.True(viewer.Plugins.HyperlinkCommand!.CanExecute("https://example.test/guide"));
            Assert.False(viewer.Plugins.HyperlinkCommand.CanExecute("file:///tmp/tool.exe"));
            var output = Environment.GetEnvironmentVariable("DINOSAURLIST_SCREENSHOTS");
            if (output is not null) { Directory.CreateDirectory(output); window.CaptureRenderedFrame()?.Save(Path.Combine(output, "readme.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        }
        finally { window.Close(); }
    }
}
