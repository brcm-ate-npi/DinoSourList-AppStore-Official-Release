using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DinoSaurList.Core.Registry;
using DinoSaurList.ViewModels;

namespace DinoSaurList;

// Invoked only by --smoke-test-output; exercises the installed native UI in release CI.
internal static class NativeSmoke
{
    public static string? OutputPrefix { get; set; }
    public static async Task CaptureAsync(Window window, MainViewModel vm)
    {
        var prefix = Path.GetFullPath(OutputPrefix!);
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        vm.NavigateCommand.Execute("Store");
        vm.DetailsVisible = false;
        await Task.Delay(1000);
        if (!window.IsVisible || window.ClientSize.Width < 1 || vm.Tools.Count == 0 || !vm.VisibleTools.Any() || vm.SelectedTool is null || !window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && c.SelectedItem is ToolPackage))
            throw new InvalidOperationException("Native window or cached catalogue failed to initialize: " + vm.Status);
        vm.Query = "Preview"; await vm.RecommendCommand.ExecuteAsync(null);
        if (vm.Recommendations.Count != 1 || !vm.AssistantResponse.Contains("Preview", StringComparison.Ordinal)) throw new InvalidOperationException("Installed assistant/model failed: " + vm.Status);
        await vm.LoadLocalIdentityAsync();
        vm.NavigateCommand.Execute("Installed");
        await Task.Delay(200);
        if (window.FindControl<Button>("DashboardUpdateButton") is not { IsEffectivelyVisible: true }) throw new InvalidOperationException("Dashboard application update action is missing.");
        vm.OpenAssistantCommand.Execute(null);
        await Task.Delay(200);
        var assistantWindow = window.OwnedWindows.OfType<AssistantWindow>().Single();
        if (!assistantWindow.IsVisible || !vm.AssistantConversation.Contains("Preview", StringComparison.Ordinal)) throw new InvalidOperationException("Assistant conversation window is missing.");
        SaveWindow(assistantWindow, prefix + "-assistant.png");
        assistantWindow.Close();
        SaveWindow(window, prefix + "-dashboard.png");
        vm.NavigateCommand.Execute("News"); await Task.Delay(200);
        if (!vm.News.Contains("5.0.0.0", StringComparison.Ordinal)) throw new InvalidOperationException("Python news version/content is missing.");
        SaveWindow(window, prefix + "-news.png");
        vm.NavigateCommand.Execute("Store"); await Task.Delay(200);
        var scaling = window.RenderScaling;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.ClientSize.Width * scaling), (int)(window.ClientSize.Height * scaling)), new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(window); bitmap.Save(prefix + ".png", PngBitmapEncoderOptions.Default);
        await File.WriteAllTextAsync(prefix + ".json", JsonSerializer.Serialize(new { version = vm.AppVersion, platform = Environment.OSVersion.ToString(), visible = window.IsVisible, tools = vm.Tools.Count, visibleTools = vm.VisibleTools.Count(), selectedPackage = vm.SelectedPackage?.FileName, status = vm.Status }, AppStorage.Json));
    }
    private static void SaveWindow(Window window, string path)
    {
        var scaling = window.RenderScaling;
        using var image = new RenderTargetBitmap(new PixelSize((int)(window.ClientSize.Width * scaling), (int)(window.ClientSize.Height * scaling)), new Vector(96 * scaling, 96 * scaling));
        image.Render(window); image.Save(path, PngBitmapEncoderOptions.Default);
    }
}
