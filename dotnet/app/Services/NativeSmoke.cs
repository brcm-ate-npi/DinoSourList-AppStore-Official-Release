using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
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
        await Task.Delay(1000);
        if (!window.IsVisible || window.ClientSize.Width < 1 || vm.Tools.Count == 0 || !vm.VisibleTools.Any() || vm.SelectedTool is null || window.FindControl<ComboBox>("PackagePicker")?.SelectedItem is null)
            throw new InvalidOperationException("Native window or cached catalogue failed to initialize: " + vm.Status);
        vm.Query = "Preview"; await vm.RecommendCommand.ExecuteAsync(null);
        if (vm.Recommendations.Count != 1 || !vm.AssistantResponse.Contains("Preview", StringComparison.Ordinal)) throw new InvalidOperationException("Installed assistant/model failed: " + vm.Status);
        var scaling = window.RenderScaling;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.ClientSize.Width * scaling), (int)(window.ClientSize.Height * scaling)), new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(window); bitmap.Save(prefix + ".png", PngBitmapEncoderOptions.Default);
        await File.WriteAllTextAsync(prefix + ".json", JsonSerializer.Serialize(new { platform = Environment.OSVersion.ToString(), visible = window.IsVisible, tools = vm.Tools.Count, visibleTools = vm.VisibleTools.Count(), selectedPackage = vm.SelectedPackage?.FileName, status = vm.Status }, AppStorage.Json));
    }
}
